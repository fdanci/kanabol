using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>
/// Several assignees on a task, over HTTP (Tasks.md group 13b; SPEC sections 4.3, 4.4 and 5.1): the whole
/// list in, every assignee out, one history entry per change, a notification only for those newly added.
/// </summary>
public sealed class TaskAssigneesEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private async Task<(User Owner, User Dan, User Eva, string Token, Guid ProjectId, Guid TaskId)> ATaskForDan()
    {
        var (owner, token) = await SignedIn([Permissions.TasksCreate]);
        var dan = await CreateUser(UnusedEmail());
        var eva = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.WithKey("ASG").OwnedBy(owner).WithMember(dan).WithMember(eva));
        using var created = await JsonFrom(await Send(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/tasks",
            token,
            new { title = "Login SSO", priority = "Medium", assigneeIds = new[] { dan.Id } }));
        return (owner, dan, eva, token, project.Id, created.RootElement.GetProperty("id").GetGuid());
    }

    private static IEnumerable<Guid> AssigneesIn(System.Text.Json.JsonDocument task) =>
        task.RootElement.GetProperty("assignees").EnumerateArray().Select(person => person.GetProperty("userId").GetGuid());

    [Fact]
    public async Task PutAssignees_AddingASecondMember_GivesBothOneHistoryEntryAndNotifiesOnlyTheNewOne()
    {
        var (owner, dan, eva, token, _, taskId) = await ATaskForDan();

        var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", token, new { assigneeIds = new[] { dan.Id, eva.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        AssigneesIn(body).Should().BeEquivalentTo([dan.Id, eva.Id]);

        await using var db = Api.Postgres.CreateContext();
        var entry = await db.TaskHistoryEntries.SingleAsync(found => found.TaskId == taskId);
        entry.Field.Should().Be("Assignees");
        entry.OldValue.Should().Be(dan.Id.ToString());
        entry.NewValue.Should().Be(string.Join(',', new[] { dan.Id, eva.Id }.Select(id => id.ToString()).Order(StringComparer.Ordinal)));

        var notified = await db.Notifications
            .Where(notification => notification.TaskId == taskId && notification.Type == NotificationType.TaskAssigned)
            .Select(notification => notification.UserId)
            .ToListAsync();
        notified.Should().BeEquivalentTo([dan.Id, eva.Id], "Dan heard of it when the task was made for him, Eva now, and once each");
        notified.Should().NotContain(owner.Id);
    }

    [Fact]
    public async Task PutAssignees_ToANonMemberADeactivatedMemberOrAnIdTwice_Returns400OnAssigneeIdsAndChangesNothing()
    {
        var (_, dan, eva, token, _, taskId) = await ATaskForDan();
        var outsider = await CreateUser(UnusedEmail());
        await using (var db = Api.Postgres.CreateContext())
        {
            await db.Users.Where(stored => stored.Id == eva.Id).ExecuteUpdateAsync(set => set.SetProperty(stored => stored.IsActive, false));
        }

        foreach (var assigneeIds in new[] { new[] { dan.Id, outsider.Id }, new[] { dan.Id, eva.Id }, new[] { dan.Id, dan.Id } })
        {
            var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", token, new { assigneeIds });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var body = await JsonFrom(response);
            body.RootElement.GetProperty("errors").TryGetProperty("assigneeIds", out _).Should().BeTrue();
        }

        await using var read = Api.Postgres.CreateContext();
        (await read.TaskAssignees.Where(assignee => assignee.TaskId == taskId).Select(assignee => assignee.UserId).ToListAsync())
            .Should().Equal(dan.Id);
        (await read.TaskHistoryEntries.CountAsync(entry => entry.TaskId == taskId)).Should().Be(0);
    }

    [Fact]
    public async Task GetList_ByEitherOfTwoAssignees_FindsTheirTask()
    {
        var (_, dan, eva, token, projectId, taskId) = await ATaskForDan();
        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", token, new { assigneeIds = new[] { eva.Id, dan.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await Send(HttpMethod.Post, $"/api/projects/{projectId}/tasks", token, new { title = "Al Evei", priority = "Low", assigneeIds = new[] { eva.Id } });

        foreach (var (assignee, keys) in new[] { (dan.Id, new[] { "ASG-1" }), (eva.Id, new[] { "ASG-2", "ASG-1" }) })
        {
            var response = await Send(HttpMethod.Get, $"/api/projects/{projectId}/tasks?assigneeId={assignee}", token);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var body = await JsonFrom(response);
            body.RootElement.GetProperty("items").EnumerateArray().Select(task => task.GetProperty("key").GetString()).Should().Equal(keys);
        }
    }

    [Fact]
    public async Task GetBoardAndGetTask_OfATaskWithTwoAssignees_ListThemBoth()
    {
        var (_, dan, eva, token, projectId, taskId) = await ATaskForDan();
        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", token, new { assigneeIds = new[] { eva.Id, dan.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var task = await JsonFrom(await Send(HttpMethod.Get, $"/api/tasks/{taskId}", token));
        using var board = await JsonFrom(await Send(HttpMethod.Get, $"/api/projects/{projectId}/board", token));

        AssigneesIn(task).Should().BeEquivalentTo([dan.Id, eva.Id]);
        board.RootElement.GetProperty("tasks")[0].GetProperty("assignees").GetArrayLength().Should().Be(2);
    }
}
