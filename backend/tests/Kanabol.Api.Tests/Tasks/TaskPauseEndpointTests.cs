using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>The pause's reason over HTTP (SPEC section 4.4, group 22): PATCH …/position into În pauză asks for pauseReason.</summary>
public sealed class TaskPauseEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private async Task<(string Token, Guid Paused, Guid TaskId)> ATask()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "Login SSO", priority = "Low" }));
        return (token, project.Columns.Single(column => column.Kind == ColumnKind.Paused).Id, created.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Patch_IntoPausedWithoutAReason_Returns400OnPauseReason()
    {
        var (token, paused, taskId) = await ATask();

        var response = await Send(HttpMethod.Patch, $"/api/tasks/{taskId}/position", token, new { columnId = paused });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name).Should().Equal("pauseReason");
    }

    [Fact]
    public async Task Patch_IntoPausedWithAReason_Returns200WithItAndTheHistorySaysIt()
    {
        var (token, paused, taskId) = await ATask();

        var response = await Send(HttpMethod.Patch, $"/api/tasks/{taskId}/position", token, new { columnId = paused, pauseReason = "Aștept răspunsul clientului" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await JsonFrom(response))
        {
            body.RootElement.GetProperty("pauseReason").GetString().Should().Be("Aștept răspunsul clientului");
        }

        using var history = await JsonFrom(await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history", token));
        var entries = history.RootElement.GetProperty("items").EnumerateArray().ToList();
        entries.Select(entry => entry.GetProperty("field").GetString()).Should().BeEquivalentTo("ColumnId", "PauseReason");
        entries.Single(entry => entry.GetProperty("field").GetString() == "PauseReason").GetProperty("newValue").GetString()
            .Should().Be("Aștept răspunsul clientului");
    }
}
