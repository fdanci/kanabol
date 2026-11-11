using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>
/// The estimate over HTTP (SPEC section 4.4, group 22): optional in POST, asked for by PATCH …/position into
/// În lucru, and changed by PUT /api/tasks/{id}/estimate, every answer with estimatedMinutes.
/// </summary>
public sealed class TaskEstimateEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private static Guid Kind(Project project, ColumnKind kind) => project.Columns.Single(column => column.Kind == kind).Id;

    private async Task<(string Token, Project Project, Guid TaskId)> ATask(object? estimatedMinutes = null)
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));
        var created = await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "Login SSO", priority = "Low", estimatedMinutes });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await JsonFrom(created);
        body.RootElement.GetProperty("estimatedMinutes").ValueKind.Should().Be(estimatedMinutes is null ? System.Text.Json.JsonValueKind.Null : System.Text.Json.JsonValueKind.Number);
        return (token, project, body.RootElement.GetProperty("id").GetGuid());
    }

    private static async Task<HashSet<string>> FieldErrorsFrom(HttpResponseMessage response)
    {
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("Validation");
        return body.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name).ToHashSet();
    }

    [Fact]
    public async Task Patch_IntoInProgressWithoutAnEstimate_Returns400OnEstimatedMinutes()
    {
        var (token, project, taskId) = await ATask();

        var response = await Send(HttpMethod.Patch, $"/api/tasks/{taskId}/position", token, new { columnId = Kind(project, ColumnKind.InProgress) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await FieldErrorsFrom(response)).Should().BeEquivalentTo("estimatedMinutes");
    }

    [Fact]
    public async Task Patch_IntoInProgressWithAnEstimate_Returns200WithIt()
    {
        var (token, project, taskId) = await ATask();

        var response = await Send(HttpMethod.Patch, $"/api/tasks/{taskId}/position", token, new { columnId = Kind(project, ColumnKind.InProgress), estimatedMinutes = 240 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("estimatedMinutes").GetInt32().Should().Be(240);
        body.RootElement.GetProperty("columnName").GetString().Should().Be("În lucru");
    }

    [Fact]
    public async Task Post_WithAnEstimate_Returns201WithIt_AndOneOutOfItsLimits400()
    {
        await ATask(estimatedMinutes: 90);
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));

        var response = await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "Prea scurt", priority = "Low", estimatedMinutes = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await FieldErrorsFrom(response)).Should().BeEquivalentTo("estimatedMinutes");
    }

    [Fact]
    public async Task Put_ByAMember_Returns200WithTheEstimate_AndNullBeforeInProgress()
    {
        var (token, _, taskId) = await ATask(estimatedMinutes: 60);

        var changed = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/estimate", token, new { estimatedMinutes = 480 });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await JsonFrom(changed))
        {
            body.RootElement.GetProperty("estimatedMinutes").GetInt32().Should().Be(480);
        }

        var cleared = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/estimate", token, new { estimatedMinutes = (int?)null });
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await JsonFrom(cleared))
        {
            body.RootElement.GetProperty("estimatedMinutes").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Put_NullOnceInProgress_Returns400OnEstimatedMinutes()
    {
        var (token, project, taskId) = await ATask(estimatedMinutes: 60);
        (await Send(HttpMethod.Patch, $"/api/tasks/{taskId}/position", token, new { columnId = Kind(project, ColumnKind.InProgress) })).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/estimate", token, new { estimatedMinutes = (int?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await FieldErrorsFrom(response)).Should().BeEquivalentTo("estimatedMinutes");
    }

    [Fact]
    public async Task Put_ByOneWhoOnlySeesTheProject_Returns403MemberRequired()
    {
        var (_, _, taskId) = await ATask();
        var (_, viewer) = await SignedIn(Permissions.ProjectsViewAll);

        var response = await Send(HttpMethod.Put, $"/api/tasks/{taskId}/estimate", viewer, new { estimatedMinutes = 60 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
    }

    [Fact]
    public async Task Put_OnATaskThatIsNotThere_Returns404()
    {
        var (_, token) = await SignedIn(Permissions.TasksCreate);

        var response = await Send(HttpMethod.Put, $"/api/tasks/{Guid.CreateVersion7()}/estimate", token, new { estimatedMinutes = 60 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("Task.NotFound");
    }
}
