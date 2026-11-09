using System.Data.Common;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Realtime;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Infrastructure.Persistence;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// The task number's transaction runs through Npgsql's retrying strategy (SPEC section 5.3, group 20): a
/// connection lost on the way rolls the counter back and the whole of it runs again, with the same number,
/// and a commit whose answer was lost is checked before anything runs again. Either way one task is saved,
/// and the board and its assignees hear of that one only.
/// </summary>
public sealed class TaskNumbersRetryTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private readonly FlakyCommits _commits = new();

    protected override void ConfigureServices(IServiceCollection services) =>
        services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(_commits));

    /// <summary>A project of Zoe's with Dan in it, and what each run of create was given.</summary>
    private async Task<(User Zoe, User Dan, Project Project)> AProject()
    {
        var zoe = await CreateUserNamed("Zoe Ene", "zoe@example.com");
        var dan = await CreateUserNamed("Dan Pop", "dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(zoe).WithMember(dan));
        SignIn(zoe);
        return (zoe, dan, project);
    }

    private static TaskItem ATask(Project project, int number, User by, User assignee) =>
        TaskItem.Create(project, ColumnAt(project, 0), number, "Login SSO", null, TaskPriority.Medium, 0, [assignee.Id], null, by.Id, Now);

    private async Task<TaskItem?> Save(Project project, Func<int, Task<TaskItem>> create) =>
        await Get<ITaskNumbers>().SaveNewTaskAsync(project.Id, create, CancellationToken.None);

    /// <summary>Ends the connection the transaction runs on, as a restart of PostgreSQL would, and waits until it's gone.</summary>
    private async Task DropTheTransactionsConnection()
    {
        var pid = (await Get<AppDbContext>().Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").ToListAsync()).Single();
        var alone = new NpgsqlConnectionStringBuilder(Postgres.ConnectionString) { Pooling = false }.ConnectionString;
        await using var connection = new NpgsqlConnection(alone);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT pg_terminate_backend({pid}, 5000)", connection);
        (await command.ExecuteScalarAsync()).Should().Be(true);
    }

    private async Task ShouldHaveSavedOneTaskHeardOnce(Project project, TaskItem? saved, User dan, int number)
    {
        saved.Should().NotBeNull();
        (await TasksOf(project)).Select(task => (task.Id, task.Number)).Should().Equal((saved!.Id, number));
        (await NextTaskNumberOf(project)).Should().Be(number + 1);
        BoardNotifier.ReceivedCalls().Count(call => (string)call.GetArguments()[1]! == BoardEvents.TaskCreated).Should().Be(1);
        await BoardNotifier.Received(1).NotifyProjectAsync(
            project.Id, BoardEvents.TaskCreated, Arg.Is<TaskEventDto>(sent => sent.TaskId == saved.Id), Arg.Any<CancellationToken>());
        await using var db = Postgres.CreateContext();
        (await db.Notifications.Where(notification => notification.UserId == dan.Id && notification.Type == NotificationType.TaskAssigned)
            .Select(notification => notification.TaskId).ToListAsync()).Should().Equal(saved.Id);
    }

    [Fact]
    public async Task SaveNewTask_WhenTheConnectionDropsInTheTransaction_RunsAgainWithTheSameNumberAndSavesOneTask()
    {
        var (zoe, dan, project) = await AProject();
        var numbers = new List<int>();

        var saved = await Save(project, async number =>
        {
            numbers.Add(number);
            if (numbers.Count == 1)
            {
                await DropTheTransactionsConnection();
            }

            return ATask(project, number, zoe, dan);
        });

        numbers.Should().Equal(1, 1);
        await ShouldHaveSavedOneTaskHeardOnce(project, saved, dan, 1);
    }

    [Fact]
    public async Task SaveNewTask_WhenTheCommitFails_RunsAgainWithTheSameNumberAndTellsOfOneTask()
    {
        var (zoe, dan, project) = await AProject();
        var numbers = new List<int>();
        _commits.FailBeforeCommit = 1;

        var saved = await Save(project, number =>
        {
            numbers.Add(number);
            return Task.FromResult(ATask(project, number, zoe, dan));
        });

        numbers.Should().Equal(1, 1);
        await ShouldHaveSavedOneTaskHeardOnce(project, saved, dan, 1);
    }

    [Fact]
    public async Task SaveNewTask_WhenTheCommitsAnswerIsLost_KeepsTheTaskItCommittedAndMakesNoOther()
    {
        var (zoe, dan, project) = await AProject();
        var numbers = new List<int>();
        _commits.FailAfterCommit = 1;

        var saved = await Save(project, number =>
        {
            numbers.Add(number);
            return Task.FromResult(ATask(project, number, zoe, dan));
        });

        numbers.Should().Equal(1);
        await ShouldHaveSavedOneTaskHeardOnce(project, saved, dan, 1);
    }

    [Fact]
    public async Task SaveNewTask_AfterARetry_LeavesTheNextOneItsOwnNumber()
    {
        var (zoe, dan, project) = await AProject();
        _commits.FailBeforeCommit = 1;
        await Save(project, number => Task.FromResult(ATask(project, number, zoe, dan)));

        var second = await Save(project, number => Task.FromResult(ATask(project, number, zoe, dan)));

        second!.Number.Should().Be(2);
        (await TasksOf(project)).Select(task => task.Number).Should().Equal(1, 2);
    }

    /// <summary>A commit that fails before it reaches PostgreSQL, or whose answer is lost after it did, as a broken connection would.</summary>
    private sealed class FlakyCommits : DbTransactionInterceptor
    {
        public int FailBeforeCommit { get; set; }

        public int FailAfterCommit { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            FailBeforeCommit-- > 0 ? throw Lost() : ValueTask.FromResult(result);

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) =>
            FailAfterCommit-- > 0 ? throw Lost() : Task.CompletedTask;

        private static NpgsqlException Lost() => new("The connection broke", new IOException("Gone"));
    }
}
