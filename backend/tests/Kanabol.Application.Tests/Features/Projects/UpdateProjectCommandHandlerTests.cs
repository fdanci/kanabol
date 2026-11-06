using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.UpdateProject;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Projects;

public sealed class UpdateProjectCommandHandlerTests(PostgresFixture postgres) : ProjectHandlerTest(postgres)
{
    private Task<Result<ProjectDto>> Update(Guid id, string name = "Kanabol v2", string? description = "Altă descriere") =>
        Handle<UpdateProjectCommand, ProjectDto>(new UpdateProjectCommand(id, name, description));

    [Fact]
    public async Task Handle_ByAnOwner_ChangesNameAndDescriptionButNotTheKey()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        SignIn(owner);

        var result = await Update(project.Id);

        result.Value.Name.Should().Be("Kanabol v2");
        result.Value.Description.Should().Be("Altă descriere");
        result.Value.Key.Should().Be("KAN");
        var saved = await Reload(project);
        saved!.Name.Should().Be("Kanabol v2");
        saved.Key.Should().Be("KAN");
    }

    [Fact]
    public async Task Handle_WithProjectsManage_ChangesAProjectTheyArentIn()
    {
        var project = await CreateProject();
        await SignInWith(Permissions.ProjectsManage);

        (await Update(project.Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ByAMember_ReturnsOwnerRequiredAndChangesNothing()
    {
        var member = await CreateUser();
        var project = await CreateProject(builder => builder.WithName("Kanabol").WithMember(member));
        SignIn(member);

        var result = await Update(project.Id);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
        (await Reload(project))!.Name.Should().Be("Kanabol");
    }

    [Fact]
    public async Task Handle_WithOnlyProjectsViewAll_ReturnsOwnerRequired()
    {
        var project = await CreateProject();
        await SignInWith(Permissions.ProjectsViewAll);

        (await Update(project.Id)).Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    [Fact]
    public async Task Handle_WhenTheProjectDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.ProjectsManage);

        (await Update(Guid.CreateVersion7())).Error.Should().Be(ProjectErrors.NotFound);
    }

    [Fact]
    public async Task Handle_WhenArchived_ReturnsArchivedAndChangesNothing()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithName("Kanabol").OwnedBy(owner));
        await Archive(project, owner);
        SignIn(owner);

        var result = await Update(project.Id);

        result.Error.Should().Be(ProjectErrors.Archived);
        (await Reload(project))!.Name.Should().Be("Kanabol");
    }

    [Fact]
    public async Task Handle_WhenTheNameIsBlank_ReturnsAValidationErrorOnName()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var result = await Update(project.Id, name: " ");

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    [Fact]
    public async Task Handle_WhenTheProjectChangedMeanwhile_ReturnsChangedMeanwhileAndKeepsTheOtherChange()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<UpdateProjectCommand, ProjectDto>(new UpdateProjectCommand(project.Id, "Primul", null)),
            async () => (await HandleInOwnScope<UpdateProjectCommand, ProjectDto>(new UpdateProjectCommand(project.Id, "Al doilea", null))).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(ProjectErrors.ChangedMeanwhile);
        (await Reload(project))!.Name.Should().Be("Al doilea");
    }
}
