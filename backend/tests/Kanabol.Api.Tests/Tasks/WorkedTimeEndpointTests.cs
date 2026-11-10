using System.Net;
using System.Text.Json;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>
/// The worked time over HTTP (SPEC section 4.4, "Timpul lucrat", group 22): the task, the board's card, the
/// list's row and "Task-urile mele" give estimatedMinutes, workedSeconds, isWorking and pauseReason, in camelCase.
/// </summary>
public sealed class WorkedTimeEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private static string Fields(JsonElement task) =>
        $"{task.GetProperty("estimatedMinutes")} {task.GetProperty("workedSeconds").GetInt64()} {task.GetProperty("isWorking").GetBoolean()} {task.GetProperty("pauseReason").ValueKind}";

    [Fact]
    public async Task EveryTaskDto_GivesTheEstimateTheWorkedTimeWorkingAndThePausesReason()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token,
            new { title = "Login SSO", priority = "Low", estimatedMinutes = 90, assigneeIds = new[] { member.Id } }));
        var taskId = created.RootElement.GetProperty("id").GetGuid();
        Fields(created.RootElement).Should().Be("90 0 False Null");

        var moved = await Send(HttpMethod.Patch, $"/api/tasks/{taskId}/position", token,
            new { columnId = project.Columns.Single(column => column.Kind == ColumnKind.InProgress).Id });

        moved.StatusCode.Should().Be(HttpStatusCode.OK);
        using var task = await JsonFrom(moved);
        using var board = await JsonFrom(await Send(HttpMethod.Get, $"/api/projects/{project.Id}/board", token));
        using var list = await JsonFrom(await Send(HttpMethod.Get, $"/api/projects/{project.Id}/tasks", token));
        using var mine = await JsonFrom(await Send(HttpMethod.Get, "/api/me/tasks", token));
        task.RootElement.GetProperty("isWorking").GetBoolean().Should().BeTrue();
        foreach (var row in new[] { board.RootElement.GetProperty("tasks")[0], list.RootElement.GetProperty("items")[0], mine.RootElement.GetProperty("items")[0] })
        {
            row.GetProperty("id").GetGuid().Should().Be(taskId);
            Fields(row).Should().MatchRegex("^90 [0-9]+ True Null$");
        }
    }
}
