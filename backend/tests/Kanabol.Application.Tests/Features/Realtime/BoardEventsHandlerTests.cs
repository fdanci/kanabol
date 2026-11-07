using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Comments;
using Kanabol.Application.Features.Comments.Commands.AddComment;
using Kanabol.Application.Features.Comments.Commands.EditComment;
using Kanabol.Application.Features.Realtime;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.AssignTask;
using Kanabol.Application.Features.Tasks.Commands.ChangeTaskDescription;
using Kanabol.Application.Features.Tasks.Commands.MoveTask;
using Kanabol.Application.Features.Tasks.Commands.RenameTask;
using Kanabol.Application.Features.Tasks.Commands.RestoreTask;
using Kanabol.Application.Features.Tasks.Commands.SetTaskDueDate;
using Kanabol.Application.Features.Tasks.Commands.SetTaskPriority;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Tests.Features.Realtime;

/// <summary>
/// What the board hears of a change (Tasks.md group 14; SPEC section 9): the task's domain events go to the
/// project's group through IBoardNotifier, with small payloads that carry the task, the project, who did it
/// and the connection of the tab that did it, and a notification goes to its user's group.
/// </summary>
public sealed class BoardEventsHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private const string Tab = "sJ0KHkCgN1vD-9_qW2xZtA";

    private static readonly string[] _fieldsInOrder = ["Title", "Description", "Priority", "DueDate", "Assignees"];

    private async Task<(User Owner, User Dan, Project Project, TaskItemDto Task)> ATaskOnTheBoard()
    {
        var owner = await CreateUser();
        var dan = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("RT").OwnedBy(owner).WithMember(dan));
        var task = await CreateTaskAs(owner, project);
        RequestContext.ConnectionId.Returns(Tab);
        BoardNotifier.ClearReceivedCalls();
        return (owner, dan, project, task);
    }

    private List<(Guid ProjectId, string Event, object Payload)> SentToProjects() =>
        [
            .. BoardNotifier.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IBoardNotifier.NotifyProjectAsync))
                .Select(call => call.GetArguments())
                .Select(arguments => ((Guid)arguments[0]!, (string)arguments[1]!, arguments[2]!)),
        ];

    private List<(Guid UserId, string Event, object Payload)> SentToUsers() =>
        [
            .. BoardNotifier.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IBoardNotifier.NotifyUserAsync))
                .Select(call => call.GetArguments())
                .Select(arguments => ((Guid)arguments[0]!, (string)arguments[1]!, arguments[2]!)),
        ];

    [Fact]
    public async Task Create_OfATask_TellsTheProjectTaskCreatedWithWhoAndTheTab()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("RT").OwnedBy(owner));
        RequestContext.ConnectionId.Returns(Tab);

        var task = await CreateTaskAs(owner, project);

        SentToProjects().Should().Equal((project.Id, "TaskCreated", new TaskEventDto(task.Id, project.Id, owner.Id, Tab)));
    }

    [Fact]
    public async Task Create_ForAssignees_AlsoTellsTheProjectTheAssigneesChanged()
    {
        var owner = await CreateUser();
        var dan = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("RT").OwnedBy(owner).WithMember(dan));
        RequestContext.ConnectionId.Returns(Tab);

        var task = await CreateTaskAs(owner, project, assigneeIds: [dan.Id]);

        SentToProjects().Should().Equal(
            (project.Id, "TaskCreated", new TaskEventDto(task.Id, project.Id, owner.Id, Tab)),
            (project.Id, "TaskUpdated", new TaskUpdatedDto(task.Id, project.Id, owner.Id, Tab, "Assignees")));
    }

    [Fact]
    public async Task Move_ToAnotherColumn_TellsTheProjectTaskMovedWithTheColumnAndThePlace()
    {
        var (owner, _, project, task) = await ATaskOnTheBoard();
        SignIn(owner);
        var target = ColumnAt(project, 1);

        (await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, target.Id, Position: null))).IsSuccess.Should().BeTrue();

        SentToProjects().Should().Equal((project.Id, "TaskMoved", new TaskMovedDto(task.Id, project.Id, owner.Id, Tab, target.Id, 0)));
    }

    [Fact]
    public async Task Move_WithinItsColumn_TellsOnlyOfTheTaskMovedNotOfThoseAround()
    {
        var (owner, _, project, first) = await ATaskOnTheBoard();
        await CreateTaskAs(owner, project, "B");
        await CreateTaskAs(owner, project, "C");
        RequestContext.ConnectionId.Returns(Tab);
        BoardNotifier.ClearReceivedCalls();
        SignIn(owner);

        (await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(first.Id, first.ColumnId, 2))).IsSuccess.Should().BeTrue();

        SentToProjects().Should().Equal((project.Id, "TaskMoved", new TaskMovedDto(first.Id, project.Id, owner.Id, Tab, first.ColumnId, 2)));
    }

    [Fact]
    public async Task EachChangeOfAField_TellsTheProjectTaskUpdatedWithTheField()
    {
        var (_, dan, project, task) = await ATaskOnTheBoard();
        SignIn(dan);

        (await Handle<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(task.Id, "Alt titlu"))).IsSuccess.Should().BeTrue();
        (await Handle<ChangeTaskDescriptionCommand, TaskItemDto>(new ChangeTaskDescriptionCommand(task.Id, "Ceva"))).IsSuccess.Should().BeTrue();
        (await Handle<SetTaskPriorityCommand, TaskItemDto>(new SetTaskPriorityCommand(task.Id, TaskPriority.High))).IsSuccess.Should().BeTrue();
        (await Handle<SetTaskDueDateCommand, TaskItemDto>(new SetTaskDueDateCommand(task.Id, Now.AddDays(3)))).IsSuccess.Should().BeTrue();
        (await Handle<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(task.Id, [dan.Id]))).IsSuccess.Should().BeTrue();

        SentToProjects().Should().Equal(
            _fieldsInOrder.Select(field => (project.Id, "TaskUpdated", (object)new TaskUpdatedDto(task.Id, project.Id, dan.Id, Tab, field))));
    }

    [Fact]
    public async Task Assign_TakingEveryoneOff_StillTellsTheProjectTheAssigneesChanged()
    {
        var (owner, dan, project, _) = await ATaskOnTheBoard();
        var task = await CreateTaskAs(owner, project, "Al lui Dan", assigneeIds: [dan.Id]);
        RequestContext.ConnectionId.Returns(Tab);
        BoardNotifier.ClearReceivedCalls();

        (await Handle<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(task.Id, []))).IsSuccess.Should().BeTrue();

        SentToProjects().Should().Equal((project.Id, "TaskUpdated", new TaskUpdatedDto(task.Id, project.Id, owner.Id, Tab, "Assignees")));
    }

    [Fact]
    public async Task ArchiveAndRestore_TellTheProjectTaskArchivedThenTaskRestored()
    {
        var (owner, _, project, task) = await ATaskOnTheBoard();
        SignIn(owner);

        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(task.Id))).IsSuccess.Should().BeTrue();
        (await Handle<RestoreTaskCommand, TaskItemDto>(new RestoreTaskCommand(task.Id))).IsSuccess.Should().BeTrue();

        SentToProjects().Should().Equal(
            (project.Id, "TaskArchived", new TaskEventDto(task.Id, project.Id, owner.Id, Tab)),
            (project.Id, "TaskRestored", new TaskEventDto(task.Id, project.Id, owner.Id, Tab)));
    }

    [Fact]
    public async Task AChange_WithoutAConnection_CarriesNoOrigin()
    {
        var (owner, _, project, task) = await ATaskOnTheBoard();
        RequestContext.ConnectionId.Returns((string?)null);
        SignIn(owner);

        (await Handle<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(task.Id, "Alt titlu"))).IsSuccess.Should().BeTrue();

        SentToProjects().Should().Equal((project.Id, "TaskUpdated", new TaskUpdatedDto(task.Id, project.Id, owner.Id, null, "Title")));
    }

    [Fact]
    public async Task AChangeThatChangesNothing_TellsNobody()
    {
        var (owner, _, _, task) = await ATaskOnTheBoard();
        SignIn(owner);

        (await Handle<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(task.Id, task.Title))).IsSuccess.Should().BeTrue();

        SentToProjects().Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_OfSomeoneNew_TellsThemNotificationCreatedOnTheirOwnGroup()
    {
        var (owner, dan, _, task) = await ATaskOnTheBoard();
        SignIn(owner);

        (await Handle<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(task.Id, [dan.Id]))).IsSuccess.Should().BeTrue();

        await using var db = Postgres.CreateContext();
        var notification = await db.Notifications.SingleAsync(found => found.UserId == dan.Id && found.Type == NotificationType.TaskAssigned);
        SentToUsers().Should().Equal((dan.Id, "NotificationCreated", new NotificationCreatedDto(notification.Id)));
    }

    // Tasks.md group 15: the board reads itself again, for the count on the card.
    [Fact]
    public async Task Comment_OnATask_TellsTheProjectCommentAddedWithTheCommentAndTheTab()
    {
        var (_, dan, project, task) = await ATaskOnTheBoard();
        SignIn(dan);

        var comment = await Handle<AddCommentCommand, CommentDto>(new AddCommentCommand(task.Id, "Merge"));

        SentToProjects().Should().Equal((project.Id, "CommentAdded", new CommentAddedDto(task.Id, project.Id, dan.Id, Tab, comment.Value.Id)));
    }

    [Fact]
    public async Task EditOfAComment_TellsTheBoardNothing()
    {
        var (_, dan, _, task) = await ATaskOnTheBoard();
        SignIn(dan);
        var comment = await Handle<AddCommentCommand, CommentDto>(new AddCommentCommand(task.Id, "Prima forma"));
        BoardNotifier.ClearReceivedCalls();

        (await Handle<EditCommentCommand, CommentDto>(new EditCommentCommand(comment.Value.Id, "A doua forma"))).IsSuccess.Should().BeTrue();

        SentToProjects().Should().BeEmpty();
        SentToUsers().Should().BeEmpty();
    }

    [Fact]
    public async Task AChange_WhenTheHubFails_IsSavedAllTheSameAndTheFailureLogged()
    {
        var (owner, _, project, task) = await ATaskOnTheBoard();
        BoardNotifier.NotifyProjectAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("the hub is down")));
        SignIn(owner);

        var moved = await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, ColumnAt(project, 1).Id, Position: null));

        moved.IsSuccess.Should().BeTrue();
        (await Stored(task.Id))!.ColumnId.Should().Be(ColumnAt(project, 1).Id);
        Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Error
            && entry.Message.Contains(nameof(BoardEventsHandler), StringComparison.Ordinal));
    }
}
