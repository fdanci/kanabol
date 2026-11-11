using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Queries.GetProjectById;
using Kanabol.Application.Features.Projects.Queries.GetProjectMember;
using Kanabol.Application.Features.Projects.Queries.GetProjectMemberCandidates;
using Kanabol.Application.Features.Projects.Queries.GetProjectMembers;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Projects;

/// <summary>A project, its members and who can join it, read by those who may (SPEC section 4.4).</summary>
public sealed class ProjectQueryHandlerTests(PostgresFixture postgres) : ProjectHandlerTest(postgres)
{
    private Task<Result<ProjectDto>> GetProject(Guid id) => Ask<GetProjectByIdQuery, ProjectDto>(new GetProjectByIdQuery(id));

    private Task<Result<ProjectMemberDto[]>> GetMembers(Guid id) => Ask<GetProjectMembersQuery, ProjectMemberDto[]>(new GetProjectMembersQuery(id));

    private Task<Result<MemberCandidateDto[]>> GetCandidates(Guid id, string? search = null) =>
        Ask<GetProjectMemberCandidatesQuery, MemberCandidateDto[]>(new GetProjectMemberCandidatesQuery(id, search));

    [Fact]
    public async Task GetById_ForAMember_ReturnsTheProjectWithTheirRoleAndColumns()
    {
        var member = await CreateUser();
        var project = await CreateProject(builder => builder.WithName("Kanabol").WithKey("KAN").WithDescription("Board").WithMember(member));
        SignIn(member);

        var result = await GetProject(project.Id);

        result.Value.Should().BeEquivalentTo(new { project.Id, Name = "Kanabol", Key = "KAN", Description = "Board", MyRole = ProjectRole.Member, CanManage = false });
        result.Value.Columns.Select(column => column.Name).Should().Equal("Backlog", "În lucru", "În pauză", "Gata");
    }

    [Fact]
    public async Task GetById_ForAnOwner_SaysTheyCanManageIt()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var result = await GetProject(project.Id);

        result.Value.MyRole.Should().Be(ProjectRole.Owner);
        result.Value.CanManage.Should().BeTrue();
    }

    [Fact]
    public async Task GetById_ForSomeoneNotInIt_ReturnsMemberRequired()
    {
        await CreateUser();
        var outsider = await CreateUser("outsider@example.com");
        var project = await CreateProject();
        SignIn(outsider);

        (await GetProject(project.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task GetById_WithProjectsViewAll_ReturnsItWithoutARoleOrManagement()
    {
        var project = await CreateProject();
        await SignInWith(Permissions.ProjectsViewAll);

        var result = await GetProject(project.Id);

        result.Value.MyRole.Should().BeNull();
        result.Value.CanManage.Should().BeFalse();
    }

    [Fact]
    public async Task GetById_WithProjectsManage_SaysTheyCanManageIt()
    {
        var project = await CreateProject();
        await SignInWith(Permissions.ProjectsManage);

        (await GetProject(project.Id)).Value.CanManage.Should().BeTrue();
    }

    [Fact]
    public async Task GetById_WhenArchived_StillReturnsItWithWhen()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await Archive(project, owner);
        SignIn(owner);

        (await GetProject(project.Id)).Value.ArchivedAt.Should().Be(Now);
    }

    [Fact]
    public async Task GetById_WhenItDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.ProjectsViewAll);

        (await GetProject(Guid.CreateVersion7())).Error.Should().Be(ProjectErrors.NotFound);
    }

    [Fact]
    public async Task GetMembers_ForAMember_ListsOwnersFirstThenByName()
    {
        var owner = await CreateUser("zoe@example.com");
        var deactivated = await CreateUser("ana@example.com");
        var member = await CreateUser("bob@example.com");
        await Deactivate(deactivated);
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member).WithMember(deactivated));
        SignIn(member);

        var result = await GetMembers(project.Id);

        result.Value.Select(found => (found.Email, found.Role, found.IsActive)).Should().Equal(
            ("zoe@example.com", ProjectRole.Owner, true),
            ("ana@example.com", ProjectRole.Member, false),
            ("bob@example.com", ProjectRole.Member, true));
    }

    [Fact]
    public async Task GetMembers_ForSomeoneNotInIt_ReturnsMemberRequired()
    {
        var outsider = await CreateUser("outsider@example.com");
        var project = await CreateProject();
        SignIn(outsider);

        (await GetMembers(project.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task GetMembers_WhenTheProjectDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.ProjectsViewAll);

        (await GetMembers(Guid.CreateVersion7())).Error.Should().Be(ProjectErrors.NotFound);
    }

    [Fact]
    public async Task GetMember_ForAMember_ReturnsThatMember()
    {
        var owner = await CreateUser("owner@example.com");
        var member = await CreateUser("bob@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(member);

        var result = await Ask<GetProjectMemberQuery, ProjectMemberDto>(new GetProjectMemberQuery(project.Id, owner.Id));

        result.Value.Should().BeEquivalentTo(new { UserId = owner.Id, Role = ProjectRole.Owner });
    }

    [Fact]
    public async Task GetMember_OfSomeoneNotInIt_ReturnsMemberNotFound()
    {
        var owner = await CreateUser("owner@example.com");
        var outsider = await CreateUser("outsider@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await Ask<GetProjectMemberQuery, ProjectMemberDto>(new GetProjectMemberQuery(project.Id, outsider.Id))).Error.Should().Be(ProjectErrors.MemberNotFound);
    }

    [Fact]
    public async Task GetMember_ForSomeoneNotInIt_ReturnsMemberRequired()
    {
        var owner = await CreateUser("owner@example.com");
        var outsider = await CreateUser("outsider@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(outsider);

        (await Ask<GetProjectMemberQuery, ProjectMemberDto>(new GetProjectMemberQuery(project.Id, owner.Id))).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task GetCandidates_ForAnOwner_ListsActiveUsersNotInTheProject()
    {
        var owner = await CreateUser("owner@example.com");
        var member = await CreateUser("member@example.com");
        var deactivated = await CreateUser("gone@example.com");
        await Deactivate(deactivated);
        var candidate = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(owner);

        var result = await GetCandidates(project.Id);

        result.Value.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = candidate.Id, candidate.Email, candidate.DisplayName });
    }

    [Fact]
    public async Task GetCandidates_WithASearch_MatchesEmailOrNameWhateverTheCase()
    {
        var owner = await CreateUser("owner@example.com");
        await CreateUser("dan.pop@example.com");
        await CreateUser("ion@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await GetCandidates(project.Id, " DAN ")).Value.Select(found => found.Email).Should().Equal("dan.pop@example.com");
        (await GetCandidates(project.Id, "ana pop")).Value.Should().HaveCount(2, "the test users are all called Ana Pop");
    }

    [Fact]
    public async Task GetCandidates_WhenManyMatch_ReturnsTheFirst20()
    {
        var owner = await CreateUser("owner@example.com");
        for (var index = 0; index < 22; index++)
        {
            await CreateUser($"user{index:00}@example.com");
        }

        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await GetCandidates(project.Id)).Value.Should().HaveCount(20);
    }

    [Fact]
    public async Task GetCandidates_ForAMember_ReturnsOwnerRequired()
    {
        var member = await CreateUser();
        var project = await CreateProject(builder => builder.WithMember(member));
        SignIn(member);

        (await GetCandidates(project.Id)).Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    [Fact]
    public async Task GetCandidates_WhenTheSearchIsTooLong_ReturnsAValidationErrorOnSearch()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await GetCandidates(project.Id, new string('a', 101))).Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Should().ContainKey("Search");
    }

    [Fact]
    public async Task GetCandidates_WhenTheProjectDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.ProjectsManage);

        (await GetCandidates(Guid.CreateVersion7())).Error.Should().Be(ProjectErrors.NotFound);
    }
}
