using Kanabol.Application.Features.Comments;
using Kanabol.Application.Features.Comments.Commands.AddComment;
using Kanabol.Application.Features.Comments.Commands.EditComment;
using Kanabol.Application.Features.Realtime;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Notifications;

/// <summary>
/// SPEC section 4.3: CommentAddedEvent gives the task's assignees and its creator a CommentAdded
/// notification, each once, but not the comment's author, all with "Ana commented on KAN-1: Login SSO".
/// Editing a comment notifies nobody. Only those who can still open the task hear of it.
/// </summary>
public sealed class CommentAddedNotificationTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private async Task<List<Notification>> NotificationsOf(User user)
    {
        await using var db = Postgres.CreateContext();
        return await db.Notifications.Where(notification => notification.UserId == user.Id).ToListAsync();
    }

    private async Task<int> AllNotifications()
    {
        await using var db = Postgres.CreateContext();
        return await db.Notifications.CountAsync(notification => notification.Type == NotificationType.CommentAdded);
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

    private async Task<CommentDto> Comment(User author, Guid taskId, string body = "Merge")
    {
        SignIn(author);
        var result = await Handle<AddCommentCommand, CommentDto>(new AddCommentCommand(taskId, body));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        return result.Value;
    }

    [Fact]
    public async Task Comment_ByAMember_NotifiesEveryAssigneeAndTheCreatorOnceButNotTheAuthor()
    {
        var (owner, dan, eva, ioana, project) = await ATeam();
        var task = await CreateTaskAs(owner, project, "Login SSO", [dan.Id, eva.Id]);

        await Comment(ioana, task.Id);

        foreach (var follower in new[] { owner, dan, eva })
        {
            (await NotificationsOf(follower)).Where(found => found.Type == NotificationType.CommentAdded).Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                Type = NotificationType.CommentAdded,
                TaskId = task.Id,
                Message = "Ioana Marin commented on KAN-1: Login SSO",
                CreatedAt = Now,
                ReadAt = (DateTimeOffset?)null,
            });
        }

        (await NotificationsOf(ioana)).Should().BeEmpty();
    }

    [Fact]
    public async Task Comment_ByAnAssignee_NotifiesTheOthersButNotThemselves()
    {
        var (owner, dan, eva, _, project) = await ATeam();
        var task = await CreateTaskAs(owner, project, "Login SSO", [dan.Id, eva.Id]);

        await Comment(dan, task.Id);

        (await NotificationsOf(dan)).Where(found => found.Type == NotificationType.CommentAdded).Should().BeEmpty();
        (await AllNotifications()).Should().Be(2, "Ana created it and Eva has it");
    }

    [Fact]
    public async Task Comment_ByTheCreatorOnATaskOnlyTheyHave_NotifiesNobody()
    {
        var (owner, _, _, _, project) = await ATeam();
        var task = await CreateTaskAs(owner, project, "Login SSO", [owner.Id]);

        await Comment(owner, task.Id);

        (await AllNotifications()).Should().Be(0);
    }

    [Fact]
    public async Task Comment_ForACreatorWhoIsAlsoAnAssignee_NotifiesThemOnce()
    {
        var (owner, dan, _, _, project) = await ATeam();
        var task = await CreateTaskAs(owner, project, "Login SSO", [owner.Id]);

        await Comment(dan, task.Id);

        (await NotificationsOf(owner)).Where(found => found.Type == NotificationType.CommentAdded).Should().ContainSingle();
    }

    [Fact]
    public async Task Comment_SkipsAFollowerDeactivatedOrNoLongerInTheProject()
    {
        var (owner, dan, eva, ioana, project) = await ATeam();
        var task = await CreateTaskAs(owner, project, "Login SSO", [dan.Id, eva.Id]);
        await Deactivate(dan);
        await using (var db = Postgres.CreateContext())
        {
            await db.ProjectMembers.Where(member => member.ProjectId == project.Id && member.UserId == eva.Id).ExecuteDeleteAsync();
        }

        await Comment(ioana, task.Id);

        (await NotificationsOf(owner)).Should().ContainSingle(found => found.Type == NotificationType.CommentAdded);
        (await AllNotifications()).Should().Be(1, "Dan is deactivated and Eva left the project");
    }

    [Fact]
    public async Task Edit_OfAComment_NotifiesNobody()
    {
        var (owner, dan, _, _, project) = await ATeam();
        var task = await CreateTaskAs(owner, project, "Login SSO", [dan.Id]);
        var comment = await Comment(dan, task.Id, "Prima forma");
        await using (var db = Postgres.CreateContext())
        {
            await db.Notifications.ExecuteDeleteAsync();
        }

        SignIn(dan);
        (await Handle<EditCommentCommand, CommentDto>(new EditCommentCommand(comment.Id, "A doua forma"))).IsSuccess.Should().BeTrue();

        (await AllNotifications()).Should().Be(0);
    }

    [Fact]
    public async Task Comment_TellsEachOneNotifiedOnTheirOwnGroup()
    {
        var (owner, dan, _, ioana, project) = await ATeam();
        var task = await CreateTaskAs(owner, project, "Login SSO", [dan.Id]);
        BoardNotifier.ClearReceivedCalls();

        await Comment(ioana, task.Id);

        await using var db = Postgres.CreateContext();
        var notifications = await db.Notifications.Where(found => found.Type == NotificationType.CommentAdded).ToListAsync();
        notifications.Should().HaveCount(2);
        foreach (var notification in notifications)
        {
            await BoardNotifier.Received(1).NotifyUserAsync(notification.UserId, "NotificationCreated", new NotificationCreatedDto(notification.Id), Arg.Any<CancellationToken>());
        }
    }
}
