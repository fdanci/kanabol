using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Queries.GetTaskById;
using Kanabol.Application.Features.Tasks.Queries.GetTaskByKey;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// A task opens for whoever can see its project, by id or by its key, as /tasks/KAN-123 does (UI spec
/// section 7.5). An archived one only with includeArchived and tasks.archive (SPEC section 5.1).
/// </summary>
public sealed class TaskQueryHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private Task<Common.Result<TaskItemDto>> ByKey(string key, bool includeArchived = false) =>
        Ask<GetTaskByKeyQuery, TaskItemDto>(new GetTaskByKeyQuery(key, includeArchived));

    private Task<Common.Result<TaskItemDto>> ById(Guid id, bool includeArchived = false) =>
        Ask<GetTaskByIdQuery, TaskItemDto>(new GetTaskByIdQuery(id, includeArchived));

    private async Task ArchiveInDatabase(Guid taskId)
    {
        await using var db = Postgres.CreateContext();
        await db.TaskItems.Where(task => task.Id == taskId).ExecuteUpdateAsync(set => set.SetProperty(task => task.ArchivedAt, Now));
    }

    [Fact]
    public async Task ByKey_OfATask_ReturnsIt()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").WithName("Kanabol").OwnedBy(owner));
        await CreateTaskAs(owner, project, "Unu");
        var second = await CreateTaskAs(owner, project, "Doi");

        var result = await ByKey("KAN-2");

        result.Value.Should().BeEquivalentTo(new { second.Id, Key = "KAN-2", Title = "Doi", ProjectName = "Kanabol", CanChange = true });
    }

    [Theory]
    [InlineData("kan-1")]
    [InlineData(" Kan-1 ")]
    public async Task ByKey_InAnyCase_FindsIt(string key)
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        var task = await CreateTaskAs(owner, project);

        (await ByKey(key)).Value.Id.Should().Be(task.Id);
    }

    [Theory]
    [InlineData("KAN-9")]
    [InlineData("ALT-1")]
    [InlineData("KAN")]
    [InlineData("KAN-01")]
    [InlineData("nu e o cheie")]
    public async Task ByKey_WhenNoTaskHasIt_ReturnsNotFound(string key)
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        await CreateTaskAs(owner, project);

        (await ByKey(key)).Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact]
    public async Task ByKey_ForSomeoneOutsideTheProject_ReturnsMemberRequired()
    {
        var owner = await CreateUser();
        var outsider = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        await CreateTaskAs(owner, project);
        SignIn(outsider);

        (await ByKey("KAN-1")).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task ByKey_WithProjectsViewAll_ReturnsItButNotToChange()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        await CreateTaskAs(owner, project);
        await SignInWith(Permissions.ProjectsViewAll);

        (await ByKey("KAN-1")).Value.CanChange.Should().BeFalse();
    }

    [Fact]
    public async Task ByKey_InAnArchivedProject_ReturnsItButNotToChange()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        await CreateTaskAs(owner, project);
        await Archive(project, owner);

        (await ByKey("KAN-1")).Value.CanChange.Should().BeFalse();
    }

    [Fact]
    public async Task ByKey_OfAnArchivedTask_ReturnsNotFound()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        var task = await CreateTaskAs(owner, project);
        await ArchiveInDatabase(task.Id);

        (await ByKey("KAN-1")).Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact]
    public async Task ByKey_OfAnArchivedTaskWithIncludeArchivedAndTasksArchive_ReturnsItButNotToChange()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        var task = await CreateTaskAs(owner, project);
        await ArchiveInDatabase(task.Id);
        HoldsInToken(Permissions.TasksArchive);

        var result = await ByKey("KAN-1", includeArchived: true);

        result.Value.ArchivedAt.Should().Be(Now);
        result.Value.CanChange.Should().BeFalse();
    }

    [Fact]
    public async Task ByKey_WithIncludeArchivedButWithoutTasksArchive_ReturnsPermissionRequired()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        await CreateTaskAs(owner, project);
        HoldsInToken(Permissions.TasksCreate);

        (await ByKey("KAN-1", includeArchived: true)).Error.Should().Be(AuthErrors.PermissionRequired);
    }

    [Fact]
    public async Task ById_OfATask_ReturnsItWithItsAssigneeAndColumn()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(member));
        var task = await CreateTaskAs(owner, project, assigneeIds: [member.Id]);

        var result = await ById(task.Id);

        result.Value.Should().BeEquivalentTo(new { Key = "KAN-1", Assignee = new TaskPersonDto(member.Id, member.DisplayName), ColumnName = "De făcut" });
    }

    [Fact]
    public async Task ById_WhenItDoesNotExist_ReturnsNotFound()
    {
        var owner = await CreateUser();
        SignIn(owner);

        (await ById(Guid.CreateVersion7())).Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact]
    public async Task ById_ForSomeoneOutsideTheProject_ReturnsMemberRequired()
    {
        var owner = await CreateUser();
        var outsider = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var task = await CreateTaskAs(owner, project);
        SignIn(outsider);

        (await ById(task.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task ById_OfAnArchivedTask_ReturnsNotFoundUnlessAskedForWithTasksArchive()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var task = await CreateTaskAs(owner, project);
        await ArchiveInDatabase(task.Id);

        (await ById(task.Id)).Error.Should().Be(TaskErrors.NotFound);
        HoldsInToken(Permissions.TasksArchive);
        (await ById(task.Id, includeArchived: true)).Value.Id.Should().Be(task.Id);
    }
}
