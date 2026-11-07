using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.AddProjectMember;
using Kanabol.Application.Features.Projects.Commands.ArchiveProject;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.CreateTask;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// Creating a task: tasks.create is the endpoint's, from the token, and the handler asks for a real
/// membership (SPEC section 4.4). The number comes from one UPDATE … RETURNING on the project's counter,
/// in the transaction that saves the task, so creations at once never race (SPEC 5.1, 5.3).
/// </summary>
public sealed class CreateTaskCommandHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    [Fact]
    public async Task Create_ByAMember_SavesItWithTheFirstNumberAndItsKey()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        SignIn(owner);

        var result = await Create(NewTask(project, "  Login SSO ", description: "Cu **Azure AD**", priority: TaskPriority.High, dueDate: Now.AddDays(3)));

        result.Value.Should().BeEquivalentTo(new
        {
            ProjectId = project.Id,
            Key = "KAN-1",
            Number = 1,
            Title = "Login SSO",
            Description = "Cu **Azure AD**",
            Priority = TaskPriority.High,
            DueDate = Now.AddDays(3),
            Assignee = (TaskPersonDto?)null,
            ColumnId = ColumnAt(project, 0).Id,
            ColumnName = "De făcut",
            Position = 0,
            CompletedAt = (DateTimeOffset?)null,
            ArchivedAt = (DateTimeOffset?)null,
            CreatedAt = Now,
            CreatedBy = new TaskPersonDto(owner.Id, owner.DisplayName),
            CanChange = true,
        });
        (await Stored(result.Value.Id)).Should().NotBeNull();
        (await NextTaskNumberOf(project)).Should().Be(2);
    }

    [Fact]
    public async Task Create_ThreeInARow_NumbersThemOneTwoThreeAndPutsEachAtTheEnd()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        foreach (var title in new[] { "Unu", "Doi", "Trei" })
        {
            await CreateTaskAs(owner, project, title);
        }

        (await TasksOf(project)).Select(task => (task.Title, task.Number, task.Position)).Should().Equal(("Unu", 1, 0), ("Doi", 2, 1), ("Trei", 3, 2));
        (await NextTaskNumberOf(project)).Should().Be(4);
    }

    [Fact]
    public async Task Create_InAGivenColumn_PutsItAtTheEndOfThatColumn()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await CreateTaskAs(owner, project);
        var inProgress = ColumnAt(project, 1);

        var result = await Create(NewTask(project, columnId: inProgress.Id));

        result.Value.ColumnId.Should().Be(inProgress.Id);
        result.Value.Position.Should().Be(0);
        result.Value.Number.Should().Be(2);
    }

    [Fact]
    public async Task Create_InTheDoneColumn_IsCompletedAtOnce()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await Create(NewTask(project, columnId: DoneColumn(project).Id))).Value.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Create_InAColumnOfAnotherProject_ReturnsAValidationErrorOnColumnIdAndTakesNoNumber()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var other = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var result = await Create(NewTask(project, columnId: ColumnAt(other, 0).Id));

        result.Error.Should().Be(TaskErrors.ColumnNotInProject);
        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("ColumnId");
        (await NextTaskNumberOf(project)).Should().Be(1);
    }

    [Fact]
    public async Task Create_ForTwoActiveMembers_AssignsThemBothWithoutHistory()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(owner);

        var result = await Create(NewTask(project, assigneeIds: [member.Id, owner.Id]));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        var stored = await Stored(result.Value.Id);
        stored!.Assignees.Should().BeEquivalentTo([
            new { UserId = member.Id, AssignedById = (Guid?)owner.Id, AssignedAt = Now },
            new { UserId = owner.Id, AssignedById = (Guid?)owner.Id, AssignedAt = Now },
        ]);
        stored.History.Should().BeEmpty();
    }

    // SPEC section 4.4: only active members of the project, each once, or a 400 on assigneeIds.
    [Fact]
    public async Task Create_ForSomeoneOutsideTheProject_ReturnsAValidationErrorOnAssigneeIds()
    {
        var owner = await CreateUser();
        var outsider = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var result = await Create(NewTask(project, assigneeIds: [owner.Id, outsider.Id]));

        result.Error.Should().Be(TaskErrors.AssigneeNotAMember);
        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("AssigneeIds");
        (await TasksOf(project)).Should().BeEmpty();
        (await NextTaskNumberOf(project)).Should().Be(1);
    }

    [Fact]
    public async Task Create_ForADeactivatedMember_ReturnsAValidationErrorOnAssigneeIds()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        await Deactivate(member);
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(owner);

        (await Create(NewTask(project, assigneeIds: [member.Id]))).Error.Should().Be(TaskErrors.AssigneeNotAMember);
    }

    [Fact]
    public async Task Create_ForTheSamePersonTwice_ReturnsAValidationErrorOnAssigneeIds()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var result = await Create(NewTask(project, assigneeIds: [owner.Id, owner.Id]));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("AssigneeIds");
        (await TasksOf(project)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task Create_ByOneWhoSeesTheProjectThroughAPermissionOnly_ReturnsMemberRequired(string permission)
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await SignInWith(permission, Permissions.TasksCreate);

        var result = await Create(NewTask(project));

        result.Error.Should().Be(ProjectErrors.MemberRequired);
        (await TasksOf(project)).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ByADeactivatedMemberWithAValidToken_ReturnsMemberRequired()
    {
        var member = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(member));
        await Deactivate(member);
        SignIn(member);

        (await Create(NewTask(project))).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Create_InAnArchivedProject_ReturnsArchivedAndTakesNoNumber()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await Archive(project, owner);
        SignIn(owner);

        (await Create(NewTask(project))).Error.Should().Be(ProjectErrors.Archived);
        (await NextTaskNumberOf(project)).Should().Be(1);
    }

    [Fact]
    public async Task Create_InAProjectThatDoesNotExist_ReturnsNotFound()
    {
        var user = await CreateUser();
        SignIn(user);

        (await Create(new CreateTaskCommand(Guid.CreateVersion7(), "Login", null, TaskPriority.Low, null, [], null))).Error.Should().Be(ProjectErrors.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Login\u0000SSO")]
    [InlineData("Login​SSO")]
    public async Task Create_WithATitleThatIsBlankOrHasHiddenCharacters_ReturnsAValidationErrorOnTitle(string title)
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await Create(NewTask(project, title))).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Title");
    }

    [Fact]
    public async Task Create_WithATitleOverTheLimit_ReturnsAValidationErrorOnTitle()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await Create(NewTask(project, new string('a', 301)))).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Title");
        (await Create(NewTask(project, new string('a', 300)))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_WithADescriptionOverTheLimitOrHiddenCharacters_ReturnsAValidationErrorOnDescription()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await Create(NewTask(project, description: new string('a', 20_001)))).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Description");
        (await Create(NewTask(project, description: "a\u0000b"))).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Description");
        (await Create(NewTask(project, description: "- unu\n\t- doi\r\n" + new string('a', 19_000)))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_WithAPriorityThatIsNotOneOfTheFour_ReturnsAValidationErrorOnPriority()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await Create(NewTask(project, priority: (TaskPriority)9))).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Priority");
    }

    [Fact]
    public async Task Create_Always_LogsTheKey()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("LOG").OwnedBy(owner));

        var task = await CreateTaskAs(owner, project);

        Logs.Entries.Should().Contain((LogLevel.Information, $"Task {task.Id} created as LOG-1 in project {project.Id}"));
    }

    // SPEC sections 5.1 and 5.3: two creations at once get different numbers, and neither sees a 409.
    [Fact]
    public async Task Create_TwoAtOnce_GetDifferentNumbersAndNoConflict()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("RAC").OwnedBy(owner));
        SignIn(owner);
        Result<TaskItemDto>? second = null;

        // The first read of the clock comes after the reads of the project, its column and the access.
        var first = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project, "Primul")),
            async () => second = await HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project, "Al doilea")));

        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.ToString() : "");
        second!.IsSuccess.Should().BeTrue();
        first.Value.Key.Should().Be("RAC-2", "the one that read the project first took the number second");
        second.Value.Key.Should().Be("RAC-1");
        (await TasksOf(project)).Select(task => (task.Number, task.Position)).Should().Equal((1, 0), (2, 1));
        (await NextTaskNumberOf(project)).Should().Be(3);
    }

    // The UPDATE … RETURNING holds the project's row until the commit, so a creation that comes while
    // another one has its number waits for that commit, then takes the next number and the next place.
    [Fact]
    public async Task Create_WhileAnotherOneHasTakenItsNumber_WaitsForItsCommitThenTakesTheNextOne()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("RAD").OwnedBy(owner));
        SignIn(owner);
        Task<Result<TaskItemDto>>? second = null;

        // The second read of the clock is the audit stamp of the save, inside the transaction.
        var first = await PausedAtClockCall(
            2,
            () => HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project, "Primul")),
            async () =>
            {
                second = HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project, "Al doilea"));
                (await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500)))).Should().NotBeSameAs(second, "it waits for the first one's commit");
            });

        first.Value.Key.Should().Be("RAD-1");
        (await second!).Value.Key.Should().Be("RAD-2");
        (await TasksOf(project)).Select(task => (task.Number, task.Position)).Should().Equal((1, 0), (2, 1));
    }

    // SPEC sections 5.1 and 5.3 (decided 2026-11-06): any number of creations at once get different numbers, and none a 409.
    [Fact]
    public async Task Create_TenAtOnce_GetTenNumbersAndTenPlacesWithoutAConflict()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("ZECE").OwnedBy(owner));
        SignIn(owner);

        var results = await Task.WhenAll(Enumerable.Range(1, 10)
            .Select(index => HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project, $"Task {index}"))));

        results.Where(result => result.IsFailure).Select(result => result.Error).Should().BeEmpty();
        var tasks = await TasksOf(project);
        tasks.Select(task => task.Number).Should().Equal(Enumerable.Range(1, 10));
        tasks.Select(task => task.Position).Should().BeEquivalentTo(Enumerable.Range(0, 10), "each one went at the end of the column");
        (await NextTaskNumberOf(project)).Should().Be(11);
    }

    // SPEC section 5.3: the counter doesn't go through the project's xmin, so a change to the members doesn't touch a creation.
    [Fact]
    public async Task Create_WhileAMemberIsAdded_StillSucceeds()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var created = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project)),
            async () => (await HandleInOwnScope<AddProjectMemberCommand, ProjectMemberDto>(new AddProjectMemberCommand(project.Id, user.Id, ProjectRole.Member))).IsSuccess.Should().BeTrue());

        created.Value.Number.Should().Be(1);
        (await MembersOf(project.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Create_WhenTheProjectIsArchivedMeanwhile_ReturnsArchived()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var created = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project)),
            async () => (await HandleInOwnScope<ArchiveProjectCommand, Unit>(new ArchiveProjectCommand(project.Id))).IsSuccess.Should().BeTrue());

        created.Error.Should().Be(ProjectErrors.Archived, "the counter only moves in an open project");
        (await TasksOf(project)).Should().BeEmpty();
        (await NextTaskNumberOf(project)).Should().Be(1);
    }

    // No retry and no 409 left (SPEC section 5.1): a project changed since it was read still takes the task.
    [Fact]
    public async Task Create_WhenTheProjectIsRenamedMeanwhile_StillSucceedsAndKeepsTheNewName()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var created = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project)),
            async () =>
            {
                await using var db = Postgres.CreateContext();
                await db.Projects.Where(stored => stored.Id == project.Id).ExecuteUpdateAsync(set => set.SetProperty(stored => stored.Name, "Alt nume"));
            });

        created.Value.Number.Should().Be(1);
        await using var check = Postgres.CreateContext();
        (await check.Projects.SingleAsync(stored => stored.Id == project.Id)).Name.Should().Be("Alt nume");
    }

    // The side effect SPEC section 5.3 writes down: a change to the members that read the project before a
    // creation took its number started from an old read, so it gets the 409.
    [Fact]
    public async Task AddMember_WhileATaskIsCreated_Returns409ChangedMeanwhile()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        // The first read of the clock in that request is the audit stamp of its save.
        var added = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<AddProjectMemberCommand, ProjectMemberDto>(new AddProjectMemberCommand(project.Id, user.Id, ProjectRole.Member)),
            async () => (await HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project))).IsSuccess.Should().BeTrue());

        added.Error.Should().Be(ProjectErrors.ChangedMeanwhile);
        (await MembersOf(project.Id)).Should().ContainSingle();
        (await TasksOf(project)).Should().ContainSingle();
    }
}
