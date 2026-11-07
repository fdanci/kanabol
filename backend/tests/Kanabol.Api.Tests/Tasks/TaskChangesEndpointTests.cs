using System.Net;
using System.Text.Json;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>The changes to a task, its archiving and the project's list, over HTTP (SPEC sections 5.1 and 7.3).</summary>
public sealed class TaskChangesEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private async Task<(User Member, string Token, Guid ProjectId, Guid TaskId)> ATask(params string[] permissions)
    {
        var (member, token) = await SignedIn([Permissions.TasksCreate, .. permissions]);
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(member));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "Login SSO", priority = "Medium" }));
        return (member, token, project.Id, created.RootElement.GetProperty("id").GetGuid());
    }

    private async Task<(User Member, User Other, string Token, Guid ProjectId, Guid TaskId)> ATaskWithAnotherMember()
    {
        var (member, token) = await SignedIn([Permissions.TasksCreate]);
        var other = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(member).WithMember(other));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "Login SSO", priority = "Medium" }));
        return (member, other, token, project.Id, created.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task PutTitle_ByAMember_Returns200WithTheTask()
    {
        var (_, token, _, taskId) = await ATask();

        var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/title", token, new { title = "Login cu SSO" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("title").GetString().Should().Be("Login cu SSO");
    }

    [Fact]
    public async Task PutDescriptionPriorityDueDateAndAssignees_ByAMember_Return200WithEachChange()
    {
        var (member, token, _, taskId) = await ATask();

        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/description", token, new { description = "Cu **Azure AD**" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/priority", token, new { priority = "Critical" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/due-date", token, new { dueDate = "2026-11-20T15:00:00Z" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", token, new { assigneeIds = new[] { member.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("description").GetString().Should().Be("Cu **Azure AD**");
        body.RootElement.GetProperty("priority").GetString().Should().Be("Critical");
        body.RootElement.GetProperty("dueDate").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero));
        body.RootElement.GetProperty("assignees").EnumerateArray().Select(assignee => assignee.GetProperty("userId").GetGuid()).Should().Equal(member.Id);
        body.RootElement.TryGetProperty("assignee", out _).Should().BeFalse();

        await using var db = Api.Postgres.CreateContext();
        (await db.TaskHistoryEntries.Where(entry => entry.TaskId == taskId).Select(entry => entry.Field).ToListAsync())
            .Should().BeEquivalentTo("Description", "Priority", "DueDate", "Assignees");
    }

    [Fact]
    public async Task PutAssignees_ToSomeoneOutsideTheProject_Returns400OnAssigneeIds()
    {
        var (member, token, _, taskId) = await ATask();
        var outsider = await CreateUser(UnusedEmail());

        var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", token, new { assigneeIds = new[] { member.Id, outsider.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("assigneeIds", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PutAssignees_ToTwoMembersThenNobody_Returns200WithTheWholeListEachTime()
    {
        var (member, other, token, _, taskId) = await ATaskWithAnotherMember();

        var both = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", token, new { assigneeIds = new[] { member.Id, other.Id } });
        var nobody = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", token, new { assigneeIds = Array.Empty<Guid>() });

        both.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await JsonFrom(both))
        {
            body.RootElement.GetProperty("assignees").GetArrayLength().Should().Be(2);
        }

        nobody.StatusCode.Should().Be(HttpStatusCode.OK);
        using var emptied = await JsonFrom(nobody);
        emptied.RootElement.GetProperty("assignees").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"assigneeIds\":null}")]
    [InlineData("{\"assigneeIds\":[\"00000000-0000-7000-8000-000000000001\",\"00000000-0000-7000-8000-000000000001\"]}")]
    public async Task PutAssignees_WithoutAListOrWithAnIdTwice_Returns400OnAssigneeIds(string json)
    {
        var (_, token, _, taskId) = await ATask();

        using var request = JsonDocument.Parse(json);
        var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", token, request.RootElement);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("assigneeIds", out _).Should().BeTrue(body.RootElement.ToString());
    }

    // The single assignee is gone with its endpoint (SPEC section 4.4).
    [Fact]
    public async Task PutAssignee_TheOldEndpoint_IsGone()
    {
        var (member, token, _, taskId) = await ATask();

        var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignee", token, new { assigneeId = member.Id });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutPriority_ThatIsNotOneOfTheFour_Returns400OnPriority()
    {
        var (_, token, _, taskId) = await ATask();

        var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/priority", token, new { priority = "Urgent" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("priority", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Put_ByOneWhoOnlySeesTheProject_Returns403MemberRequired()
    {
        var (_, _, _, taskId) = await ATask();
        var (_, viewer) = await SignedIn(Permissions.ProjectsViewAll);

        var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/title", viewer, new { title = "Al meu" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
    }

    [Fact]
    public async Task Delete_ByAMember_Returns204AndTheTaskIsGoneFromTheList()
    {
        var (_, token, projectId, taskId) = await ATask();

        var response = await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Get, $"/api/tasks/{taskId}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var list = await JsonFrom(await Send(HttpMethod.Get, $"/api/projects/{projectId}/tasks", token));
        list.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
        (await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostRestore_WithTasksArchive_Returns200AndTheTaskIsBack()
    {
        var (_, token, projectId, taskId) = await ATask(Permissions.TasksArchive);
        await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", token);

        using var archived = await JsonFrom(await Send(HttpMethod.Get, $"/api/projects/{projectId}/tasks?includeArchived=true", token));
        archived.RootElement.GetProperty("items")[0].GetProperty("archivedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);

        var response = await Send(HttpMethod.Post, $"/api/tasks/{taskId}/restore", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("archivedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task PostRestore_WithoutTasksArchive_Returns403PermissionRequired()
    {
        var (_, token, _, taskId) = await ATask();
        await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", token);

        var response = await Send(HttpMethod.Post, $"/api/tasks/{taskId}/restore", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }

    [Fact]
    public async Task GetList_WithFiltersAndSort_ReturnsAPageOfTheMatchingTasks()
    {
        var (member, other, token, projectId, _) = await ATaskWithAnotherMember();
        await Send(HttpMethod.Post, $"/api/projects/{projectId}/tasks", token, new { title = "Raport", priority = "High", assigneeIds = new[] { member.Id } });
        await Send(HttpMethod.Post, $"/api/projects/{projectId}/tasks", token, new { title = "Logo", priority = "Critical", assigneeIds = new[] { other.Id, member.Id } });

        var response = await Send(HttpMethod.Get, $"/api/projects/{projectId}/tasks?assigneeId={member.Id}&priority=High&priority=Critical&sort=title:asc&pageSize=25", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("items").EnumerateArray().Select(task => task.GetProperty("key").GetString()).Should().Equal("KAN-3", "KAN-2");
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(25);
        body.RootElement.GetProperty("items")[0].GetProperty("priority").GetString().Should().Be("Critical");
        body.RootElement.GetProperty("items")[0].GetProperty("assignees").GetArrayLength().Should().Be(2);
    }

    [Theory]
    [InlineData("sort=color:asc", "sort")]
    [InlineData("priority=Urgent", "priority")]
    [InlineData("pageSize=101", "pageSize")]
    public async Task GetList_WithAParameterThatIsWrong_Returns400OnIt(string parameter, string field)
    {
        var (_, token, projectId, _) = await ATask();

        var response = await Send(HttpMethod.Get, $"/api/projects/{projectId}/tasks?{parameter}", token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetList_WithIncludeArchivedWithoutTasksArchive_Returns403()
    {
        var (_, token, projectId, _) = await ATask();

        var response = await Send(HttpMethod.Get, $"/api/projects/{projectId}/tasks?includeArchived=true", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }
}
