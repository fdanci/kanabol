using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.MoveTask;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// The pause's reason (SPEC section 4.4, group 22): asked for at every entry into În pauză, one line of 1 to
/// 500 characters, written in the history next to the move; gone once the task leaves, without an entry;
/// ignored on any other move.
/// </summary>
public sealed class TaskPauseCommandHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private static BoardColumn Kind(Project project, ColumnKind kind) => project.Columns.Single(column => column.Kind == kind);

    private Task<Result<TaskItemDto>> Move(Guid taskId, BoardColumn column, string? pauseReason = null, int? position = null) =>
        Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(taskId, column.Id, position, EstimatedMinutes: 60, PauseReason: pauseReason));

    private async Task<(User Owner, Project Project, TaskItemDto Task)> ATask()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("PAU").OwnedBy(owner));
        SignIn(owner);
        return (owner, project, (await Create(NewTask(project, "Login SSO"))).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pe două\nrânduri")]
    [InlineData("cu\u0007clopoțel")]
    public async Task Move_IntoPausedWithoutAReasonOrAWrongOne_ReturnsAValidationErrorOnPauseReasonAndMovesNothing(string? reason)
    {
        var (_, project, task) = await ATask();

        var result = await Move(task.Id, Kind(project, ColumnKind.Paused), reason);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("PauseReason");
        var stored = (await Stored(task.Id))!;
        stored.ColumnId.Should().Be(Kind(project, ColumnKind.Backlog).Id);
        stored.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Move_IntoPausedWithAReasonOver500Characters_ReturnsAValidationErrorOnPauseReason()
    {
        var (_, project, task) = await ATask();

        (await Move(task.Id, Kind(project, ColumnKind.Paused), new string('a', 501))).Error
            .Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("PauseReason");
        (await Move(task.Id, Kind(project, ColumnKind.Paused), new string('a', 500))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Move_IntoPausedWithAReason_KeepsItAndWritesItNextToTheMove()
    {
        var (owner, project, task) = await ATask();

        var result = await Move(task.Id, Kind(project, ColumnKind.Paused), " Aștept răspunsul clientului ");

        result.Value.Should().BeEquivalentTo(new { ColumnName = "În pauză", PauseReason = "Aștept răspunsul clientului" });
        var stored = (await Stored(task.Id))!;
        stored.History.Should().HaveCount(2).And.OnlyContain(entry => entry.ChangedAt == Now && entry.ChangedById == owner.Id);
        stored.History.Single(entry => entry.Field == TaskHistoryFields.PauseReason).Should().BeEquivalentTo(
            new { OldValue = (string?)null, NewValue = "Aștept răspunsul clientului" });
    }

    [Fact]
    public async Task Move_OutOfPaused_ForgetsTheReasonWithoutAnEntry()
    {
        var (_, project, task) = await ATask();
        (await Move(task.Id, Kind(project, ColumnKind.Paused), "Aștept")).IsSuccess.Should().BeTrue();

        var result = await Move(task.Id, Kind(project, ColumnKind.InProgress));

        result.Value.PauseReason.Should().BeNull();
        (await Stored(task.Id))!.History.Count(entry => entry.Field == TaskHistoryFields.PauseReason).Should().Be(1);
    }

    [Fact]
    public async Task Move_WithinPaused_AsksForNoReasonAndKeepsIt()
    {
        var (owner, project, task) = await ATask();
        var other = await CreateTaskAs(owner, project, "Al doilea");
        (await Move(task.Id, Kind(project, ColumnKind.Paused), "Aștept")).IsSuccess.Should().BeTrue();
        (await Move(other.Id, Kind(project, ColumnKind.Paused), "Și eu")).IsSuccess.Should().BeTrue();

        var result = await Move(other.Id, Kind(project, ColumnKind.Paused), position: 0);

        result.Value.Should().BeEquivalentTo(new { Position = 0, PauseReason = "Și eu" });
    }

    [Fact]
    public async Task Move_ElsewhereWithAReason_IgnoresIt()
    {
        var (_, project, task) = await ATask();

        var result = await Move(task.Id, Kind(project, ColumnKind.Done), "pe două\nrânduri");

        result.IsSuccess.Should().BeTrue();
        result.Value.PauseReason.Should().BeNull();
    }

    // SPEC section 4.4, the order: the access before the fields the column asks for.
    [Fact]
    public async Task Move_IntoPausedByOneWhoSeesTheProjectThroughAPermissionOnly_ReturnsMemberRequired()
    {
        var (_, project, task) = await ATask();
        await SignInWith(Permissions.ProjectsManage);

        (await Move(task.Id, Kind(project, ColumnKind.Paused))).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    // The deadline alerts don't change: a task on pause stays open, and late.
    [Fact]
    public async Task APausedTask_StaysOpen()
    {
        var (_, project, task) = await ATask();

        (await Move(task.Id, Kind(project, ColumnKind.Paused), "Aștept")).Value.CompletedAt.Should().BeNull();
    }
}
