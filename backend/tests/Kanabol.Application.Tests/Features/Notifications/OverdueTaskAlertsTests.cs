using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Notifications.Commands.NotifyOverdueTasks;
using Kanabol.Application.Features.Realtime;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.AssignTask;
using Kanabol.Application.Features.Tasks.Commands.MoveTask;
using Kanabol.Application.Features.Tasks.Commands.SetTaskDueDate;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Application.Tests.Features.Notifications;

/// <summary>
/// SPEC section 4.3, "Background service": NotifyOverdueTasksCommand gives every open task past its
/// deadline, in an open project, a TaskOverdue notification for each of its assignees and of the project's
/// owners who are active members, "KAN-1: Login SSO is overdue", each person once per task, ever, all in
/// one save. It runs from the worker, without a signed-in user.
/// </summary>
public sealed class OverdueTaskAlertsTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private static readonly DateTimeOffset _anHourAgo = Now.AddHours(-1);

    private async Task<List<Notification>> OverdueNotifications()
    {
        await using var db = Postgres.CreateContext();
        return await db.Notifications.Where(notification => notification.Type == NotificationType.TaskOverdue).ToListAsync();
    }

    private async Task<(User Owner, User Dan, User Eva, User Ioana, Project Project)> ATeam()
    {
        var owner = await CreateUserNamed("Ana Ionescu", "ana@example.com");
        var dan = await CreateUserNamed("Dan Pop", "dan@example.com");
        var eva = await CreateUserNamed("Eva Radu", "eva@example.com");
        var ioana = await CreateUserNamed("Ioana Marin", "ioana@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(dan).WithMember(eva).WithMember(ioana));
        return (owner, dan, eva, ioana, project);
    }

    private async Task<TaskItemDto> ATaskDue(User owner, Project project, DateTimeOffset? dueDate, IReadOnlyList<Guid> assigneeIds, string title = "Login SSO")
    {
        SignIn(owner);
        var result = await Create(NewTask(project, title, assigneeIds: assigneeIds, dueDate: dueDate));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        return result.Value;
    }

    /// <summary>As the worker runs it: in a scope of its own, nobody signed in.</summary>
    private async Task<int> Check()
    {
        CurrentUser.IsAuthenticated.Returns(false);
        CurrentUser.ClearReceivedCalls();
        await using var scope = NewScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<NotifyOverdueTasksCommand, int>>()
            .Handle(new NotifyOverdueTasksCommand(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        _ = CurrentUser.DidNotReceive().Id;
        return result.Value;
    }

    [Fact]
    public async Task Handle_ForAnOverdueTask_NotifiesEachAssigneeAndOwnerOnce()
    {
        var (owner, dan, eva, ioana, project) = await ATeam();
        var task = await ATaskDue(owner, project, _anHourAgo, [dan.Id, eva.Id, owner.Id]);

        var made = await Check();

        made.Should().Be(3);
        var notifications = await OverdueNotifications();
        notifications.Select(notification => notification.UserId).Should().BeEquivalentTo([owner.Id, dan.Id, eva.Id]);
        notifications.Should().AllSatisfy(notification => notification.Should().BeEquivalentTo(new
        {
            Type = NotificationType.TaskOverdue,
            TaskId = (Guid?)task.Id,
            Message = "KAN-1: Login SSO is overdue",
            CreatedAt = Now,
            ReadAt = (DateTimeOffset?)null,
        }));
        notifications.Should().NotContain(notification => notification.UserId == ioana.Id, "Ioana is a member, neither an owner nor an assignee");
    }

    [Fact]
    public async Task Handle_ForAnUnassignedOverdueTask_NotifiesTheOwners()
    {
        var (owner, _, _, _, project) = await ATeam();
        await ATaskDue(owner, project, _anHourAgo, []);

        (await Check()).Should().Be(1);

        (await OverdueNotifications()).Should().ContainSingle().Which.UserId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task Handle_RunAgain_NotifiesNobodyTwice()
    {
        var (owner, dan, _, _, project) = await ATeam();
        await ATaskDue(owner, project, _anHourAgo, [dan.Id]);
        await Check();

        (await Check()).Should().Be(0);

        (await OverdueNotifications()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ForAnAssigneeAddedLater_NotifiesOnlyThem()
    {
        var (owner, dan, eva, _, project) = await ATeam();
        var task = await ATaskDue(owner, project, _anHourAgo, [dan.Id]);
        await Check();
        SignIn(owner);
        (await Handle<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(task.Id, [dan.Id, eva.Id]))).IsSuccess.Should().BeTrue();

        (await Check()).Should().Be(1);

        (await OverdueNotifications()).Where(notification => notification.UserId == eva.Id).Should().ContainSingle();
        (await OverdueNotifications()).Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_AfterTheDeadlineMovedAwayAndPassedAgain_NotifiesNobodyAgain()
    {
        var (owner, dan, _, _, project) = await ATeam();
        var task = await ATaskDue(owner, project, _anHourAgo, [dan.Id]);
        await Check();
        SignIn(owner);
        (await Handle<SetTaskDueDateCommand, TaskItemDto>(new SetTaskDueDateCommand(task.Id, Now.AddDays(1)))).IsSuccess.Should().BeTrue();
        (await Check()).Should().Be(0, "the deadline is in the future now");
        SignIn(owner);
        (await Handle<SetTaskDueDateCommand, TaskItemDto>(new SetTaskDueDateCommand(task.Id, Now.AddMinutes(-5)))).IsSuccess.Should().BeTrue();

        (await Check()).Should().Be(0);

        (await OverdueNotifications()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ForATaskDueNowOrLaterOrWithoutADeadline_NotifiesNobody()
    {
        var (owner, dan, _, _, project) = await ATeam();
        await ATaskDue(owner, project, Now, [dan.Id], "Due now");
        await ATaskDue(owner, project, Now.AddMinutes(1), [dan.Id], "Due later");
        await ATaskDue(owner, project, null, [dan.Id], "No deadline");

        (await Check()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_ForATaskDone_NotifiesNobody()
    {
        var (owner, dan, _, _, project) = await ATeam();
        var task = await ATaskDue(owner, project, _anHourAgo, [dan.Id]);
        SignIn(owner);
        (await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, DoneColumn(project).Id, null))).IsSuccess.Should().BeTrue();

        (await Check()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_ForAnArchivedTask_NotifiesNobody()
    {
        var (owner, dan, _, _, project) = await ATeam();
        var task = await ATaskDue(owner, project, _anHourAgo, [dan.Id]);
        SignIn(owner);
        (await Handle<ArchiveTaskCommand, Common.Unit>(new ArchiveTaskCommand(task.Id))).IsSuccess.Should().BeTrue();

        (await Check()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_ForATaskOfAnArchivedProject_NotifiesNobody()
    {
        var (owner, dan, _, _, project) = await ATeam();
        await ATaskDue(owner, project, _anHourAgo, [dan.Id]);
        await Archive(project, owner);

        (await Check()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_SkipsWhoeverIsDeactivatedOrOutOfTheProject()
    {
        var (owner, dan, eva, _, project) = await ATeam();
        await ATaskDue(owner, project, _anHourAgo, [dan.Id, eva.Id]);
        await Deactivate(dan);
        await using (var db = Postgres.CreateContext())
        {
            await db.ProjectMembers.Where(member => member.ProjectId == project.Id && member.UserId == eva.Id).ExecuteDeleteAsync();
        }

        (await Check()).Should().Be(1);

        (await OverdueNotifications()).Should().ContainSingle().Which.UserId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task Handle_ForSeveralTasks_NotifiesForEachInOneSave()
    {
        var (owner, dan, eva, _, project) = await ATeam();
        await ATaskDue(owner, project, _anHourAgo, [dan.Id], "Login SSO");
        await ATaskDue(owner, project, Now.AddDays(-3), [eva.Id], "Export CSV");

        (await Check()).Should().Be(4);

        var messages = (await OverdueNotifications()).Select(notification => notification.Message);
        messages.Should().BeEquivalentTo(["KAN-1: Login SSO is overdue", "KAN-1: Login SSO is overdue", "KAN-2: Export CSV is overdue", "KAN-2: Export CSV is overdue"]);
    }

    [Fact]
    public async Task Handle_TellsEachOneNotifiedOnTheirOwnGroup()
    {
        var (owner, dan, _, _, project) = await ATeam();
        await ATaskDue(owner, project, _anHourAgo, [dan.Id]);
        BoardNotifier.ClearReceivedCalls();

        await Check();

        foreach (var notification in await OverdueNotifications())
        {
            await BoardNotifier.Received(1).NotifyUserAsync(notification.UserId, "NotificationCreated", new NotificationCreatedDto(notification.Id), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_WithANotificationOfAnotherType_StillNotifiesTheOverdue()
    {
        var (owner, dan, _, _, project) = await ATeam();
        var task = await ATaskDue(owner, project, _anHourAgo, [dan.Id]);
        await using (var db = Postgres.CreateContext())
        {
            (await db.Notifications.CountAsync(notification => notification.UserId == dan.Id && notification.TaskId == task.Id))
                .Should().Be(1, "Dan was told of the assignment");
        }

        await Check();

        (await OverdueNotifications()).Should().Contain(notification => notification.UserId == dan.Id);
    }

    [Fact]
    public async Task Handle_Always_LogsHowManyItMade()
    {
        var (owner, dan, _, _, project) = await ATeam();
        await ATaskDue(owner, project, _anHourAgo, [dan.Id]);

        await Check();

        Logs.Entries.Should().Contain(entry => entry.Message.Contains("Overdue check made 2 notifications for 1 tasks", StringComparison.Ordinal));
    }
}
