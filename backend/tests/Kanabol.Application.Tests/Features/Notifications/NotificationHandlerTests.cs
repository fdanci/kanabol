using Kanabol.Application.Common;
using Kanabol.Application.Features.Notifications;
using Kanabol.Application.Features.Notifications.Commands.MarkAllNotificationsRead;
using Kanabol.Application.Features.Notifications.Commands.MarkNotificationRead;
using Kanabol.Application.Features.Notifications.Queries.GetNotifications;
using Kanabol.Application.Features.Notifications.Queries.GetUnreadNotificationCount;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.RenameTask;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Notifications;

/// <summary>
/// SPEC section 4.4, "Notificările": the signed-in user's own notifications, a Page from the newest (20 by
/// default, 100 at most), filtered by unread and by type, each with its task as it is now; the unread count;
/// one marked read, the first time kept; all marked read, answering how many. Nobody reaches another's.
/// </summary>
public sealed class NotificationHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private async Task<(User Ana, User Dan, Project Project, TaskItemDto Task)> ATask()
    {
        var ana = await CreateUserNamed("Ana Ionescu", "ana@example.com");
        var dan = await CreateUserNamed("Dan Pop", "dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(ana).WithMember(dan));
        var task = await CreateTaskAs(ana, project, "Login SSO");
        return (ana, dan, project, task);
    }

    /// <summary>Saved through the plain context, minutes from Now, unread unless read is given.</summary>
    private async Task<Notification> Notify(User user, NotificationType type, Guid? taskId, int minutesAgo = 0, string message = "A message", int? readMinutesAgo = null)
    {
        var notification = new Notification(user.Id, type, taskId, message, Now.AddMinutes(-minutesAgo));
        if (readMinutesAgo is { } read)
        {
            notification.MarkRead(Now.AddMinutes(-read));
        }

        await using var db = Postgres.CreateContext();
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification;
    }

    private Task<Result<Page<NotificationDto>>> List(GetNotificationsQuery? query = null) =>
        Ask<GetNotificationsQuery, Page<NotificationDto>>(query ?? new GetNotificationsQuery());

    private async Task<DateTimeOffset?> ReadAtOf(Notification notification)
    {
        await using var db = Postgres.CreateContext();
        return await db.Notifications.Where(found => found.Id == notification.Id).Select(found => found.ReadAt).SingleAsync();
    }

    [Fact]
    public async Task List_GivesOnlyTheCallersFromTheNewest()
    {
        var (ana, dan, _, task) = await ATask();
        var older = await Notify(dan, NotificationType.TaskAssigned, task.Id, minutesAgo: 30);
        var newer = await Notify(dan, NotificationType.CommentAdded, task.Id, minutesAgo: 5);
        await Notify(ana, NotificationType.TaskOverdue, task.Id);
        SignIn(dan);

        var page = (await List()).Value;

        page.Items.Select(item => item.Id).Should().Equal(newer.Id, older.Id);
        (page.Number, page.PageSize, page.TotalCount).Should().Be((1, 20, 2));
    }

    [Fact]
    public async Task List_InTheSameMoment_GoesByIdFromTheLast()
    {
        var (_, dan, _, task) = await ATask();
        var first = await Notify(dan, NotificationType.TaskAssigned, task.Id);
        var second = await Notify(dan, NotificationType.TaskAssigned, task.Id);
        SignIn(dan);

        var page = (await List(new GetNotificationsQuery { PageSize = 1 })).Value;
        var next = (await List(new GetNotificationsQuery { Page = 2, PageSize = 1 })).Value;

        page.Items.Single().Id.Should().Be(second.Id);
        next.Items.Single().Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task List_GivesEachWithItsFieldsAndItsTaskAsItIsNow()
    {
        var (ana, dan, _, task) = await ATask();
        var notification = await Notify(dan, NotificationType.TaskAssigned, task.Id, minutesAgo: 3, message: "Ana Ionescu assigned you KAN-1: Login SSO");
        SignIn(ana);
        (await Handle<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(task.Id, "Login cu SSO"))).IsSuccess.Should().BeTrue();
        SignIn(dan);

        var item = (await List()).Value.Items.Single();

        item.Should().BeEquivalentTo(new NotificationDto(
            notification.Id,
            NotificationType.TaskAssigned,
            "Ana Ionescu assigned you KAN-1: Login SSO",
            Now.AddMinutes(-3),
            null,
            new NotificationTaskDto(task.Id, "KAN-1", "Login cu SSO")));
    }

    [Fact]
    public async Task List_KeepsOneWhoseTaskIsArchivedOrInAnArchivedProject_AndOneWithoutATask()
    {
        var (ana, dan, project, task) = await ATask();
        var other = await CreateTaskAs(ana, project, "Export CSV");
        SignIn(ana);
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(task.Id))).IsSuccess.Should().BeTrue();
        await Archive(project, ana);
        await Notify(dan, NotificationType.TaskOverdue, task.Id, minutesAgo: 2);
        await Notify(dan, NotificationType.CommentAdded, other.Id, minutesAgo: 1);
        await Notify(dan, NotificationType.TaskAssigned, null, message: "Something without a task");
        SignIn(dan);

        var items = (await List()).Value.Items;

        items.Select(item => item.Task?.Key).Should().Equal(null, "KAN-2", "KAN-1");
        items[0].Message.Should().Be("Something without a task");
    }

    [Fact]
    public async Task List_WithUnread_GivesOnlyTheUnread()
    {
        var (_, dan, _, task) = await ATask();
        var unread = await Notify(dan, NotificationType.TaskAssigned, task.Id);
        await Notify(dan, NotificationType.TaskAssigned, task.Id, minutesAgo: 1, readMinutesAgo: 0);
        SignIn(dan);

        var page = (await List(new GetNotificationsQuery { Unread = true })).Value;

        page.Items.Select(item => item.Id).Should().Equal(unread.Id);
        page.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task List_WithTypes_GivesOnlyThose()
    {
        var (_, dan, _, task) = await ATask();
        var overdue = await Notify(dan, NotificationType.TaskOverdue, task.Id, minutesAgo: 2);
        await Notify(dan, NotificationType.TaskAssigned, task.Id, minutesAgo: 1);
        var comment = await Notify(dan, NotificationType.CommentAdded, task.Id);
        SignIn(dan);

        var page = (await List(new GetNotificationsQuery { Types = [NotificationType.TaskOverdue, NotificationType.CommentAdded] })).Value;

        page.Items.Select(item => item.Id).Should().Equal(comment.Id, overdue.Id);
    }

    [Theory]
    [InlineData(0, 20, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 101, "PageSize")]
    public async Task List_WithAPageOutOfRange_IsRefusedOnIt(int page, int pageSize, string field)
    {
        var (_, dan, _, _) = await ATask();
        SignIn(dan);

        var result = await List(new GetNotificationsQuery { Page = page, PageSize = pageSize });

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().Equal(field);
    }

    [Fact]
    public async Task List_OfAHundred_IsAllowed()
    {
        var (_, dan, _, _) = await ATask();
        SignIn(dan);

        (await List(new GetNotificationsQuery { PageSize = 100 })).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UnreadCount_CountsOnlyTheCallersUnread()
    {
        var (ana, dan, _, task) = await ATask();
        await Notify(dan, NotificationType.TaskAssigned, task.Id);
        await Notify(dan, NotificationType.TaskOverdue, null);
        await Notify(dan, NotificationType.CommentAdded, task.Id, readMinutesAgo: 0);
        await Notify(ana, NotificationType.TaskOverdue, task.Id);
        SignIn(dan);

        var count = await Ask<GetUnreadNotificationCountQuery, NotificationCountDto>(new GetUnreadNotificationCountQuery());

        count.Value.Should().Be(new NotificationCountDto(2));
    }

    [Fact]
    public async Task MarkRead_OfAnUnreadOne_ReadsItNowAndGivesIt()
    {
        var (_, dan, _, task) = await ATask();
        var notification = await Notify(dan, NotificationType.TaskAssigned, task.Id, minutesAgo: 10);
        SignIn(dan);

        var result = await Handle<MarkNotificationReadCommand, NotificationDto>(new MarkNotificationReadCommand(notification.Id));

        result.Value.Should().BeEquivalentTo(new { notification.Id, ReadAt = (DateTimeOffset?)Now, Task = new { Key = "KAN-1" } });
        (await ReadAtOf(notification)).Should().Be(Now);
    }

    [Fact]
    public async Task MarkRead_AgainLater_KeepsTheFirstTime()
    {
        var (_, dan, _, task) = await ATask();
        var notification = await Notify(dan, NotificationType.TaskAssigned, task.Id, minutesAgo: 10, readMinutesAgo: 4);
        SignIn(dan);

        var result = await Handle<MarkNotificationReadCommand, NotificationDto>(new MarkNotificationReadCommand(notification.Id));

        result.Value.ReadAt.Should().Be(Now.AddMinutes(-4));
        (await ReadAtOf(notification)).Should().Be(Now.AddMinutes(-4));
    }

    [Fact]
    public async Task MarkRead_OfAnothersOrOfNone_IsNotFound()
    {
        var (ana, dan, _, task) = await ATask();
        var anas = await Notify(ana, NotificationType.TaskOverdue, task.Id);
        SignIn(dan);

        var another = await Handle<MarkNotificationReadCommand, NotificationDto>(new MarkNotificationReadCommand(anas.Id));
        var none = await Handle<MarkNotificationReadCommand, NotificationDto>(new MarkNotificationReadCommand(Guid.CreateVersion7()));

        another.Error.Should().Be(NotificationErrors.NotFound);
        none.Error.Should().Be(NotificationErrors.NotFound);
        NotificationErrors.NotFound.Code.Should().Be("Notification.NotFound");
        (await ReadAtOf(anas)).Should().BeNull();
    }

    [Fact]
    public async Task MarkRead_LeavesTheTaskAlone()
    {
        var (_, dan, _, task) = await ATask();
        var notification = await Notify(dan, NotificationType.TaskAssigned, task.Id);
        var before = await Stored(task.Id);
        SignIn(dan);

        await Handle<MarkNotificationReadCommand, NotificationDto>(new MarkNotificationReadCommand(notification.Id));

        var after = await Stored(task.Id);
        after!.UpdatedAt.Should().Be(before!.UpdatedAt);
        after.History.Should().HaveCount(before.History.Count);
    }

    [Fact]
    public async Task MarkAllRead_MarksTheCallersUnreadNowAndCountsThem()
    {
        var (ana, dan, _, task) = await ATask();
        var first = await Notify(dan, NotificationType.TaskAssigned, task.Id, minutesAgo: 3);
        var second = await Notify(dan, NotificationType.TaskOverdue, null, minutesAgo: 2);
        var earlier = await Notify(dan, NotificationType.CommentAdded, task.Id, minutesAgo: 9, readMinutesAgo: 8);
        var anas = await Notify(ana, NotificationType.TaskOverdue, task.Id);
        SignIn(dan);

        var result = await Handle<MarkAllNotificationsReadCommand, NotificationCountDto>(new MarkAllNotificationsReadCommand());

        result.Value.Should().Be(new NotificationCountDto(2));
        (await ReadAtOf(first)).Should().Be(Now);
        (await ReadAtOf(second)).Should().Be(Now);
        (await ReadAtOf(earlier)).Should().Be(Now.AddMinutes(-8));
        (await ReadAtOf(anas)).Should().BeNull();
    }

    [Fact]
    public async Task MarkAllRead_Again_CountsNone()
    {
        var (_, dan, _, task) = await ATask();
        await Notify(dan, NotificationType.TaskAssigned, task.Id);
        SignIn(dan);
        await Handle<MarkAllNotificationsReadCommand, NotificationCountDto>(new MarkAllNotificationsReadCommand());

        var again = await Handle<MarkAllNotificationsReadCommand, NotificationCountDto>(new MarkAllNotificationsReadCommand());

        again.Value.Should().Be(new NotificationCountDto(0));
    }
}
