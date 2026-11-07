using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>A task's history over HTTP (Tasks.md group 16; SPEC section 4.4, "Istoricul unui task").</summary>
public sealed class TaskHistoryEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private async Task<(User Member, string Token, string OtherToken, Guid TaskId)> ATaskOfTwoMembers()
    {
        var (member, token) = await SignedIn([Permissions.TasksCreate]);
        var (other, otherToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(member).WithMember(other));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "Login SSO", priority = "Medium" }));
        return (member, token, otherToken, created.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Get_Returns200WithAPageOfEntriesFromTheNewest()
    {
        var (member, token, otherToken, taskId) = await ATaskOfTwoMembers();
        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/title", token, new { title = "Login cu SSO" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", token, new { assigneeIds = new[] { member.Id } })).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history", otherToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        var page = json.RootElement;
        (page.GetProperty("page").GetInt32(), page.GetProperty("pageSize").GetInt32(), page.GetProperty("totalCount").GetInt32()).Should().Be((1, 50, 2));
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("field").GetString()).Should().Equal("Assignees", "Title");

        var assigned = items[0];
        assigned.GetProperty("oldValue").GetString().Should().BeEmpty();
        assigned.GetProperty("newValue").GetString().Should().Be(member.Id.ToString());
        assigned.GetProperty("changedBy").GetProperty("userId").GetGuid().Should().Be(member.Id);
        assigned.GetProperty("changedBy").GetProperty("displayName").GetString().Should().Be(member.DisplayName);
        assigned.GetProperty("names").GetProperty(member.Id.ToString()).GetString().Should().Be(member.DisplayName);
        assigned.GetProperty("changedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        var renamed = items[1];
        (renamed.GetProperty("oldValue").GetString(), renamed.GetProperty("newValue").GetString()).Should().Be(("Login SSO", "Login cu SSO"));
        renamed.GetProperty("names").EnumerateObject().Should().BeEmpty();
        renamed.TryGetProperty("id", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_TakesItsPageAndPageSizeFromTheQuery()
    {
        var (_, token, _, taskId) = await ATaskOfTwoMembers();
        foreach (var title in new[] { "Unu", "Doi", "Trei" })
        {
            (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/title", token, new { title })).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history?page=2&pageSize=2", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("totalCount").GetInt32().Should().Be(3);
        json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("newValue").GetString()).Should().Equal("Unu");
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=101", "pageSize")]
    public async Task Get_APageOutOfRange_Returns400OnIt(string query, string field)
    {
        var (_, token, _, taskId) = await ATaskOfTwoMembers();

        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history?{query}", token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_OfAnArchivedTask_Returns404TaskNotFound_ButToTasksArchive()
    {
        var (_, token, otherToken, taskId) = await ATaskOfTwoMembers();
        (await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var (_, archiverToken) = await SignedIn([Permissions.ProjectsViewAll, Permissions.TasksArchive]);

        var refused = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history", otherToken);
        var given = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history", archiverToken);

        (refused.StatusCode, await CodeFrom(refused)).Should().Be((HttpStatusCode.NotFound, "Task.NotFound"));
        given.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(given);
        json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("field").GetString()).Should().Equal("ArchivedAt");
    }

    [Fact]
    public async Task Get_ForSomeoneOutsideTheProject_Returns403MemberRequired()
    {
        var (_, _, _, taskId) = await ATaskOfTwoMembers();
        var (_, outsiderToken) = await SignedIn();

        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history", outsiderToken);

        (response.StatusCode, await CodeFrom(response)).Should().Be((HttpStatusCode.Forbidden, "Project.MemberRequired"));
    }

    [Fact]
    public async Task Get_OfATaskThatDoesntExist_Returns404TaskNotFound()
    {
        var (_, token, _, _) = await ATaskOfTwoMembers();

        var response = await Send(HttpMethod.Get, $"/api/tasks/{Guid.CreateVersion7()}/history", token);

        (response.StatusCode, await CodeFrom(response)).Should().Be((HttpStatusCode.NotFound, "Task.NotFound"));
    }

    [Fact]
    public async Task Get_WithoutASession_Returns401()
    {
        var (_, _, _, taskId) = await ATaskOfTwoMembers();

        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
