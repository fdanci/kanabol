using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.AddProjectMember;
using Kanabol.Application.Features.Projects.Commands.ChangeProjectMemberRole;
using Kanabol.Application.Features.Projects.Commands.RemoveProjectMember;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Projects;

/// <summary>Owners manage the members, and a project always keeps an owner (SPEC section 4.4).</summary>
public sealed class ProjectMemberCommandHandlerTests(PostgresFixture postgres) : ProjectHandlerTest(postgres)
{
    private Task<Result<ProjectMemberDto>> Add(Guid projectId, Guid userId, ProjectRole role = ProjectRole.Member) =>
        Handle<AddProjectMemberCommand, ProjectMemberDto>(new AddProjectMemberCommand(projectId, userId, role));

    private Task<Result<ProjectMemberDto>> ChangeRole(Guid projectId, Guid userId, ProjectRole role) =>
        Handle<ChangeProjectMemberRoleCommand, ProjectMemberDto>(new ChangeProjectMemberRoleCommand(projectId, userId, role));

    private Task<Result<Unit>> Remove(Guid projectId, Guid userId) =>
        Handle<RemoveProjectMemberCommand, Unit>(new RemoveProjectMemberCommand(projectId, userId));

    [Fact]
    public async Task Add_ByAnOwner_AddsTheUserWithTheRoleGiven()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var result = await Add(project.Id, user.Id, ProjectRole.Owner);

        result.Value.Should().BeEquivalentTo(new { UserId = user.Id, user.Email, user.DisplayName, Role = ProjectRole.Owner, IsActive = true });
        (await MembersOf(project.Id)).Should().ContainSingle(member => member.UserId == user.Id && member.Role == ProjectRole.Owner);
    }

    [Fact]
    public async Task Add_WithProjectsManage_AddsToAProjectTheyArentIn()
    {
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject();
        await SignInWith(Permissions.ProjectsManage);

        (await Add(project.Id, user.Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Add_ByAMember_ReturnsOwnerRequiredAndAddsNobody()
    {
        var member = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.WithMember(member));
        SignIn(member);

        (await Add(project.Id, user.Id)).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await MembersOf(project.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Add_WhenAlreadyAMember_ReturnsAlreadyAMemberAndKeepsTheirRole()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(user));
        SignIn(owner);

        (await Add(project.Id, user.Id, ProjectRole.Owner)).Error.Should().Be(ProjectErrors.AlreadyAMember);
        (await MembersOf(project.Id)).Single(member => member.UserId == user.Id).Role.Should().Be(ProjectRole.Member);
    }

    [Fact]
    public async Task Add_WhenTheUserIsDeactivated_ReturnsAValidationErrorOnUserId()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        await Deactivate(user);
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var result = await Add(project.Id, user.Id);

        result.Error.Should().Be(ProjectErrors.UserNotFound);
        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("UserId");
    }

    [Fact]
    public async Task Add_WhenTheUserDoesNotExist_ReturnsAValidationErrorOnUserId()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await Add(project.Id, Guid.CreateVersion7())).Error.Should().Be(ProjectErrors.UserNotFound);
    }

    [Fact]
    public async Task Add_WhenTheRoleIsNotOneOfTheTwo_ReturnsAValidationErrorOnRole()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var result = await Add(project.Id, user.Id, (ProjectRole)7);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Role");
    }

    [Fact]
    public async Task Add_WhenArchived_ReturnsArchived()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await Archive(project, owner);
        SignIn(owner);

        (await Add(project.Id, user.Id)).Error.Should().Be(ProjectErrors.Archived);
    }

    [Fact]
    public async Task Add_WhenTheProjectDoesNotExist_ReturnsNotFound()
    {
        var user = await CreateUser("dan@example.com");
        await SignInWith(Permissions.ProjectsManage);

        (await Add(Guid.CreateVersion7(), user.Id)).Error.Should().Be(ProjectErrors.NotFound);
    }

    [Fact]
    public async Task ChangeRole_ByAnOwner_MakesAMemberAnOwner()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(user));
        SignIn(owner);

        (await ChangeRole(project.Id, user.Id, ProjectRole.Owner)).Value.Role.Should().Be(ProjectRole.Owner);
        (await MembersOf(project.Id)).Should().OnlyContain(member => member.Role == ProjectRole.Owner);
    }

    [Fact]
    public async Task ChangeRole_OfAnOwnerWhenAnotherStays_MakesThemAMember()
    {
        var owner = await CreateUser();
        var other = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).OwnedBy(other));
        SignIn(owner);

        (await ChangeRole(project.Id, owner.Id, ProjectRole.Member)).Value.Role.Should().Be(ProjectRole.Member);
    }

    [Fact]
    public async Task ChangeRole_OfTheLastOwner_ReturnsLastOwnerAndKeepsThemOwner()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(owner);

        var result = await ChangeRole(project.Id, owner.Id, ProjectRole.Member);

        result.Error.Should().Be(ProjectErrors.LastOwner);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        (await MembersOf(project.Id)).Single(stored => stored.UserId == owner.Id).Role.Should().Be(ProjectRole.Owner);
    }

    [Fact]
    public async Task ChangeRole_OfTheLastOwnerWithProjectsManage_ReturnsLastOwnerToo()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await SignInWith(Permissions.ProjectsManage);

        (await ChangeRole(project.Id, owner.Id, ProjectRole.Member)).Error.Should().Be(ProjectErrors.LastOwner);
    }

    [Fact]
    public async Task ChangeRole_ByAMember_ReturnsOwnerRequired()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(member);

        (await ChangeRole(project.Id, member.Id, ProjectRole.Owner)).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await MembersOf(project.Id)).Single(stored => stored.UserId == member.Id).Role.Should().Be(ProjectRole.Member);
    }

    [Fact]
    public async Task ChangeRole_OfSomeoneNotInTheProject_ReturnsMemberNotFound()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await ChangeRole(project.Id, user.Id, ProjectRole.Owner)).Error.Should().Be(ProjectErrors.MemberNotFound);
    }

    [Fact]
    public async Task ChangeRole_WhenArchived_ReturnsArchived()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        await Archive(project, owner);
        SignIn(owner);

        (await ChangeRole(project.Id, member.Id, ProjectRole.Owner)).Error.Should().Be(ProjectErrors.Archived);
    }

    [Fact]
    public async Task Remove_ByAnOwner_RemovesTheMember()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(owner);

        (await Remove(project.Id, member.Id)).IsSuccess.Should().BeTrue();
        (await MembersOf(project.Id)).Should().ContainSingle(stored => stored.UserId == owner.Id);
    }

    [Fact]
    public async Task Remove_OfAnOwnerWhenAnotherStays_RemovesThem()
    {
        var owner = await CreateUser();
        var other = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).OwnedBy(other));
        SignIn(owner);

        (await Remove(project.Id, owner.Id)).IsSuccess.Should().BeTrue();
        (await MembersOf(project.Id)).Should().ContainSingle(stored => stored.UserId == other.Id);
    }

    [Fact]
    public async Task Remove_OfTheLastOwner_ReturnsLastOwnerAndKeepsThem()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(owner);

        (await Remove(project.Id, owner.Id)).Error.Should().Be(ProjectErrors.LastOwner);
        (await MembersOf(project.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Remove_OfTheLastOwnerWithProjectsManage_ReturnsLastOwnerToo()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await SignInWith(Permissions.ProjectsManage);

        (await Remove(project.Id, owner.Id)).Error.Should().Be(ProjectErrors.LastOwner);
    }

    [Fact]
    public async Task Remove_ByAMember_ReturnsOwnerRequired()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(member);

        (await Remove(project.Id, owner.Id)).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await MembersOf(project.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Remove_OfSomeoneNotInTheProject_ReturnsMemberNotFound()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await Remove(project.Id, user.Id)).Error.Should().Be(ProjectErrors.MemberNotFound);
    }

    [Fact]
    public async Task Remove_WhenArchived_ReturnsArchived()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        await Archive(project, owner);
        SignIn(owner);

        (await Remove(project.Id, member.Id)).Error.Should().Be(ProjectErrors.Archived);
        (await MembersOf(project.Id)).Should().HaveCount(2);
    }

    // "Always an owner" means an active one: the handler gives the entity who is active (SPEC section 4.4).
    [Fact]
    public async Task Remove_OfAnOwnerWhenTheOnlyOtherOwnerIsDeactivated_ReturnsLastOwnerAndKeepsThem()
    {
        var owner = await CreateUser();
        var other = await CreateUser("dan@example.com");
        await Deactivate(other);
        var project = await CreateProject(builder => builder.OwnedBy(owner).OwnedBy(other));
        SignIn(owner);

        (await Remove(project.Id, owner.Id)).Error.Should().Be(ProjectErrors.LastOwner);
        (await MembersOf(project.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ChangeRole_OfAnOwnerWhenTheOnlyOtherOwnerIsDeactivated_ReturnsLastOwnerAndKeepsThemOwner()
    {
        var owner = await CreateUser();
        var other = await CreateUser("dan@example.com");
        await Deactivate(other);
        var project = await CreateProject(builder => builder.OwnedBy(owner).OwnedBy(other));
        SignIn(owner);

        (await ChangeRole(project.Id, owner.Id, ProjectRole.Member)).Error.Should().Be(ProjectErrors.LastOwner);
        (await MembersOf(project.Id)).Should().OnlyContain(member => member.Role == ProjectRole.Owner);
    }

    [Fact]
    public async Task Remove_OfADeactivatedOwnerWhenAnActiveOneStays_RemovesThem()
    {
        var owner = await CreateUser();
        var other = await CreateUser("dan@example.com");
        await Deactivate(other);
        var project = await CreateProject(builder => builder.OwnedBy(owner).OwnedBy(other));
        SignIn(owner);

        (await Remove(project.Id, other.Id)).IsSuccess.Should().BeTrue();
        (await MembersOf(project.Id)).Should().ContainSingle(member => member.UserId == owner.Id);
    }

    [Fact]
    public async Task ChangeRole_OfADeactivatedOwnerWhenAnActiveOneStays_MakesThemAMember()
    {
        var owner = await CreateUser();
        var other = await CreateUser("dan@example.com");
        await Deactivate(other);
        var project = await CreateProject(builder => builder.OwnedBy(owner).OwnedBy(other));
        SignIn(owner);

        (await ChangeRole(project.Id, other.Id, ProjectRole.Member)).Value.Should().BeEquivalentTo(new { UserId = other.Id, Role = ProjectRole.Member, IsActive = false });
    }

    [Fact]
    public async Task Remove_OfTheOnlyOwnerWhoIsDeactivatedWithProjectsManage_ReturnsLastOwner()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        await Deactivate(owner);
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        await SignInWith(Permissions.ProjectsManage);

        (await Remove(project.Id, owner.Id)).Error.Should().Be(ProjectErrors.LastOwner);
        (await ChangeRole(project.Id, owner.Id, ProjectRole.Member)).Error.Should().Be(ProjectErrors.LastOwner);
    }

    [Fact]
    public async Task ChangeRole_WhenNoOwnerIsActive_LetsProjectsManageMakeANewOneAndThenRemoveTheOld()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        await Deactivate(owner);
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        await SignInWith(Permissions.ProjectsManage);

        (await ChangeRole(project.Id, member.Id, ProjectRole.Owner)).IsSuccess.Should().BeTrue();
        (await Remove(project.Id, owner.Id)).IsSuccess.Should().BeTrue();
        (await MembersOf(project.Id)).Should().ContainSingle(stored => stored.UserId == member.Id && stored.Role == ProjectRole.Owner);
    }

    [Fact]
    public async Task Remove_WhenTheProjectDoesNotExist_ReturnsNotFound()
    {
        var user = await CreateUser("dan@example.com");
        await SignInWith(Permissions.ProjectsManage);

        (await Remove(Guid.CreateVersion7(), user.Id)).Error.Should().Be(ProjectErrors.NotFound);
    }
}
