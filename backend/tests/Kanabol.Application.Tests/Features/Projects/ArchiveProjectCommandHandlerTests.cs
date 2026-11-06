using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.ArchiveProject;
using Kanabol.Application.Features.Projects.Commands.RestoreProject;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Projects;

/// <summary>Projects are archived, never deleted (SPEC section 5.1), and an owner can bring them back.</summary>
public sealed class ArchiveProjectCommandHandlerTests(PostgresFixture postgres) : ProjectHandlerTest(postgres)
{
    private Task<Result<Unit>> ArchiveIt(Guid id) => Handle<ArchiveProjectCommand, Unit>(new ArchiveProjectCommand(id));

    private Task<Result<ProjectDto>> RestoreIt(Guid id) => Handle<RestoreProjectCommand, ProjectDto>(new RestoreProjectCommand(id));

    [Fact]
    public async Task Archive_ByAnOwner_KeepsTheRowWithWhenAndWho()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await ArchiveIt(project.Id)).IsSuccess.Should().BeTrue();

        var saved = await Reload(project);
        saved!.ArchivedAt.Should().Be(Now);
        saved.ArchivedById.Should().Be(owner.Id);
        saved.Members.Should().ContainSingle();
        saved.Columns.Should().HaveCount(3);
        await using var db = Postgres.CreateContext();
        (await db.Projects.AnyAsync(stored => stored.Id == project.Id)).Should().BeFalse("archived projects are filtered out");
    }

    [Fact]
    public async Task Archive_WhenAlreadyArchived_SucceedsAndKeepsTheFirstTime()
    {
        var owner = await CreateUser();
        var other = await CreateUser("other@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).OwnedBy(other));
        await Archive(project, other);
        SignIn(owner);

        (await ArchiveIt(project.Id)).IsSuccess.Should().BeTrue();

        (await Reload(project))!.ArchivedById.Should().Be(other.Id);
    }

    [Fact]
    public async Task Archive_WithProjectsManage_ArchivesAProjectTheyArentIn()
    {
        var project = await CreateProject();
        await SignInWith(Permissions.ProjectsManage);

        (await ArchiveIt(project.Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Archive_ByAMember_ReturnsOwnerRequiredAndKeepsItOpen()
    {
        var member = await CreateUser();
        var project = await CreateProject(builder => builder.WithMember(member));
        SignIn(member);

        (await ArchiveIt(project.Id)).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await Reload(project))!.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Archive_WhenTheProjectDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.ProjectsManage);

        (await ArchiveIt(Guid.CreateVersion7())).Error.Should().Be(ProjectErrors.NotFound);
    }

    [Fact]
    public async Task Restore_ByAnOwnerOfAnArchivedProject_BringsItBack()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await Archive(project, owner);
        SignIn(owner);

        var result = await RestoreIt(project.Id);

        result.Value.ArchivedAt.Should().BeNull();
        var saved = await Reload(project);
        saved!.ArchivedAt.Should().BeNull();
        saved.ArchivedById.Should().BeNull();
    }

    [Fact]
    public async Task Restore_WhenNotArchived_SucceedsAndChangesNothing()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await RestoreIt(project.Id)).Value.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Restore_ByAMember_ReturnsOwnerRequiredAndKeepsItArchived()
    {
        var owner = await CreateUser();
        var member = await CreateUser("member@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        await Archive(project, owner);
        SignIn(member);

        (await RestoreIt(project.Id)).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await Reload(project))!.ArchivedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Restore_WithProjectsManage_BringsBackAProjectTheyArentIn()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await Archive(project, owner);
        await SignInWith(Permissions.ProjectsManage);

        (await RestoreIt(project.Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_WhenTheProjectDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.ProjectsManage);

        (await RestoreIt(Guid.CreateVersion7())).Error.Should().Be(ProjectErrors.NotFound);
    }
}
