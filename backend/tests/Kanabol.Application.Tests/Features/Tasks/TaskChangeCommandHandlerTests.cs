using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.AssignTask;
using Kanabol.Application.Features.Tasks.Commands.ChangeTaskDescription;
using Kanabol.Application.Features.Tasks.Commands.RenameTask;
using Kanabol.Application.Features.Tasks.Commands.RestoreTask;
using Kanabol.Application.Features.Tasks.Commands.SetTaskDueDate;
using Kanabol.Application.Features.Tasks.Commands.SetTaskPriority;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// What changes a task: a real member of an open project, as for creating one (SPEC section 4.4). Each
/// change writes its history through the entity, and two changes of one task at once give one 409.
/// </summary>
public sealed class TaskChangeCommandHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private Task<Result<TaskItemDto>> Rename(Guid taskId, string title) =>
        Handle<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(taskId, title));

    private Task<Result<TaskItemDto>> Describe(Guid taskId, string? description) =>
        Handle<ChangeTaskDescriptionCommand, TaskItemDto>(new ChangeTaskDescriptionCommand(taskId, description));

    private Task<Result<TaskItemDto>> Assign(Guid taskId, params Guid[] assigneeIds) =>
        Handle<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(taskId, assigneeIds));

    private Task<Result<TaskItemDto>> Prioritize(Guid taskId, TaskPriority priority) =>
        Handle<SetTaskPriorityCommand, TaskItemDto>(new SetTaskPriorityCommand(taskId, priority));

    private Task<Result<TaskItemDto>> Schedule(Guid taskId, DateTimeOffset? dueDate) =>
        Handle<SetTaskDueDateCommand, TaskItemDto>(new SetTaskDueDateCommand(taskId, dueDate));

    private Task<Result<Unit>> ArchiveTask(Guid taskId) => Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(taskId));

    private Task<Result<TaskItemDto>> Restore(Guid taskId) => Handle<RestoreTaskCommand, TaskItemDto>(new RestoreTaskCommand(taskId));

    /// <summary>Each kind of change, so a refusal is checked on all of them.</summary>
    private IEnumerable<(string Name, Func<Guid, Task<Error?>> Change)> EveryChange(Guid assigneeId) =>
    [
        ("rename", async id => ErrorOf(await Rename(id, "Alt titlu"))),
        ("describe", async id => ErrorOf(await Describe(id, "Altă descriere"))),
        ("assign", async id => ErrorOf(await Assign(id, assigneeId))),
        ("prioritize", async id => ErrorOf(await Prioritize(id, TaskPriority.Critical))),
        ("schedule", async id => ErrorOf(await Schedule(id, Now.AddDays(1)))),
        ("archive", async id => ErrorOf(await ArchiveTask(id))),
    ];

    private static Error? ErrorOf<T>(Result<T> result) => result.IsFailure ? result.Error : null;

    private async Task<(User Owner, User Member, Project Project, TaskItemDto Task)> ATaskWithAMember()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(member));
        var task = await CreateTaskAs(owner, project, "Login SSO");
        SignIn(member);
        return (owner, member, project, task);
    }

    [Fact]
    public async Task Rename_ByAMember_ChangesTheTitleAndWritesWhoAndWhen()
    {
        var (_, member, _, task) = await ATaskWithAMember();

        var result = await Rename(task.Id, "  Login cu SSO ");

        result.Value.Title.Should().Be("Login cu SSO");
        result.Value.UpdatedAt.Should().Be(Now);
        (await Stored(task.Id))!.History.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Field = "Title", OldValue = "Login SSO", NewValue = "Login cu SSO", ChangedById = member.Id, ChangedAt = Now });
    }

    [Fact]
    public async Task ChangeDescription_ByAMember_ChangesItAndWritesTheHistory()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Describe(task.Id, "Cu **Azure AD**")).Value.Description.Should().Be("Cu **Azure AD**");
        (await Describe(task.Id, null)).Value.Description.Should().BeNull();

        (await Stored(task.Id))!.History.Select(entry => (entry.Field, entry.OldValue, entry.NewValue)).Should().Equal(
            ("Description", null, "Cu **Azure AD**"),
            ("Description", "Cu **Azure AD**", null));
    }

    [Fact]
    public async Task SetPriority_ByAMember_ChangesItAndWritesTheHistory()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Prioritize(task.Id, TaskPriority.Critical)).Value.Priority.Should().Be(TaskPriority.Critical);

        (await Stored(task.Id))!.History.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Field = "Priority", OldValue = "Medium", NewValue = "Critical" });
    }

    [Fact]
    public async Task SetDueDate_ByAMember_SetsAndClearsItAndWritesTheHistory()
    {
        var (_, _, _, task) = await ATaskWithAMember();
        var due = new DateTimeOffset(2026, 11, 20, 17, 0, 0, TimeSpan.FromHours(2));

        (await Schedule(task.Id, due)).Value.DueDate.Should().Be(due);
        (await Schedule(task.Id, null)).Value.DueDate.Should().BeNull();

        (await Stored(task.Id))!.History.Select(entry => (entry.Field, entry.OldValue, entry.NewValue)).Should().Equal(
            ("DueDate", null, "2026-11-20T15:00:00.0000000+00:00"),
            ("DueDate", "2026-11-20T15:00:00.0000000+00:00", null));
    }

    [Fact]
    public async Task Assign_ToAnActiveMember_AssignsThemAndWritesTheHistory()
    {
        var (owner, member, _, task) = await ATaskWithAMember();

        (await Assign(task.Id, owner.Id)).IsSuccess.Should().BeTrue();
        (await Stored(task.Id))!.Assignees.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { UserId = owner.Id, AssignedById = (Guid?)member.Id, AssignedAt = Now });
        (await Assign(task.Id)).IsSuccess.Should().BeTrue();

        var stored = await Stored(task.Id);
        stored!.Assignees.Should().BeEmpty();
        stored.History.Select(entry => (entry.Field, entry.OldValue, entry.NewValue)).Should().BeEquivalentTo([
            ("Assignees", "", owner.Id.ToString()),
            ("Assignees", owner.Id.ToString(), ""),
        ]);
    }

    // SPEC section 4.4: any number of active members, the whole list at once, in one history entry.
    [Fact]
    public async Task Assign_ToTwoActiveMembers_AssignsBothInOneHistoryEntry()
    {
        var (owner, member, _, task) = await ATaskWithAMember();

        (await Assign(task.Id, member.Id, owner.Id)).IsSuccess.Should().BeTrue();

        var stored = await Stored(task.Id);
        stored!.Assignees.Select(assignee => assignee.UserId).Should().BeEquivalentTo([owner.Id, member.Id]);
        var sorted = string.Join(',', new[] { owner.Id, member.Id }.Select(id => id.ToString()).Order(StringComparer.Ordinal));
        stored.History.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Field = "Assignees", OldValue = "", NewValue = sorted });
    }

    // Tasks.md group 12: assigning to someone outside the project is an error, even next to a member.
    [Fact]
    public async Task Assign_ToSomeoneOutsideTheProject_ReturnsAValidationErrorOnAssigneeIdsAndChangesNothing()
    {
        var (owner, _, _, task) = await ATaskWithAMember();
        var outsider = await CreateUser("eva@example.com");

        var result = await Assign(task.Id, owner.Id, outsider.Id);

        result.Error.Should().Be(TaskErrors.AssigneeNotAMember);
        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("AssigneeIds");
        var stored = await Stored(task.Id);
        stored!.Assignees.Should().BeEmpty();
        stored.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_ToADeactivatedMember_ReturnsAValidationErrorOnAssigneeIds()
    {
        var (owner, _, _, task) = await ATaskWithAMember();
        await Deactivate(owner);

        (await Assign(task.Id, owner.Id)).Error.Should().Be(TaskErrors.AssigneeNotAMember);
    }

    [Fact]
    public async Task Assign_TheSamePersonTwice_ReturnsAValidationErrorOnAssigneeIds()
    {
        var (owner, _, _, task) = await ATaskWithAMember();

        var result = await Assign(task.Id, owner.Id, owner.Id);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("AssigneeIds");
        (await Stored(task.Id))!.Assignees.Should().BeEmpty();
    }

    // SPEC section 4.4: a deactivated assignee stays on the task until someone takes them off.
    [Fact]
    public async Task Assign_KeepingAnAssigneeDeactivatedSince_KeepsThemAndAddsTheOthers()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        (await Assign(task.Id, owner.Id)).IsSuccess.Should().BeTrue();
        await Deactivate(owner);

        (await Assign(task.Id, owner.Id, member.Id)).IsSuccess.Should().BeTrue();

        (await Stored(task.Id))!.Assignees.Select(assignee => assignee.UserId).Should().BeEquivalentTo([owner.Id, member.Id]);
        (await Assign(task.Id, member.Id)).IsSuccess.Should().BeTrue();
        (await Stored(task.Id))!.Assignees.Select(assignee => assignee.UserId).Should().Equal(member.Id);
    }

    [Fact]
    public async Task Changes_ToTheSameValue_WriteNoHistory()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Rename(task.Id, "Login SSO")).IsSuccess.Should().BeTrue();
        (await Prioritize(task.Id, TaskPriority.Medium)).IsSuccess.Should().BeTrue();
        (await Assign(task.Id)).IsSuccess.Should().BeTrue();
        (await Schedule(task.Id, null)).IsSuccess.Should().BeTrue();
        (await Describe(task.Id, "   ")).IsSuccess.Should().BeTrue();

        (await Stored(task.Id))!.History.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task EveryChange_ByOneWhoSeesTheProjectThroughAPermissionOnly_ReturnsMemberRequired(string permission)
    {
        var (owner, _, _, task) = await ATaskWithAMember();
        await SignInWith(permission);

        foreach (var (name, change) in EveryChange(owner.Id))
        {
            (await change(task.Id)).Should().Be(ProjectErrors.MemberRequired, name);
        }

        (await Stored(task.Id))!.History.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryChange_ByADeactivatedMember_ReturnsMemberRequired()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        await Deactivate(member);

        foreach (var (name, change) in EveryChange(owner.Id))
        {
            (await change(task.Id)).Should().Be(ProjectErrors.MemberRequired, name);
        }
    }

    [Fact]
    public async Task EveryChange_InAnArchivedProject_ReturnsArchived()
    {
        var (owner, _, project, task) = await ATaskWithAMember();
        await Archive(project, owner);

        foreach (var (name, change) in EveryChange(owner.Id))
        {
            (await change(task.Id)).Should().Be(ProjectErrors.Archived, name);
        }
    }

    [Fact]
    public async Task EveryChange_OfATaskThatDoesNotExist_ReturnsNotFound()
    {
        var (owner, _, _, _) = await ATaskWithAMember();

        foreach (var (name, change) in EveryChange(owner.Id))
        {
            (await change(Guid.CreateVersion7())).Should().Be(TaskErrors.NotFound, name);
        }
    }

    [Fact]
    public async Task EveryChange_OfAnArchivedTask_ReturnsNotFound()
    {
        var (owner, _, _, task) = await ATaskWithAMember();
        (await ArchiveTask(task.Id)).IsSuccess.Should().BeTrue();

        foreach (var (name, change) in EveryChange(owner.Id))
        {
            (await change(task.Id)).Should().Be(TaskErrors.NotFound, name);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Login\u0007SSO")]
    public async Task Rename_ToATitleThatIsBlankOrHasHiddenCharacters_ReturnsAValidationErrorOnTitle(string title)
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Rename(task.Id, title)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Title");
    }

    [Fact]
    public async Task Rename_ToATitleOverTheLimit_ReturnsAValidationErrorOnTitle()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Rename(task.Id, new string('a', 301))).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Title");
    }

    [Fact]
    public async Task ChangeDescription_OverTheLimit_ReturnsAValidationErrorOnDescription()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Describe(task.Id, new string('a', 20_001))).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Description");
    }

    [Fact]
    public async Task SetPriority_ThatIsNotOneOfTheFour_ReturnsAValidationErrorOnPriority()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Prioritize(task.Id, (TaskPriority)4)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Priority");
    }

    // SPEC section 5.3: the task's xmin, so the second of two changes at once changes nothing.
    [Fact]
    public async Task Changes_OfOneTaskAtOnce_GiveTheSecondChangedMeanwhile()
    {
        var (_, member, _, task) = await ATaskWithAMember();

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(task.Id, "Primul"), member),
            async () => (await HandleInOwnScope<SetTaskPriorityCommand, TaskItemDto>(new SetTaskPriorityCommand(task.Id, TaskPriority.High), member)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(TaskErrors.ChangedMeanwhile);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        var stored = await Stored(task.Id);
        stored!.Title.Should().Be("Login SSO");
        stored.Priority.Should().Be(TaskPriority.High);
        stored.History.Should().ContainSingle(entry => entry.Field == "Priority");
    }

    // A change of assignees changes no column of task_items, so the task is saved too, with its xmin.
    [Fact]
    public async Task Assign_OfOneTaskAtOnce_GivesTheSecondChangedMeanwhileAndKeepsTheFirst()
    {
        var (owner, member, _, task) = await ATaskWithAMember();

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(task.Id, [owner.Id]), member),
            async () => (await HandleInOwnScope<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(task.Id, [member.Id]), member)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(TaskErrors.ChangedMeanwhile);
        var stored = await Stored(task.Id);
        stored!.Assignees.Select(assignee => assignee.UserId).Should().Equal(member.Id);
        stored.History.Should().ContainSingle(entry => entry.Field == "Assignees");
    }

    // Both add the same person: the second would insert the same task_assignees row.
    [Fact]
    public async Task Assign_OfTheSamePersonByTwoAtOnce_GivesTheSecondChangedMeanwhile()
    {
        var (owner, member, _, task) = await ATaskWithAMember();

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(task.Id, [owner.Id]), member),
            async () => (await HandleInOwnScope<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(task.Id, [owner.Id]), owner)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(TaskErrors.ChangedMeanwhile);
        (await Stored(task.Id))!.Assignees.Should().ContainSingle().Which.AssignedById.Should().Be(owner.Id);
    }

    [Fact]
    public async Task Archive_ByAMember_ArchivesItWritesTheHistoryAndKeepsTheRow()
    {
        var (_, member, _, task) = await ATaskWithAMember();

        (await ArchiveTask(task.Id)).IsSuccess.Should().BeTrue();

        var stored = await Stored(task.Id);
        stored!.ArchivedAt.Should().Be(Now);
        stored.ArchivedById.Should().Be(member.Id);
        stored.History.Should().ContainSingle(entry => entry.Field == "ArchivedAt" && entry.OldValue == null);
    }

    [Fact]
    public async Task Restore_OfAnArchivedTask_BringsItBackAndWritesTheHistory()
    {
        var (owner, _, _, task) = await ATaskWithAMember();
        (await ArchiveTask(task.Id)).IsSuccess.Should().BeTrue();
        SignIn(owner);

        var result = await Restore(task.Id);

        result.Value.ArchivedAt.Should().BeNull();
        result.Value.CanChange.Should().BeTrue();
        (await Stored(task.Id))!.History.Select(entry => (entry.Field, entry.NewValue)).Should().Equal(("ArchivedAt", "2026-11-05T12:00:00.0000000+00:00"), ("ArchivedAt", null));
    }

    // tasks.archive is the endpoint's; seeing the project is enough here, so an admin restores without joining it.
    [Fact]
    public async Task Restore_ByOneWhoSeesTheProjectThroughProjectsViewAll_BringsItBack()
    {
        var (_, _, _, task) = await ATaskWithAMember();
        (await ArchiveTask(task.Id)).IsSuccess.Should().BeTrue();
        await SignInWith(Permissions.ProjectsViewAll, Permissions.TasksArchive);

        var result = await Restore(task.Id);

        result.Value.ArchivedAt.Should().BeNull();
        result.Value.CanChange.Should().BeFalse();
    }

    [Fact]
    public async Task Restore_BySomeoneOutsideTheProject_ReturnsMemberRequired()
    {
        var (_, _, _, task) = await ATaskWithAMember();
        (await ArchiveTask(task.Id)).IsSuccess.Should().BeTrue();
        await SignInWith(Permissions.TasksArchive);

        (await Restore(task.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
        (await Stored(task.Id))!.ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Restore_InAnArchivedProject_ReturnsArchived()
    {
        var (owner, _, project, task) = await ATaskWithAMember();
        (await ArchiveTask(task.Id)).IsSuccess.Should().BeTrue();
        await Archive(project, owner);
        SignIn(owner);

        (await Restore(task.Id)).Error.Should().Be(ProjectErrors.Archived);
    }

    [Fact]
    public async Task Restore_OfATaskThatIsNotArchived_ReturnsItAndWritesNothing()
    {
        var (owner, _, _, task) = await ATaskWithAMember();
        SignIn(owner);

        (await Restore(task.Id)).Value.Id.Should().Be(task.Id);
        (await Stored(task.Id))!.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_OfATaskThatDoesNotExist_ReturnsNotFound()
    {
        var (owner, _, _, _) = await ATaskWithAMember();
        SignIn(owner);

        (await Restore(Guid.CreateVersion7())).Error.Should().Be(TaskErrors.NotFound);
    }
}
