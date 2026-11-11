using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.CreateProject;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Projects;

public sealed class CreateProjectCommandHandlerTests(PostgresFixture postgres) : ProjectHandlerTest(postgres)
{
    private Task<Result<ProjectDto>> Create(string name = "Kanabol", string key = "KAN", string? description = "Board-ul echipei") =>
        Handle<CreateProjectCommand, ProjectDto>(new CreateProjectCommand(name, key, description));

    [Fact]
    public async Task Handle_WhenValid_SavesTheProjectWithTheCreatorAsItsOnlyOwner()
    {
        var creator = await SignInWith(Permissions.ProjectsCreate);

        var result = await Create();

        result.Value.Name.Should().Be("Kanabol");
        result.Value.Key.Should().Be("KAN");
        result.Value.Description.Should().Be("Board-ul echipei");
        result.Value.MyRole.Should().Be(ProjectRole.Owner);
        result.Value.CanManage.Should().BeTrue();
        result.Value.ArchivedAt.Should().BeNull();
        result.Value.CreatedAt.Should().Be(Now);

        var saved = await MembersOf(result.Value.Id);
        saved.Should().ContainSingle().Which.Should().BeEquivalentTo(new { UserId = creator.Id, Role = ProjectRole.Owner });
    }

    [Fact]
    public async Task Handle_WhenValid_GivesTheBoardTheFourFixedColumns()
    {
        await SignInWith(Permissions.ProjectsCreate);

        var result = await Create();

        result.Value.Columns.Select(column => (column.Name, column.Position, column.Kind)).Should().Equal(
            ("Backlog", 0, ColumnKind.Backlog), ("În lucru", 1, ColumnKind.InProgress), ("În pauză", 2, ColumnKind.Paused), ("Gata", 3, ColumnKind.Done));

        await using var db = Postgres.CreateContext();
        (await db.BoardColumns.CountAsync(column => column.ProjectId == result.Value.Id)).Should().Be(4);
        (await db.Projects.SingleAsync(project => project.Id == result.Value.Id)).NextTaskNumber.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenTheKeyIsInSmallLetters_StoresItInCapitals()
    {
        await SignInWith(Permissions.ProjectsCreate);

        var result = await Create(key: " kan ");

        result.Value.Key.Should().Be("KAN");
    }

    [Fact]
    public async Task Handle_WhenAnotherProjectHasTheKey_ReturnsKeyTakenAndSavesNothing()
    {
        await SignInWith(Permissions.ProjectsCreate);
        await CreateProject(builder => builder.WithKey("KAN"));

        var result = await Create(key: "kan");

        result.Error.Should().Be(ProjectErrors.KeyTaken);
        await using var db = Postgres.CreateContext();
        (await db.Projects.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenAnArchivedProjectHasTheKey_ReturnsKeyTaken()
    {
        var creator = await SignInWith(Permissions.ProjectsCreate);
        var archived = await CreateProject(builder => builder.WithKey("KAN"));
        await Archive(archived, creator);

        var result = await Create(key: "KAN");

        result.Error.Should().Be(ProjectErrors.KeyTaken);
    }

    [Fact]
    public async Task Handle_WhenTwoRequestsTakeTheSameKeyAtOnce_TheSecondGetsKeyTaken()
    {
        var creator = await SignInWith(Permissions.ProjectsCreate);

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<CreateProjectCommand, ProjectDto>(new CreateProjectCommand("Primul", "KAN", null), creator),
            async () => (await HandleInOwnScope<CreateProjectCommand, ProjectDto>(new CreateProjectCommand("Al doilea", "KAN", null), creator)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(ProjectErrors.KeyTaken);
        await using var db = Postgres.CreateContext();
        (await db.Projects.SingleAsync()).Name.Should().Be("Al doilea");
    }

    [Theory]
    [InlineData("K")]
    [InlineData("KANBOL")]
    [InlineData("K1")]
    [InlineData("KĂN")]
    [InlineData("")]
    public async Task Handle_WhenTheKeyIsNotTwoToFiveLetters_ReturnsAValidationErrorOnKey(string key)
    {
        await SignInWith(Permissions.ProjectsCreate);

        var result = await Create(key: key);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Key");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Kana​bol")]
    public async Task Handle_WhenTheNameIsBlankOrHasHiddenCharacters_ReturnsAValidationErrorOnName(string name)
    {
        await SignInWith(Permissions.ProjectsCreate);

        var result = await Create(name: name);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    [Fact]
    public async Task Handle_WhenTheNameIsTooLong_ReturnsAValidationErrorOnName()
    {
        await SignInWith(Permissions.ProjectsCreate);

        var result = await Create(name: new string('a', 201));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    [Fact]
    public async Task Handle_WhenTheDescriptionHasLinesAndTabs_KeepsThem()
    {
        await SignInWith(Permissions.ProjectsCreate);

        var result = await Create(description: "Prima linie\r\n\tA doua linie");

        result.Value.Description.Should().Be("Prima linie\r\n\tA doua linie");
    }

    [Theory]
    [InlineData("Ascuns​")]
    [InlineData("Nul\0")]
    public async Task Handle_WhenTheDescriptionHasHiddenCharacters_ReturnsAValidationErrorOnDescription(string description)
    {
        await SignInWith(Permissions.ProjectsCreate);

        var result = await Create(description: description);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Description");
    }

    [Fact]
    public async Task Handle_WhenTheDescriptionIsOver2000Characters_ReturnsAValidationErrorOnDescription()
    {
        await SignInWith(Permissions.ProjectsCreate);

        (await Create(description: new string('a', 2000))).IsSuccess.Should().BeTrue();
        var result = await Create(key: "DOI", description: new string('a', 2001));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Description");
    }
}
