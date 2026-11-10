using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>PATCH /api/tasks/{id}/position, as the board calls it (SPEC sections 6.3 and 7.3).</summary>
public sealed class TaskMoveEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    [Fact]
    public async Task Patch_ByAMember_Returns200WithTheTaskInItsNewPlace()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));
        var inProgress = project.Columns.Single(column => column.Position == 1);
        var ids = new List<Guid>();
        foreach (var title in new[] { "A", "B" })
        {
            using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title, priority = "Low" }));
            ids.Add(created.RootElement.GetProperty("id").GetGuid());
        }

        var response = await Send(HttpMethod.Patch, $"/api/tasks/{ids[1]}/position", token, new { columnId = inProgress.Id, position = 0, estimatedMinutes = 60 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("columnId").GetGuid().Should().Be(inProgress.Id);
        body.RootElement.GetProperty("position").GetInt32().Should().Be(0);
        await using var db = Api.Postgres.CreateContext();
        (await db.TaskItems.Where(task => task.Id == ids[0]).Select(task => task.Position).SingleAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Patch_ToANegativePlace_Returns400OnPosition()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "A", priority = "Low" }));

        var response = await Send(HttpMethod.Patch, $"/api/tasks/{created.RootElement.GetProperty("id").GetGuid()}/position", token, new { columnId = project.Columns.First().Id, position = -1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("position", out _).Should().BeTrue();
    }

    // SPEC section 4.4: a task started in În lucru doesn't go back before it.
    [Fact]
    public async Task Patch_FromInProgressBackToBacklog_Returns409CannotMoveBack()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "A", priority = "Low" }));
        var id = created.RootElement.GetProperty("id").GetGuid();
        var backlog = project.Columns.Single(column => column.Position == 0).Id;
        (await Send(HttpMethod.Patch, $"/api/tasks/{id}/position", token, new { columnId = project.Columns.Single(column => column.Position == 1).Id, estimatedMinutes = 60 })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var response = await Send(HttpMethod.Patch, $"/api/tasks/{id}/position", token, new { columnId = backlog });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Task.CannotMoveBack");
    }

    [Fact]
    public async Task Patch_ByOneWhoOnlySeesTheProject_Returns403MemberRequired()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var (_, viewer) = await SignedIn(Permissions.ProjectsViewAll);
        var project = await CreateProject(builder => builder.OwnedBy(member));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "A", priority = "Low" }));

        var response = await Send(HttpMethod.Patch, $"/api/tasks/{created.RootElement.GetProperty("id").GetGuid()}/position", viewer, new { columnId = project.Columns.First().Id, position = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
    }
}
