using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Api.Tests.Projects;

/// <summary>
/// The columns of a board over HTTP (UI spec section 7.4): an owner changes the project's own, whoever sees the
/// project reads them; the four fixed ones answer 409 Column.Fixed to any change (SPEC section 4.4).
/// </summary>
public sealed class BoardColumnsEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private static string ColumnsOf(Guid projectId) => $"{Projects}/{projectId}/board-columns";

    [Fact]
    public async Task Post_ByAnOwner_Returns201WithLocationThatOpensTheColumn()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name = "Review" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await JsonFrom(response);
        var id = body.RootElement.GetProperty("id").GetGuid();
        body.RootElement.GetProperty("position").GetInt32().Should().Be(3, "right before Gata");
        body.RootElement.GetProperty("kind").GetString().Should().Be("Custom");
        response.Headers.Location!.ToString().Should().Be($"{ColumnsOf(project.Id)}/{id}");
        var located = await Send(HttpMethod.Get, response.Headers.Location.ToString(), token);
        located.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // SPEC section 4.4, group 23: a real member whose role has columns.manage; one without gets 403.
    [Fact]
    public async Task Post_ByAMemberWithColumnsManage_Returns201AndWithoutIt403ManagerRequired()
    {
        var (member, token) = await SignedIn(Permissions.ColumnsManage);
        var (plain, plainToken) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithMember(member).WithMember(plain));

        (await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name = "Review" })).StatusCode.Should().Be(HttpStatusCode.Created);
        var refused = await Send(HttpMethod.Post, ColumnsOf(project.Id), plainToken, new { name = "QA" });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var body = await JsonFrom(refused);
        body.RootElement.GetProperty("code").GetString().Should().Be("Column.ManagerRequired");
    }

    [Fact]
    public async Task Get_ForAMember_Returns200WithTheColumnsFromTheLeft()
    {
        var (member, token) = await SignedIn();
        var project = await CreateProject(builder => builder.WithMember(member));

        var response = await Send(HttpMethod.Get, ColumnsOf(project.Id), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.EnumerateArray().Select(column => $"{column.GetProperty("name").GetString()} {column.GetProperty("kind").GetString()}")
            .Should().Equal("Backlog Backlog", "În lucru InProgress", "În pauză Paused", "Gata Done");
        body.RootElement[0].TryGetProperty("isDone", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PutPatchAndDelete_ByAnOwner_RenameMoveAndRemove()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        Guid review;
        using (var body = await JsonFrom(await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name = "Review" })))
        {
            review = body.RootElement.GetProperty("id").GetGuid();
        }

        var renamed = await Send(HttpMethod.Put, $"{ColumnsOf(project.Id)}/{review}", token, new { name = "Verificare" });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await JsonFrom(renamed))
        {
            body.RootElement.GetProperty("name").GetString().Should().Be("Verificare");
            body.RootElement.GetProperty("kind").GetString().Should().Be("Custom");
        }

        var moved = await Send(HttpMethod.Patch, $"{ColumnsOf(project.Id)}/{review}/position", token, new { position = 2 });
        moved.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await JsonFrom(moved))
        {
            body.RootElement.EnumerateArray().Select(column => column.GetProperty("name").GetString())
                .Should().Equal("Backlog", "În lucru", "Verificare", "În pauză", "Gata");
        }

        var deleted = await Send(HttpMethod.Delete, $"{ColumnsOf(project.Id)}/{review}", token);
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_AColumnWithATask_Returns409NotEmpty()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var review = project.AddColumn("Review", owner.Id);
        await using (var db = Api.Postgres.CreateContext())
        {
            db.BoardColumns.Add(review);
            db.TaskItems.Add(new TaskItemBuilder().InProject(project).InColumn(review).WithPriority(TaskPriority.Low).Build());
            await db.SaveChangesAsync();
        }

        var response = await Send(HttpMethod.Delete, $"{ColumnsOf(project.Id)}/{review.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Column.NotEmpty");
    }

    // SPEC section 4.4, group 23.
    [Fact]
    public async Task Patch_AColumnWithATaskAcrossInProgress_Returns409CrossesInProgress()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var review = project.AddColumn("Review", owner.Id);
        await using (var db = Api.Postgres.CreateContext())
        {
            db.BoardColumns.Add(review);
            db.TaskItems.Add(new TaskItemBuilder().InProject(project).InColumn(review).WithPriority(TaskPriority.Low).Build());
            await db.SaveChangesAsync();
        }

        var response = await Send(HttpMethod.Patch, $"{ColumnsOf(project.Id)}/{review.Id}/position", token, new { position = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Column.CrossesInProgress");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task PutPatchAndDelete_OnAFixedColumn_Return409ColumnFixed(int position)
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var column = $"{ColumnsOf(project.Id)}/{project.Columns.Single(found => found.Position == position).Id}";

        var renamed = await Send(HttpMethod.Put, column, token, new { name = "Altceva" });
        var moved = await Send(HttpMethod.Patch, $"{column}/position", token, new { position = 1 });
        var deleted = await Send(HttpMethod.Delete, column, token);

        foreach (var response in new[] { renamed, moved, deleted })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeFrom(response)).Should().Be("Column.Fixed");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Patch_BeforeBacklogOrAfterGata_Returns400OnPosition(int position)
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        using var added = await JsonFrom(await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name = "Review" }));

        var response = await Send(HttpMethod.Patch, $"{ColumnsOf(project.Id)}/{added.RootElement.GetProperty("id").GetGuid()}/position", token, new { position });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("position", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("Gata")]
    [InlineData("în lucru")]
    public async Task Post_WithTheNameOfAFixedColumn_Returns400OnName(string name)
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
    }

    // isDone left POST and PUT (SPEC section 4.4): one sent as before is ignored, and the column is of the project's own.
    [Fact]
    public async Task Post_WithIsDoneAsBefore_MakesAColumnOfTheProjectsOwnAnyway()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name = "Livrat", isDone = true });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("kind").GetString().Should().Be("Custom");
    }

    [Fact]
    public async Task Post_ByAMember_Returns403ManagerRequired()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithMember(member));

        var response = await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name = "Review" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Column.ManagerRequired");
    }

    [Fact]
    public async Task Post_WithoutAName_Returns400OnName()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Post, ColumnsOf(project.Id), token, new { name = " " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Put_OnAColumnThatIsNotThere_Returns404ColumnNotFound()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Put, $"{ColumnsOf(project.Id)}/{Guid.CreateVersion7()}", token, new { name = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("Column.NotFound");
    }
}
