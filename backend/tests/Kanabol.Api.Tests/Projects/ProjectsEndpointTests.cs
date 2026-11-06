using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;

namespace Kanabol.Api.Tests.Projects;

/// <summary>The status codes, bodies and refusals of the project endpoints (SPEC sections 4.4, 5.1 and 7.3).</summary>
public sealed class ProjectsEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    [Fact]
    public async Task Post_WithProjectsCreate_Returns201WithLocationColumnsAndTheCreatorAsOwner()
    {
        var (creator, token) = await SignedIn(Permissions.ProjectsCreate);

        var response = await Send(HttpMethod.Post, Projects, token, new { name = "Kanabol", key = "kan", description = "Board-ul echipei" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await JsonFrom(response);
        var id = body.RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/api/projects/{id}");
        body.RootElement.GetProperty("key").GetString().Should().Be("KAN");
        body.RootElement.GetProperty("myRole").GetString().Should().Be("Owner");
        body.RootElement.GetProperty("canManage").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("columns").EnumerateArray().Select(column => column.GetProperty("name").GetString())
            .Should().Equal("De făcut", "În lucru", "Gata");
        (await MembersOf(id)).Should().ContainSingle(member => member.UserId == creator.Id && member.Role == ProjectRole.Owner);
    }

    [Fact]
    public async Task Post_WithoutProjectsCreate_Returns403AndCreatesNothing()
    {
        var (_, token) = await SignedIn(Permissions.ProjectsManage, Permissions.ProjectsViewAll);

        var response = await Send(HttpMethod.Post, Projects, token, new { name = "Kanabol", key = "KAN" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }

    [Fact]
    public async Task Post_WhenTheKeyIsTaken_Returns409KeyTaken()
    {
        var (_, token) = await SignedIn(Permissions.ProjectsCreate);
        await CreateProject(builder => builder.WithKey("KAN"));

        var response = await Send(HttpMethod.Post, Projects, token, new { name = "Altul", key = "KAN" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Project.KeyTaken");
    }

    [Fact]
    public async Task Post_WhenTheKeyIsInvalid_Returns400OnKey()
    {
        var (_, token) = await SignedIn(Permissions.ProjectsCreate);

        var response = await Send(HttpMethod.Post, Projects, token, new { name = "Kanabol", key = "K-1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("Validation");
        body.RootElement.GetProperty("errors").TryGetProperty("key", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_WithoutAKey_Returns400OnKey()
    {
        var (_, token) = await SignedIn(Permissions.ProjectsCreate);

        var response = await Send(HttpMethod.Post, Projects, token, new { name = "Kanabol" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("key", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_ForAMember_Returns200WithTheirRole()
    {
        var (member, token) = await SignedIn();
        var project = await CreateProject(builder => builder.WithMember(member));

        var response = await Send(HttpMethod.Get, $"{Projects}/{project.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("myRole").GetString().Should().Be("Member");
        body.RootElement.GetProperty("canManage").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Get_ForSomeoneNotInIt_Returns403MemberRequired()
    {
        var (_, token) = await SignedIn(Permissions.ProjectsCreate, Permissions.TasksCreate);
        var project = await CreateProject();

        var response = await Send(HttpMethod.Get, $"{Projects}/{project.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
    }

    [Fact]
    public async Task Get_WithProjectsViewAll_Returns200WithoutARole()
    {
        var (_, token) = await SignedIn(Permissions.ProjectsViewAll);
        var project = await CreateProject();

        var response = await Send(HttpMethod.Get, $"{Projects}/{project.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("myRole").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_WhenItDoesNotExist_Returns404()
    {
        var (_, token) = await SignedIn(Permissions.ProjectsViewAll);

        var response = await Send(HttpMethod.Get, $"{Projects}/{Guid.CreateVersion7()}", token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("Project.NotFound");
    }

    [Fact]
    public async Task Put_ByAnOwner_Returns200WithTheNewNameAndTheSameKey()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));

        var response = await Send(HttpMethod.Put, $"{Projects}/{project.Id}", token, new { name = "Kanabol v2", key = "ALT", description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("name").GetString().Should().Be("Kanabol v2");
        body.RootElement.GetProperty("key").GetString().Should().Be("KAN");
    }

    [Fact]
    public async Task Put_ByAMember_Returns403OwnerRequired()
    {
        var (member, token) = await SignedIn();
        var project = await CreateProject(builder => builder.WithMember(member));

        var response = await Send(HttpMethod.Put, $"{Projects}/{project.Id}", token, new { name = "Kanabol v2" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.OwnerRequired");
    }

    [Fact]
    public async Task Put_WhenArchived_Returns409Archived()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await ArchiveInDatabase(project);

        var response = await Send(HttpMethod.Put, $"{Projects}/{project.Id}", token, new { name = "Kanabol v2" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Project.Archived");
    }

    [Fact]
    public async Task Delete_ByAnOwner_Returns204AndArchivesWhileGetStillOpensIt()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Delete, $"{Projects}/{project.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var body = await JsonFrom(await Send(HttpMethod.Get, $"{Projects}/{project.Id}", token));
        body.RootElement.GetProperty("archivedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
    }

    [Fact]
    public async Task Delete_ByAMember_Returns403OwnerRequired()
    {
        var (member, token) = await SignedIn();
        var project = await CreateProject(builder => builder.WithMember(member));

        var response = await Send(HttpMethod.Delete, $"{Projects}/{project.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.OwnerRequired");
    }

    [Fact]
    public async Task PostRestore_ByAnOwner_Returns200WithoutArchivedAt()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await ArchiveInDatabase(project);

        var response = await Send(HttpMethod.Post, $"{Projects}/{project.Id}/restore", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("archivedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }
}
