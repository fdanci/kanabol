using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Projects;

/// <summary>
/// Every endpoint of a project, for every kind of caller, over HTTP with a real login (SPEC section 4.4):
/// a member or projects.view_all reads, an owner or projects.manage changes, and nobody else does either.
/// </summary>
public sealed class ProjectAccessMatrixTests(ApiFixture api) : ProjectApiTest(api)
{
    public enum Caller
    {
        Owner,
        Member,
        Outsider,
        ViewAll,
        Manage,
    }

    private enum Needs
    {
        Member,
        Owner,
    }

    private static readonly (string Name, Needs Needs, HttpStatusCode Success)[] _endpoints =
    [
        ("GetProject", Needs.Member, HttpStatusCode.OK),
        ("GetMembers", Needs.Member, HttpStatusCode.OK),
        ("GetMember", Needs.Member, HttpStatusCode.OK),
        ("UpdateProject", Needs.Owner, HttpStatusCode.OK),
        ("ArchiveProject", Needs.Owner, HttpStatusCode.NoContent),
        ("RestoreProject", Needs.Owner, HttpStatusCode.OK),
        ("AddMember", Needs.Owner, HttpStatusCode.Created),
        ("ChangeMemberRole", Needs.Owner, HttpStatusCode.OK),
        ("RemoveMember", Needs.Owner, HttpStatusCode.NoContent),
        ("GetMemberCandidates", Needs.Owner, HttpStatusCode.OK),
    ];

    public static TheoryData<string, Caller, HttpStatusCode, string?> Cases()
    {
        var cases = new TheoryData<string, Caller, HttpStatusCode, string?>();
        foreach (var (name, needs, success) in _endpoints)
        {
            foreach (var caller in Enum.GetValues<Caller>())
            {
                var passes = needs == Needs.Member
                    ? caller is Caller.Owner or Caller.Member or Caller.ViewAll or Caller.Manage
                    : caller is Caller.Owner or Caller.Manage;
                var refusal = needs == Needs.Member ? "Project.MemberRequired" : "Project.OwnerRequired";
                cases.Add(name, caller, passes ? success : HttpStatusCode.Forbidden, passes ? null : refusal);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Endpoint_ForEachKindOfCaller_AnswersAsItsRoleAllows(string endpoint, Caller caller, HttpStatusCode expected, string? code)
    {
        var owner = await CreateUser(UnusedEmail());
        var member = await CreateUser(UnusedEmail());
        var candidate = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        var token = caller switch
        {
            Caller.Owner => await AccessTokenFrom(await Login(owner.Email)),
            Caller.Member => await AccessTokenFrom(await Login(member.Email)),
            Caller.Outsider => await TokenWith(Permissions.ProjectsCreate, Permissions.TasksCreate, Permissions.ReportsView),
            Caller.ViewAll => await TokenWith(Permissions.ProjectsViewAll),
            _ => await TokenWith(Permissions.ProjectsManage),
        };
        var (method, path, body) = RequestFor(endpoint, project, member, candidate);

        var response = await Send(method, path, token, body);

        response.StatusCode.Should().Be(expected, $"{caller} calls {method} {path}");
        if (code is not null)
        {
            (await CodeFrom(response)).Should().Be(code);
        }
    }

    [Fact]
    public void Cases_CoverEveryEndpointOfAProject() =>
        _endpoints.Select(endpoint => endpoint.Name).Should().BeEquivalentTo(
            ["GetProject", "GetMembers", "GetMember", "UpdateProject", "ArchiveProject", "RestoreProject", "AddMember", "ChangeMemberRole", "RemoveMember", "GetMemberCandidates"],
            "the list and the creation of projects are about no single project, and are tested on their own");

    [Fact]
    public async Task Endpoint_ForAnOwnerDeactivatedWithATokenStillValid_IsRefused()
    {
        var owner = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var token = await AccessTokenFrom(await Login(owner.Email));
        await Deactivate(owner);

        var read = await Send(HttpMethod.Get, $"{Projects}/{project.Id}", token);
        var change = await Send(HttpMethod.Put, $"{Projects}/{project.Id}", token, new { name = "Al meu încă" });

        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(read)).Should().Be("Project.MemberRequired");
        change.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(change)).Should().Be("Project.OwnerRequired");
    }

    // Until the deactivated owner is settled (Tasks.md, group 11), deactivating the only owner isn't
    // refused, they stay its owner, and projects.manage hands the project on (decided 2026-11-05).
    [Fact]
    public async Task Project_WhoseOnlyOwnerIsDeactivated_StaysManagedThroughProjectsManage()
    {
        var owner = await CreateUser(UnusedEmail());
        var successor = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var admin = await TokenWith(Permissions.UsersManage, Permissions.ProjectsManage);

        (await Send(HttpMethod.Post, $"/api/users/{owner.Id}/deactivate", admin)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MembersOf(project.Id)).Should().ContainSingle(stored => stored.UserId == owner.Id && stored.Role == ProjectRole.Owner);

        var removedFirst = await Send(HttpMethod.Delete, $"{Projects}/{project.Id}/members/{owner.Id}", admin);
        removedFirst.StatusCode.Should().Be(HttpStatusCode.Conflict, "the deactivated owner is still the only owner");
        (await CodeFrom(removedFirst)).Should().Be("Project.LastOwner");

        (await Send(HttpMethod.Post, $"{Projects}/{project.Id}/members", admin, new { userId = successor.Id, role = "Owner" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Send(HttpMethod.Delete, $"{Projects}/{project.Id}/members/{owner.Id}", admin)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await MembersOf(project.Id)).Should().ContainSingle(stored => stored.UserId == successor.Id && stored.Role == ProjectRole.Owner);
    }

    [Fact]
    public async Task Remove_OfTheLastOwnerByThemselves_Returns409AndKeepsTheProjectOwned()
    {
        var owner = await CreateUser(UnusedEmail());
        var member = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        var token = await AccessTokenFrom(await Login(owner.Email));

        var leaving = await Send(HttpMethod.Delete, $"{Projects}/{project.Id}/members/{owner.Id}", token);
        var stepping = await Send(HttpMethod.Put, $"{Projects}/{project.Id}/members/{owner.Id}", token, new { role = "Member" });

        leaving.StatusCode.Should().Be(HttpStatusCode.Conflict);
        stepping.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(stepping)).Should().Be("Project.LastOwner");
        (await MembersOf(project.Id)).Single(stored => stored.Role == ProjectRole.Owner).UserId.Should().Be(owner.Id);
    }

    private static (HttpMethod Method, string Path, object? Body) RequestFor(string endpoint, Project project, User member, User candidate)
    {
        var root = $"{Projects}/{project.Id}";
        return endpoint switch
        {
            "GetProject" => (HttpMethod.Get, root, null),
            "GetMembers" => (HttpMethod.Get, $"{root}/members", null),
            "GetMember" => (HttpMethod.Get, $"{root}/members/{member.Id}", null),
            "UpdateProject" => (HttpMethod.Put, root, new { name = "Alt nume" }),
            "ArchiveProject" => (HttpMethod.Delete, root, null),
            "RestoreProject" => (HttpMethod.Post, $"{root}/restore", null),
            "AddMember" => (HttpMethod.Post, $"{root}/members", new { userId = candidate.Id, role = "Member" }),
            "ChangeMemberRole" => (HttpMethod.Put, $"{root}/members/{member.Id}", new { role = "Owner" }),
            "RemoveMember" => (HttpMethod.Delete, $"{root}/members/{member.Id}", null),
            "GetMemberCandidates" => (HttpMethod.Get, $"{root}/member-candidates", null),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "No such endpoint in the matrix."),
        };
    }

    private async Task Deactivate(User user)
    {
        await using var db = Api.Postgres.CreateContext();
        await db.Users.Where(stored => stored.Id == user.Id).ExecuteUpdateAsync(set => set.SetProperty(stored => stored.IsActive, false));
    }
}
