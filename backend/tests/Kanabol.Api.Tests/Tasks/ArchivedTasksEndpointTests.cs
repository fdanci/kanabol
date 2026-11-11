using System.Net;
using System.Text.Json;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>
/// GET /api/projects/{id}/board/archived, the column "Arhivate" (SPEC section 4.4, group 22), and the board's
/// canSeeArchived: tasks.archive from the token, and seeing the project, or 403 and 404.
/// </summary>
public sealed class ArchivedTasksEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    [Fact]
    public async Task Get_WithTasksArchive_Returns200WithAPageOfCardsInCamelCase()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate, Permissions.TasksArchive);
        var project = await CreateProject(builder => builder.WithKey("ARH").OwnedBy(member));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "De arhivat", priority = "Low" }));
        var taskId = created.RootElement.GetProperty("id").GetGuid();
        (await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await Send(HttpMethod.Get, $"/api/projects/{project.Id}/board/archived?page=1&pageSize=10", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(10);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        var card = body.RootElement.GetProperty("items").EnumerateArray().Single();
        card.GetProperty("id").GetGuid().Should().Be(taskId);
        card.GetProperty("key").GetString().Should().Be("ARH-1");
        card.GetProperty("columnName").GetString().Should().Be("Backlog");
        card.GetProperty("priority").GetString().Should().Be("Low");
        card.GetProperty("archivedBy").GetProperty("userId").GetGuid().Should().Be(member.Id);
        card.GetProperty("archivedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        using var board = await JsonFrom(await Send(HttpMethod.Get, $"/api/projects/{project.Id}/board", token));
        board.RootElement.GetProperty("canSeeArchived").GetBoolean().Should().BeTrue();
        board.RootElement.GetProperty("archivedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Get_WithoutTasksArchive_Returns403PermissionRequired_AndTheBoardSaysSo()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));

        var response = await Send(HttpMethod.Get, $"/api/projects/{project.Id}/board/archived", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
        using var board = await JsonFrom(await Send(HttpMethod.Get, $"/api/projects/{project.Id}/board", token));
        board.RootElement.GetProperty("canSeeArchived").GetBoolean().Should().BeFalse();
        board.RootElement.GetProperty("archivedCount").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_OfAProjectTheCallerDoesNotSee_Returns404ProjectNotFound()
    {
        var (_, token) = await SignedIn(Permissions.TasksArchive);
        var project = await CreateProject();

        var response = await Send(HttpMethod.Get, $"/api/projects/{project.Id}/board/archived", token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("Project.NotFound");
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=101", "pageSize")]
    public async Task Get_WithAWrongPage_Returns400OnIt(string query, string field)
    {
        var (member, token) = await SignedIn(Permissions.TasksArchive);
        var project = await CreateProject(builder => builder.OwnedBy(member));

        var response = await Send(HttpMethod.Get, $"/api/projects/{project.Id}/board/archived?{query}", token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).Should().Equal(field);
    }
}
