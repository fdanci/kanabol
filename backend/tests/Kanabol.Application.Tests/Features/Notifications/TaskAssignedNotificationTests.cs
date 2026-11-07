using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.AssignTask;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Notifications;

/// <summary>
/// SPEC section 4.3: TaskAssignedEvent gives each assignee newly added a TaskAssigned notification, not
/// those already on the task, nor whoever made the change. It comes from the domain event, so creating a
/// task for people counts too.
/// </summary>
public sealed class TaskAssignedNotificationTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private async Task<List<Notification>> NotificationsOf(User user)
    {
        await using var db = Postgres.CreateContext();
        return await db.Notifications.Where(notification => notification.UserId == user.Id).ToListAsync();
    }

    private async Task<int> AllNotifications()
    {
        await using var db = Postgres.CreateContext();
        return await db.Notifications.CountAsync();
    }

    private async Task<(User Owner, User Member, Project Project)> AProject()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(member));
        return (owner, member, project);
    }

    private async Task<(User Owner, User Dan, User Eva, User Ioana, Project Project)> ATeam()
    {
        var owner = await CreateUser();
        var dan = await CreateUser("dan@example.com");
        var eva = await CreateUser("eva@example.com");
        var ioana = await CreateUser("ioana@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(dan).WithMember(eva).WithMember(ioana));
        return (owner, dan, eva, ioana, project);
    }

    private Task<Common.Result<TaskItemDto>> Assign(Guid taskId, params Guid[] assigneeIds) =>
        Handle<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(taskId, assigneeIds));

    [Fact]
    public async Task Assign_ToSomeoneElse_NotifiesTheNewAssignee()
    {
        var (owner, member, project) = await AProject();
        var task = await CreateTaskAs(owner, project, "Login SSO");

        (await Assign(task.Id, member.Id)).IsSuccess.Should().BeTrue();

        (await NotificationsOf(member)).Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Type = NotificationType.TaskAssigned,
            TaskId = task.Id,
            Message = $"{owner.DisplayName} assigned you KAN-1: Login SSO",
            CreatedAt = Now,
            ReadAt = (DateTimeOffset?)null,
        });
        (await NotificationsOf(owner)).Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_ToThemselves_NotifiesNobody()
    {
        var (owner, _, project) = await AProject();
        var task = await CreateTaskAs(owner, project);

        (await Assign(task.Id, owner.Id)).IsSuccess.Should().BeTrue();

        (await AllNotifications()).Should().Be(0);
    }

    [Fact]
    public async Task Create_ForSomeoneElse_NotifiesThem()
    {
        var (owner, member, project) = await AProject();

        var task = await CreateTaskAs(owner, project, "Login SSO", assigneeIds: [member.Id]);

        (await NotificationsOf(member)).Should().ContainSingle(notification => notification.TaskId == task.Id && notification.Type == NotificationType.TaskAssigned);
    }

    [Fact]
    public async Task Create_ForThemselves_NotifiesNobody()
    {
        var (owner, _, project) = await AProject();

        await CreateTaskAs(owner, project, assigneeIds: [owner.Id]);

        (await AllNotifications()).Should().Be(0);
    }

    [Fact]
    public async Task Reassign_FromOneToAnother_NotifiesOnlyTheNewOne()
    {
        var (owner, member, project) = await AProject();
        var task = await CreateTaskAs(owner, project, assigneeIds: [owner.Id]);
        SignIn(member);

        (await Assign(task.Id, member.Id)).IsSuccess.Should().BeTrue();
        (await Assign(task.Id, owner.Id)).IsSuccess.Should().BeTrue();

        (await NotificationsOf(owner)).Should().ContainSingle();
        (await NotificationsOf(member)).Should().BeEmpty();
    }

    [Fact]
    public async Task Unassign_NotifiesNobody()
    {
        var (owner, member, project) = await AProject();
        var task = await CreateTaskAs(owner, project, assigneeIds: [member.Id]);

        (await Assign(task.Id)).IsSuccess.Should().BeTrue();

        (await NotificationsOf(member)).Should().ContainSingle();
        (await AllNotifications()).Should().Be(1);
    }

    [Fact]
    public async Task Assign_ToTheSamePersonAgain_NotifiesThemOnlyOnce()
    {
        var (owner, member, project) = await AProject();
        var task = await CreateTaskAs(owner, project);

        (await Assign(task.Id, member.Id)).IsSuccess.Should().BeTrue();
        (await Assign(task.Id, member.Id)).IsSuccess.Should().BeTrue();

        (await NotificationsOf(member)).Should().ContainSingle();
    }

    // Each of those added hears of it, with the same message as for one (SPEC section 4.3).
    [Fact]
    public async Task Assign_ToTwoOthers_NotifiesEachWithTheMessageOfOne()
    {
        var (owner, dan, eva, _, project) = await ATeam();
        var task = await CreateTaskAs(owner, project, "Login SSO");

        (await Assign(task.Id, dan.Id, eva.Id)).IsSuccess.Should().BeTrue();

        foreach (var assignee in new[] { dan, eva })
        {
            (await NotificationsOf(assignee)).Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                Type = NotificationType.TaskAssigned,
                TaskId = task.Id,
                Message = $"{owner.DisplayName} assigned you KAN-1: Login SSO",
                ReadAt = (DateTimeOffset?)null,
            });
        }

        (await AllNotifications()).Should().Be(2);
    }

    [Fact]
    public async Task Assign_AddingSomeoneToThoseAlreadyThere_NotifiesOnlyTheNewOne()
    {
        var (owner, dan, eva, ioana, project) = await ATeam();
        var task = await CreateTaskAs(owner, project, assigneeIds: [dan.Id, eva.Id]);

        (await Assign(task.Id, eva.Id, ioana.Id, dan.Id)).IsSuccess.Should().BeTrue();

        (await NotificationsOf(ioana)).Should().ContainSingle();
        (await NotificationsOf(dan)).Should().ContainSingle();
        (await NotificationsOf(eva)).Should().ContainSingle();
        (await AllNotifications()).Should().Be(3);
    }

    [Fact]
    public async Task Assign_ByOneWhoAddsThemselvesWithOthers_NotifiesOnlyTheOthers()
    {
        var (owner, dan, eva, _, project) = await ATeam();
        var task = await CreateTaskAs(owner, project);
        SignIn(dan);

        (await Assign(task.Id, dan.Id, eva.Id, owner.Id)).IsSuccess.Should().BeTrue();

        (await NotificationsOf(dan)).Should().BeEmpty();
        (await NotificationsOf(eva)).Should().ContainSingle().Which.Message.Should().StartWith($"{dan.DisplayName} assigned you");
        (await NotificationsOf(owner)).Should().ContainSingle();
    }

    [Fact]
    public async Task Create_ForTwoOthersAndThemselves_NotifiesTheTwo()
    {
        var (owner, dan, eva, _, project) = await ATeam();

        var task = await CreateTaskAs(owner, project, "Raport", assigneeIds: [owner.Id, dan.Id, eva.Id]);

        (await NotificationsOf(owner)).Should().BeEmpty();
        (await NotificationsOf(dan)).Should().ContainSingle(notification => notification.TaskId == task.Id);
        (await NotificationsOf(eva)).Should().ContainSingle(notification => notification.TaskId == task.Id);
    }

    [Fact]
    public async Task Assign_TakingSomeoneOffAndKeepingTheRest_NotifiesNobody()
    {
        var (owner, dan, eva, _, project) = await ATeam();
        var task = await CreateTaskAs(owner, project, assigneeIds: [dan.Id, eva.Id]);

        (await Assign(task.Id, eva.Id)).IsSuccess.Should().BeTrue();

        (await AllNotifications()).Should().Be(2);
    }

    [Fact]
    public async Task Assign_ToTwo_LogsEachNotification()
    {
        var (owner, dan, eva, _, project) = await ATeam();
        var task = await CreateTaskAs(owner, project);

        (await Assign(task.Id, dan.Id, eva.Id)).IsSuccess.Should().BeTrue();

        foreach (var assignee in new[] { dan, eva })
        {
            Logs.Entries.Should().ContainSingle(entry => entry.Message.StartsWith($"User {assignee.Id} notified of being assigned task {task.Id}", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Assign_Always_LogsTheNotification()
    {
        var (owner, member, project) = await AProject();
        var task = await CreateTaskAs(owner, project);

        (await Assign(task.Id, member.Id)).IsSuccess.Should().BeTrue();

        Logs.Entries.Should().Contain(entry => entry.Message.StartsWith($"User {member.Id} notified of being assigned task {task.Id}", StringComparison.Ordinal));
    }
}
