using System.Net;
using System.Text.Json;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>
/// A task's history over HTTP (Tasks.md groups 16 and 16b; SPEC section 4.4, "Istoricul unui task"): the list
/// without a description's texts, and one entry with them.
/// </summary>
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

    private async Task<Guid> DescribedAs(string token, Guid taskId, string description)
    {
        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/description", token, new { description })).StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history?pageSize=1", token));
        return json.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Get_AChangeOfTheDescription_ComesWithNullTexts()
    {
        var (_, token, otherToken, taskId) = await ATaskOfTwoMembers();
        await DescribedAs(token, taskId, "Cu **Azure AD**");

        using var json = await JsonFrom(await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history", otherToken));

        var entry = json.RootElement.GetProperty("items").EnumerateArray().Single();
        entry.GetProperty("field").GetString().Should().Be("Description");
        (entry.GetProperty("oldValue").ValueKind, entry.GetProperty("newValue").ValueKind).Should().Be((JsonValueKind.Null, JsonValueKind.Null));
    }

    [Fact]
    public async Task GetEntry_Returns200WithTheEntryAndItsTexts()
    {
        var (member, token, otherToken, taskId) = await ATaskOfTwoMembers();
        await DescribedAs(token, taskId, "Cu **Azure AD**");
        var entryId = await DescribedAs(token, taskId, "Cu **Entra ID**\n- și un rând");

        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{entryId}", otherToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        var entry = json.RootElement;
        entry.GetProperty("id").GetGuid().Should().Be(entryId);
        entry.GetProperty("field").GetString().Should().Be("Description");
        (entry.GetProperty("oldValue").GetString(), entry.GetProperty("newValue").GetString()).Should().Be(("Cu **Azure AD**", "Cu **Entra ID**\n- și un rând"));
        entry.GetProperty("changedBy").GetProperty("userId").GetGuid().Should().Be(member.Id);
        entry.GetProperty("changedBy").GetProperty("displayName").GetString().Should().Be(member.DisplayName);
        entry.GetProperty("changedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        entry.GetProperty("names").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task GetEntry_OfAnotherTasksEntry_Returns404HistoryNotFound()
    {
        var (_, token, _, taskId) = await ATaskOfTwoMembers();
        using var created = await JsonFrom(await Send(HttpMethod.Get, $"/api/tasks/{taskId}", token));
        var projectId = created.RootElement.GetProperty("projectId").GetGuid();
        using var other = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{projectId}/tasks", token, new { title = "Alt task", priority = "Low" }));
        var otherId = other.RootElement.GetProperty("id").GetGuid();
        var othersEntry = await DescribedAs(token, otherId, "Descrierea altui task");

        var refused = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{othersEntry}", token);
        var missing = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{Guid.CreateVersion7()}", token);
        var given = await Send(HttpMethod.Get, $"/api/tasks/{otherId}/history/{othersEntry}", token);

        (refused.StatusCode, await CodeFrom(refused)).Should().Be((HttpStatusCode.NotFound, "History.NotFound"));
        (missing.StatusCode, await CodeFrom(missing)).Should().Be((HttpStatusCode.NotFound, "History.NotFound"));
        given.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEntry_OfAnArchivedTask_Returns404TaskNotFound_ButToTasksArchive()
    {
        var (_, token, otherToken, taskId) = await ATaskOfTwoMembers();
        var entryId = await DescribedAs(token, taskId, "Cu **Azure AD**");
        (await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var (_, archiverToken) = await SignedIn([Permissions.ProjectsViewAll, Permissions.TasksArchive]);

        var refused = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{entryId}", otherToken);
        var given = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{entryId}", archiverToken);

        (refused.StatusCode, await CodeFrom(refused)).Should().Be((HttpStatusCode.NotFound, "Task.NotFound"));
        given.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(given);
        json.RootElement.GetProperty("newValue").GetString().Should().Be("Cu **Azure AD**");
    }

    [Fact]
    public async Task GetEntry_ForSomeoneOutsideTheProject_Returns403MemberRequired()
    {
        var (_, token, _, taskId) = await ATaskOfTwoMembers();
        var entryId = await DescribedAs(token, taskId, "Cu **Azure AD**");
        var (_, outsiderToken) = await SignedIn();

        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{entryId}", outsiderToken);

        (response.StatusCode, await CodeFrom(response)).Should().Be((HttpStatusCode.Forbidden, "Project.MemberRequired"));
    }

    [Fact]
    public async Task GetEntry_OfATaskThatDoesntExist_Returns404TaskNotFound()
    {
        var (_, token, _, taskId) = await ATaskOfTwoMembers();
        var entryId = await DescribedAs(token, taskId, "Cu **Azure AD**");

        var response = await Send(HttpMethod.Get, $"/api/tasks/{Guid.CreateVersion7()}/history/{entryId}", token);

        (response.StatusCode, await CodeFrom(response)).Should().Be((HttpStatusCode.NotFound, "Task.NotFound"));
    }

    [Fact]
    public async Task GetEntry_WithoutASession_Returns401()
    {
        var (_, token, _, taskId) = await ATaskOfTwoMembers();
        var entryId = await DescribedAs(token, taskId, "Cu **Azure AD**");

        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{entryId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
