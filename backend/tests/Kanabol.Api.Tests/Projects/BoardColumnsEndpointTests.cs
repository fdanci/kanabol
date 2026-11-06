using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Api.Tests.Projects;

/// <summary>The columns of a board over HTTP (UI spec section 7.4): an owner changes them, whoever sees the project reads them.</summary>
public sealed class BoardColumnsEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private static string ColumnsOf(Guid projectId) => $"{Projects}/{projectId}/board-columns";

    [Fact]
    public async Task Post_ByAnOwner_Returns201WithLocationThatOpensTheColumn()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name = "Review", isDone = false });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await JsonFrom(response);
        var id = body.RootElement.GetProperty("id").GetGuid();
        body.RootElement.GetProperty("position").GetInt32().Should().Be(3);
        response.Headers.Location!.ToString().Should().Be($"{ColumnsOf(project.Id)}/{id}");
        var located = await Send(HttpMethod.Get, response.Headers.Location.ToString(), token);
        located.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_ForAMember_Returns200WithTheColumnsFromTheLeft()
    {
        var (member, token) = await SignedIn();
        var project = await CreateProject(builder => builder.WithMember(member));

        var response = await Send(HttpMethod.Get, ColumnsOf(project.Id), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.EnumerateArray().Select(column => column.GetProperty("name").GetString()).Should().Equal("De făcut", "În lucru", "Gata");
        body.RootElement[2].GetProperty("isDone").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task PutPatchAndDelete_ByAnOwner_RenameMoveAndRemove()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var columns = project.Columns.OrderBy(column => column.Position).ToList();

        var renamed = await Send(HttpMethod.Put, $"{ColumnsOf(project.Id)}/{columns[1].Id}", token, new { name = "Lucrez", isDone = true });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await JsonFrom(renamed))
        {
            body.RootElement.GetProperty("name").GetString().Should().Be("Lucrez");
            body.RootElement.GetProperty("isDone").GetBoolean().Should().BeTrue();
        }

        var moved = await Send(HttpMethod.Patch, $"{ColumnsOf(project.Id)}/{columns[2].Id}/position", token, new { position = 0 });
        moved.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await JsonFrom(moved))
        {
            body.RootElement.EnumerateArray().Select(column => column.GetProperty("name").GetString()).Should().Equal("Gata", "De făcut", "Lucrez");
        }

        var deleted = await Send(HttpMethod.Delete, $"{ColumnsOf(project.Id)}/{columns[0].Id}", token);
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_AColumnWithATask_Returns409NotEmpty()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await using (var db = Api.Postgres.CreateContext())
        {
            db.TaskItems.Add(new TaskItemBuilder().InProject(project).WithPriority(TaskPriority.Low).Build());
            await db.SaveChangesAsync();
        }

        var response = await Send(HttpMethod.Delete, $"{ColumnsOf(project.Id)}/{project.Columns.Single(column => column.Position == 0).Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Column.NotEmpty");
    }

    [Fact]
    public async Task Post_ByAMember_Returns403OwnerRequired()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithMember(member));

        var response = await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name = "Review", isDone = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.OwnerRequired");
    }

    [Fact]
    public async Task Post_WithoutAName_Returns400OnName()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name = " ", isDone = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Put_OnAColumnThatIsNotThere_Returns404ColumnNotFound()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Put, $"{ColumnsOf(project.Id)}/{Guid.CreateVersion7()}", token, new { name = "X", isDone = false });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("Column.NotFound");
    }
}
