using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.DeleteBoardColumn;
using Kanabol.Application.Features.Projects.Commands.RemoveProjectMember;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.AssignTask;
using Kanabol.Application.Features.Tasks.Commands.MoveTask;
using Kanabol.Application.Features.Tasks.Commands.RenameTask;
using Kanabol.Application.Features.Tasks.Commands.RestoreTask;
using Kanabol.Application.Features.Tasks.Commands.SetTaskPriority;
using Kanabol.Application.Features.Tasks.Queries.GetTaskHistory;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// A task's history (Tasks.md group 16; SPEC section 4.4, "Istoricul unui task"): a Page from the newest,
/// 50 by default, for whoever sees the project, an archived task only for tasks.archive. Each entry names
/// who made it, deactivated or not, and the ids in its values: the assignees, even those who left the
/// project, and the columns, without a name for one deleted since.
/// </summary>
public sealed class TaskHistoryQueryHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    /// <summary>The clock moves on a minute before each change, so the entries come in the order they were made.</summary>
    private DateTimeOffset _moment = Now;

    private Task<Result<Page<TaskHistoryEntryDto>>> HistoryOf(Guid taskId, int page = 1, int pageSize = Paging.DefaultPageSize) =>
        Ask<GetTaskHistoryQuery, Page<TaskHistoryEntryDto>>(new GetTaskHistoryQuery(taskId) { Page = page, PageSize = pageSize });

    private DateTimeOffset NextMinute()
    {
        _moment = _moment.AddMinutes(1);
        Clock.UtcNow.Returns(_moment);
        return _moment;
    }

    private static async Task<T> Changed<T>(Task<Result<T>> change)
    {
        var result = await change;
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        return result.Value;
    }

    private async Task<(User Owner, User Member, Project Project, TaskItemDto Task)> ATaskOfTwo()
    {
        var owner = await CreateUserNamed("Ana Ionescu", "ana@example.com");
        var member = await CreateUserNamed("Dan Pop", "dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(member));
        var task = await CreateTaskAs(owner, project, "Login SSO");
        return (owner, member, project, task);
    }

    [Fact]
    public async Task Get_ATaskJustCreated_IsAnEmptyPage()
    {
        var (_, _, _, task) = await ATaskOfTwo();

        var result = await HistoryOf(task.Id);

        result.Value.Should().BeEquivalentTo(new Page<TaskHistoryEntryDto>([], 1, Paging.DefaultPageSize, 0));
    }

    [Fact]
    public async Task Get_AfterChanges_GivesEachEntryFromTheNewestWithWhoAndWhen()
    {
        var (owner, member, _, task) = await ATaskOfTwo();
        SignIn(member);
        var renamedAt = NextMinute();
        await Changed(Handle<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(task.Id, "Login cu SSO")));
        SignIn(owner);
        var prioritizedAt = NextMinute();
        await Changed(Handle<SetTaskPriorityCommand, TaskItemDto>(new SetTaskPriorityCommand(task.Id, TaskPriority.High)));

        var result = await HistoryOf(task.Id);

        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Should().BeEquivalentTo(
            [
                new
                {
                    Field = TaskHistoryFields.Priority,
                    OldValue = "Medium",
                    NewValue = "High",
                    ChangedAt = prioritizedAt,
                    ChangedBy = new TaskPersonDto(owner.Id, "Ana Ionescu"),
                    Names = new Dictionary<Guid, string?>(),
                },
                new
                {
                    Field = TaskHistoryFields.Title,
                    OldValue = "Login SSO",
                    NewValue = "Login cu SSO",
                    ChangedAt = renamedAt,
                    ChangedBy = new TaskPersonDto(member.Id, "Dan Pop"),
                    Names = new Dictionary<Guid, string?>(),
                },
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Get_OnlyHoldsTheEntriesOfThatTask()
    {
        var (owner, _, project, task) = await ATaskOfTwo();
        var other = await CreateTaskAs(owner, project, "Alt task");
        NextMinute();
        await Changed(Handle<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(other.Id, "Alt task, redenumit")));

        (await HistoryOf(task.Id)).Value.Items.Should().BeEmpty();
        (await HistoryOf(other.Id)).Value.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Get_ByOneWhoChangedItAndWasDeactivatedSince_StillNamesThem()
    {
        var (_, member, _, task) = await ATaskOfTwo();
        SignIn(member);
        NextMinute();
        await Changed(Handle<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(task.Id, "Login cu SSO")));
        await Deactivate(member);
        await SignInWith(Permissions.ProjectsViewAll);

        var entry = (await HistoryOf(task.Id)).Value.Items.Single();

        entry.ChangedBy.Should().Be(new TaskPersonDto(member.Id, "Dan Pop"));
    }

    [Fact]
    public async Task Get_AChangeOfAssignees_NamesEveryoneOnBothSides_EvenOneWhoLeftTheProject()
    {
        var (owner, member, project, task) = await ATaskOfTwo();
        var eva = await CreateUserNamed("Eva Ene", "eva@example.com");
        await AddMember(project, eva);
        SignIn(owner);
        NextMinute();
        await Changed(Handle<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(task.Id, [member.Id, eva.Id])));
        NextMinute();
        await Changed(Handle<AssignTaskCommand, TaskItemDto>(new AssignTaskCommand(task.Id, [member.Id])));
        await Changed(Handle<RemoveProjectMemberCommand, Unit>(new RemoveProjectMemberCommand(project.Id, eva.Id)));

        var entries = (await HistoryOf(task.Id)).Value.Items;

        entries.Should().HaveCount(2).And.OnlyContain(entry => entry.Field == TaskHistoryFields.Assignees);
        entries[0].Names.Should().BeEquivalentTo(new Dictionary<Guid, string?> { [member.Id] = "Dan Pop", [eva.Id] = "Eva Ene" });
        entries[1].OldValue.Should().BeEmpty();
        entries[1].Names.Should().BeEquivalentTo(new Dictionary<Guid, string?> { [member.Id] = "Dan Pop", [eva.Id] = "Eva Ene" });
    }

    [Fact]
    public async Task Get_AMove_NamesBothColumns_AndNoneForAColumnDeletedSince()
    {
        var (owner, _, project, task) = await ATaskOfTwo();
        var first = ColumnAt(project, 0);
        var second = ColumnAt(project, 1);
        var done = DoneColumn(project);
        SignIn(owner);
        NextMinute();
        await Changed(Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, second.Id, null)));
        NextMinute();
        await Changed(Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, done.Id, null)));
        await Changed(Handle<DeleteBoardColumnCommand, Unit>(new DeleteBoardColumnCommand(project.Id, first.Id)));

        var entries = (await HistoryOf(task.Id)).Value.Items;

        entries.Should().HaveCount(2).And.OnlyContain(entry => entry.Field == TaskHistoryFields.ColumnId);
        entries[0].Should().BeEquivalentTo(new { OldValue = second.Id.ToString(), NewValue = done.Id.ToString() });
        entries[0].Names.Should().BeEquivalentTo(new Dictionary<Guid, string?> { [second.Id] = second.Name, [done.Id] = done.Name });
        entries[1].Should().BeEquivalentTo(new { OldValue = first.Id.ToString(), NewValue = second.Id.ToString() });
        entries[1].Names.Should().BeEquivalentTo(new Dictionary<Guid, string?> { [first.Id] = null, [second.Id] = second.Name });
    }

    [Fact]
    public async Task Get_ABrokenIdInAValue_IsLeftWithoutAName()
    {
        var (owner, _, _, task) = await ATaskOfTwo();
        await AddEntries(new TaskHistoryEntry(task.Id, TaskHistoryFields.Assignees, "", $"nu-e-un-id,{owner.Id}", owner.Id, Now));

        var entry = (await HistoryOf(task.Id)).Value.Items.Single();

        entry.Names.Should().BeEquivalentTo(new Dictionary<Guid, string?> { [owner.Id] = "Ana Ionescu" });
    }

    [Fact]
    public async Task Get_ManyEntries_ComeFiftyAPageFromTheNewest()
    {
        var (owner, _, _, task) = await ATaskOfTwo();
        await AddEntries([.. Enumerable.Range(1, 51).Select(minute =>
            new TaskHistoryEntry(task.Id, TaskHistoryFields.Title, $"Titlul {minute - 1}", $"Titlul {minute}", owner.Id, Now.AddMinutes(minute)))]);

        var first = (await HistoryOf(task.Id)).Value;
        var second = (await HistoryOf(task.Id, page: 2)).Value;

        first.Should().BeEquivalentTo(new { Number = 1, PageSize = 50, TotalCount = 51 });
        first.Items.Should().HaveCount(50);
        first.Items[0].NewValue.Should().Be("Titlul 51");
        first.Items[^1].NewValue.Should().Be("Titlul 2");
        second.Should().BeEquivalentTo(new { Number = 2, PageSize = 50, TotalCount = 51 });
        second.Items.Select(entry => entry.NewValue).Should().Equal("Titlul 1");
    }

    [Fact]
    public async Task Get_EntriesOfTheSameMoment_KeepOneOrderFromPageToPage()
    {
        var (owner, _, _, task) = await ATaskOfTwo();
        await AddEntries([.. Enumerable.Range(1, 5).Select(index =>
            new TaskHistoryEntry(task.Id, TaskHistoryFields.Title, null, $"Titlul {index}", owner.Id, Now))]);

        var all = (await HistoryOf(task.Id)).Value.Items.Select(entry => entry.Id).ToList();
        var paged = new List<Guid>();
        for (var page = 1; page <= 5; page++)
        {
            paged.AddRange((await HistoryOf(task.Id, page, pageSize: 1)).Value.Items.Select(entry => entry.Id));
        }

        // PostgreSQL orders uuids by their bytes, as their text reads.
        paged.Should().Equal(all);
        all.Should().Equal(all.OrderByDescending(id => id.ToString(), StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0, 50, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 101, "PageSize")]
    public async Task Get_APageOutOfRange_IsAValidationError(int page, int pageSize, string field)
    {
        var (_, _, _, task) = await ATaskOfTwo();

        var result = await HistoryOf(task.Id, page, pageSize);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
    }

    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task Get_ByOneWhoSeesTheProjectThroughAPermission_GivesIt(string permission)
    {
        var (_, member, _, task) = await ATaskOfTwo();
        SignIn(member);
        NextMinute();
        await Changed(Handle<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(task.Id, "Login cu SSO")));
        await SignInWith(permission);

        (await HistoryOf(task.Id)).Value.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Get_ByAnOutsider_ReturnsMemberRequired()
    {
        var (_, _, _, task) = await ATaskOfTwo();
        SignIn(await CreateUser("eva@example.com"));

        (await HistoryOf(task.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Get_ByADeactivatedMember_ReturnsMemberRequired()
    {
        var (_, member, _, task) = await ATaskOfTwo();
        await Deactivate(member);
        SignIn(member);

        (await HistoryOf(task.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Get_ATaskThatDoesntExist_ReturnsTaskNotFound()
    {
        await ATaskOfTwo();

        (await HistoryOf(Guid.CreateVersion7())).Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact]
    public async Task Get_AnArchivedTask_WithoutTasksArchive_ReturnsTaskNotFound()
    {
        var (owner, member, _, task) = await ATaskOfTwo();
        SignIn(owner);
        await Changed(Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(task.Id)));
        SignIn(member);

        (await HistoryOf(task.Id)).Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact]
    public async Task Get_AnArchivedTask_ForTasksArchive_GivesItsArchivingAndRestoring()
    {
        var (owner, _, _, task) = await ATaskOfTwo();
        SignIn(owner);
        var archivedAt = NextMinute();
        await Changed(Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(task.Id)));
        HoldsInToken(Permissions.TasksArchive);

        var archived = (await HistoryOf(task.Id)).Value.Items;
        NextMinute();
        await Changed(Handle<RestoreTaskCommand, TaskItemDto>(new RestoreTaskCommand(task.Id)));
        var restored = (await HistoryOf(task.Id)).Value.Items;

        archived.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Field = TaskHistoryFields.ArchivedAt, OldValue = (string?)null });
        restored.Select(entry => (entry.Field, entry.NewValue)).Should().Equal(
            (TaskHistoryFields.ArchivedAt, null),
            (TaskHistoryFields.ArchivedAt, archivedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private async Task AddEntries(params TaskHistoryEntry[] entries)
    {
        await using var db = Postgres.CreateContext();
        db.TaskHistoryEntries.AddRange(entries);
        await db.SaveChangesAsync();
    }

    private async Task AddMember(Project project, User user)
    {
        await using var db = Postgres.CreateContext();
        db.ProjectMembers.Add(new ProjectMember(project.Id, user.Id, ProjectRole.Member));
        await db.SaveChangesAsync();
    }
}
