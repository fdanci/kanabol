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
/// SPEC section 4.3: TaskAssignedEvent gives the new assignee a TaskAssigned notification, unless they
/// assigned it to themselves. It comes from the domain event, so creating a task for someone counts too.
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

    [Fact]
    public async Task Assign_Always_LogsTheNotification()
    {
        var (owner, member, project) = await AProject();
        var task = await CreateTaskAs(owner, project);

        (await Assign(task.Id, member.Id)).IsSuccess.Should().BeTrue();

        Logs.Entries.Should().Contain(entry => entry.Message.StartsWith($"User {member.Id} notified of being assigned task {task.Id}", StringComparison.Ordinal));
    }
}
