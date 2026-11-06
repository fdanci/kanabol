using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;

namespace Kanabol.Api.Tests.Projects;

/// <summary>Members, their roles and who can join, over HTTP; a project always keeps an owner (SPEC section 4.4).</summary>
public sealed class ProjectMembersEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    [Fact]
    public async Task GetMembers_ForAMember_Returns200WithRolesAsText()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Get, $"{Projects}/{project.Id}/members", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        var member = body.RootElement.EnumerateArray().Single();
        member.GetProperty("userId").GetGuid().Should().Be(owner.Id);
        member.GetProperty("role").GetString().Should().Be("Owner");
        member.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetMembers_ForSomeoneNotInIt_Returns403MemberRequired()
    {
        var (_, token) = await SignedIn();
        var project = await CreateProject();

        var response = await Send(HttpMethod.Get, $"{Projects}/{project.Id}/members", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
    }

    [Fact]
    public async Task PostMember_ByAnOwner_Returns201WithLocationThatOpensTheMember()
    {
        var (owner, token) = await SignedIn();
        var user = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Post, $"{Projects}/{project.Id}/members", token, new { userId = user.Id, role = "Member" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Be($"/api/projects/{project.Id}/members/{user.Id}");
        var located = await Send(HttpMethod.Get, response.Headers.Location.ToString(), token);
        located.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(located);
        body.RootElement.GetProperty("role").GetString().Should().Be("Member");
    }

    [Fact]
    public async Task PostMember_WithAnUnknownRole_Returns400OnRole()
    {
        var (owner, token) = await SignedIn();
        var user = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Post, $"{Projects}/{project.Id}/members", token, new { userId = user.Id, role = "Admin" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("role", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PostMember_WhenAlreadyIn_Returns409AlreadyAMember()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Post, $"{Projects}/{project.Id}/members", token, new { userId = owner.Id, role = "Member" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Project.AlreadyAMember");
    }

    [Fact]
    public async Task PostMember_ByAMember_Returns403OwnerRequired()
    {
        var (member, token) = await SignedIn();
        var user = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.WithMember(member));

        var response = await Send(HttpMethod.Post, $"{Projects}/{project.Id}/members", token, new { userId = user.Id, role = "Member" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.OwnerRequired");
    }

    [Fact]
    public async Task PutMember_ByAnOwner_Returns200WithTheNewRole()
    {
        var (owner, token) = await SignedIn();
        var user = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(user));

        var response = await Send(HttpMethod.Put, $"{Projects}/{project.Id}/members/{user.Id}", token, new { role = "Owner" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("role").GetString().Should().Be("Owner");
    }

    [Fact]
    public async Task PutMember_OfTheLastOwner_Returns409LastOwner()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Put, $"{Projects}/{project.Id}/members/{owner.Id}", token, new { role = "Member" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Project.LastOwner");
        (await MembersOf(project.Id)).Single().Role.Should().Be(ProjectRole.Owner);
    }

    [Fact]
    public async Task DeleteMember_ByAnOwner_Returns204()
    {
        var (owner, token) = await SignedIn();
        var user = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(user));

        var response = await Send(HttpMethod.Delete, $"{Projects}/{project.Id}/members/{user.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await MembersOf(project.Id)).Should().ContainSingle(member => member.UserId == owner.Id);
    }

    [Fact]
    public async Task DeleteMember_OfTheLastOwner_Returns409LastOwnerAndKeepsThem()
    {
        var (owner, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Delete, $"{Projects}/{project.Id}/members/{owner.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Project.LastOwner");
        (await MembersOf(project.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteMember_OfTheLastOwnerWithProjectsManage_Returns409LastOwnerToo()
    {
        var (_, token) = await SignedIn(Permissions.ProjectsManage);
        var owner = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Delete, $"{Projects}/{project.Id}/members/{owner.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Project.LastOwner");
    }

    [Fact]
    public async Task DeleteMember_OfSomeoneNotInIt_Returns404MemberNotFound()
    {
        var (owner, token) = await SignedIn();
        var user = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Delete, $"{Projects}/{project.Id}/members/{user.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("Project.MemberNotFound");
    }

    [Fact]
    public async Task GetCandidates_ForAnOwner_Returns200WithoutTheMembers()
    {
        var (owner, token) = await SignedIn();
        var user = await CreateUser("dan.candidat@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var response = await Send(HttpMethod.Get, $"{Projects}/{project.Id}/member-candidates?search=candidat", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.EnumerateArray().Select(candidate => candidate.GetProperty("id").GetGuid()).Should().Equal(user.Id);
    }

    [Fact]
    public async Task GetCandidates_ForAMember_Returns403OwnerRequired()
    {
        var (member, token) = await SignedIn();
        var project = await CreateProject(builder => builder.WithMember(member));

        var response = await Send(HttpMethod.Get, $"{Projects}/{project.Id}/member-candidates", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.OwnerRequired");
    }
}
