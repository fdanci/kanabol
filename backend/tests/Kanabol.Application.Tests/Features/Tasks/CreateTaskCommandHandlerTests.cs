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
/// membership (SPEC section 4.4). The number comes from the project's counter, whose version guards it,
/// and a creation that loses that race takes the next number instead of answering 409 (SPEC 5.1, 5.3).
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
    public async Task Create_ForAnActiveMember_AssignsThem()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(owner);

        var result = await Create(NewTask(project, assigneeId: member.Id));

        result.Value.Assignee.Should().Be(new TaskPersonDto(member.Id, member.DisplayName));
    }

    [Fact]
    public async Task Create_ForSomeoneOutsideTheProject_ReturnsAValidationErrorOnAssigneeId()
    {
        var owner = await CreateUser();
        var outsider = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        var result = await Create(NewTask(project, assigneeId: outsider.Id));

        result.Error.Should().Be(TaskErrors.AssigneeNotAMember);
        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("AssigneeId");
        (await TasksOf(project)).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ForADeactivatedMember_ReturnsAValidationErrorOnAssigneeId()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        await Deactivate(member);
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(owner);

        (await Create(NewTask(project, assigneeId: member.Id))).Error.Should().Be(TaskErrors.AssigneeNotAMember);
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

        (await Create(new CreateTaskCommand(Guid.CreateVersion7(), "Login", null, TaskPriority.Low, null, null, null))).Error.Should().Be(ProjectErrors.NotFound);
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

        var first = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project, "Primul")),
            async () => second = await HandleInOwnScope<CreateTaskCommand, TaskItemDto>(NewTask(project, "Al doilea")));

        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.ToString() : "");
        second!.IsSuccess.Should().BeTrue();
        new[] { first.Value.Key, second.Value.Key }.Should().BeEquivalentTo("RAC-1", "RAC-2");
        first.Value.Key.Should().Be("RAC-2", "the one that waited lost the race and took the next number");
        (await TasksOf(project)).Select(task => task.Number).Should().Equal(1, 2);
        (await NextTaskNumberOf(project)).Should().Be(3);
        Logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Information && entry.Message.StartsWith($"Project {project.Id} changed while", StringComparison.Ordinal));
    }

    // The side effect SPEC section 5.3 writes down: a change to the members bumps the project's version too.
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

        created.Error.Should().Be(ProjectErrors.Archived);
        (await TasksOf(project)).Should().BeEmpty();
    }

    // At most 3 retries (SPEC section 5.1): a project that changes before every save gives up with the 409.
    [Fact]
    public async Task Create_WhenTheProjectChangesBeforeEverySave_GivesUpAfterThreeRetries()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);
        Clock.UtcNow.Returns(_ =>
        {
            // Every read of the clock comes just before a save, from the handler or the audit stamp.
            using var db = Postgres.CreateContext();
            db.Projects.IgnoreQueryFilters().Where(stored => stored.Id == project.Id)
                .ExecuteUpdate(set => set.SetProperty(stored => stored.Name, stored => stored.Name));
            return Now;
        });

        var result = await Create(NewTask(project));

        result.Error.Should().Be(ProjectErrors.ChangedMeanwhile);
        (await TasksOf(project)).Should().BeEmpty();
        (await NextTaskNumberOf(project)).Should().Be(1);
        Logs.Entries.Count(entry => entry.Message.StartsWith($"Project {project.Id} changed while", StringComparison.Ordinal)).Should().Be(3);
    }
}
