using System.Text;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Attachments;
using Kanabol.Application.Features.Attachments.Commands.AddAttachment;
using Kanabol.Application.Features.Attachments.Commands.DeleteAttachment;
using Kanabol.Application.Features.Attachments.Queries.DownloadAttachment;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Realtime;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Queries.GetTaskById;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Attachments;

/// <summary>
/// Files on a task (Tasks.md group 15; SPEC sections 4.4 and 10), through the real LocalFileStorage in a
/// folder of the test's own: up to 25 MB with an allowed extension, from a real member on an open task; read
/// by whoever sees the task; deleted, row and file, by whoever uploaded it or tasks.archive.
/// </summary>
public sealed class AttachmentHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    /// <summary>The spec's own number, so a change to the code's constant can't move it.</summary>
    private const long TwentyFiveMegabytes = 25L * 1024 * 1024;

    private static readonly byte[] _content = Encoding.UTF8.GetBytes("un fișier de probă");

    private async Task<Result<AttachmentDto>> Attach(Guid taskId, string fileName, byte[]? content = null, long? size = null)
    {
        await using var stream = new MemoryStream(content ?? _content);
        return await Handle<AddAttachmentCommand, AttachmentDto>(new AddAttachmentCommand(taskId, fileName, size ?? stream.Length, stream));
    }

    private Task<Result<AttachmentDownload>> Download(Guid attachmentId) =>
        Ask<DownloadAttachmentQuery, AttachmentDownload>(new DownloadAttachmentQuery(attachmentId));

    private Task<Result<Unit>> Delete(Guid attachmentId) =>
        Handle<DeleteAttachmentCommand, Unit>(new DeleteAttachmentCommand(attachmentId));

    private async Task<(User Owner, User Member, Project Project, TaskItemDto Task)> ATaskWithAMember()
    {
        var owner = await CreateUserNamed("Ana Ionescu", "ana@example.com");
        var member = await CreateUserNamed("Dan Pop", "dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(member));
        var task = await CreateTaskAs(owner, project, "Login SSO");
        SignIn(member);
        return (owner, member, project, task);
    }

    private async Task<AttachmentDto> Attached(User by, Guid taskId, string fileName = "brief.pdf")
    {
        SignIn(by);
        var result = await Attach(taskId, fileName);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        return result.Value;
    }

    private async Task<string> StorageKeyOf(Guid attachmentId)
    {
        await using var db = Postgres.CreateContext();
        return await db.Attachments.Where(found => found.Id == attachmentId).Select(found => found.StorageKey).SingleAsync();
    }

    private string[] StoredFiles() =>
        Directory.Exists(StorageRoot) ? Directory.GetFiles(StorageRoot, "*", SearchOption.AllDirectories) : [];

    private static void ShouldBeRefusedOnFile<T>(Result<T> result) =>
        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().Equal("File");

    private async Task ArchiveTask(User by, Guid taskId)
    {
        SignIn(by);
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(taskId))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Add_ByAMember_StoresTheFileUnderAKeyOfItsOwnAndItsRow()
    {
        var (_, member, _, task) = await ATaskWithAMember();

        var result = await Attach(task.Id, "Brief Final.PDF");

        result.Value.Should().BeEquivalentTo(new
        {
            FileName = "Brief Final.PDF",
            ContentType = "application/pdf",
            Size = (long)_content.Length,
            UploadedBy = new TaskPersonDto(member.Id, "Dan Pop"),
            UploadedAt = Now,
            CanDelete = true,
        });
        var key = await StorageKeyOf(result.Value.Id);
        key.Should().MatchRegex($@"^{Now:yyyy}/{Now:MM}/[0-9a-f]{{32}}\.pdf$");
        (await File.ReadAllBytesAsync(Path.Combine(StorageRoot, key))).Should().Equal(_content);
        StoredFiles().Should().ContainSingle();
    }

    [Fact]
    public async Task Add_IsNoChangeOfTheTask_NeitherInItsHistoryNorInWhenItWasUpdated()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Attach(task.Id, "brief.pdf")).IsSuccess.Should().BeTrue();

        var stored = await Stored(task.Id);
        stored!.History.Should().BeEmpty();
        stored.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Add_OfExactlyTwentyFiveMegabytes_IsTakenAndOneByteMoreIsRefusedOnFile()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Attach(task.Id, "mare.pdf", [1, 2, 3], size: TwentyFiveMegabytes)).IsSuccess.Should().BeTrue();
        ShouldBeRefusedOnFile(await Attach(task.Id, "prea-mare.pdf", [1, 2, 3], size: TwentyFiveMegabytes + 1));
    }

    [Fact]
    public async Task Add_OfAFileLargerThanItsFormSaid_IsRefusedOnFileAndLeavesNothing()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        ShouldBeRefusedOnFile(await Attach(task.Id, "mincinos.pdf", new byte[TwentyFiveMegabytes + 1], size: 10));

        StoredFiles().Should().BeEmpty();
        await using var db = Postgres.CreateContext();
        (await db.Attachments.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("program.exe")]
    [InlineData("fara-extensie")]
    [InlineData("brief.pdf.exe")]
    [InlineData("")]
    [InlineData("dosar/")]
    [InlineData("cu-tab\t.pdf")]
    public async Task Add_WithANameOrAnExtensionThatIsRefused_ReturnsAValidationErrorOnFile(string fileName)
    {
        var (_, _, _, task) = await ATaskWithAMember();

        ShouldBeRefusedOnFile(await Attach(task.Id, fileName));
        StoredFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Add_WithANameOf256Characters_IsRefusedOnFile_AndOf255Taken()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        ShouldBeRefusedOnFile(await Attach(task.Id, new string('a', 252) + ".pdf"));
        (await Attach(task.Id, new string('a', 251) + ".pdf")).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("schita.png")]
    [InlineData("SCHITA.Png")]
    [InlineData("note.TXT")]
    public async Task Add_WithAnExtensionOfTheList_InAnyCase_IsTaken(string fileName)
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Attach(task.Id, fileName)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Add_WithAFolderInTheName_KeepsOnlyTheName()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await Attach(task.Id, "../../etc/brief.pdf")).Value.FileName.Should().Be("brief.pdf");
        (await Attach(task.Id, @"C:\Users\ana\brief.pdf")).Value.FileName.Should().Be("brief.pdf");
    }

    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task Add_ByOneWhoSeesTheProjectThroughAPermissionOnly_ReturnsMemberRequiredAndStoresNothing(string permission)
    {
        var (_, _, _, task) = await ATaskWithAMember();
        await SignInWith(permission);

        (await Attach(task.Id, "brief.pdf")).Error.Should().Be(ProjectErrors.MemberRequired);
        StoredFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Add_ToAnArchivedTask_Returns409TaskArchivedAndStoresNothing()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        await ArchiveTask(owner, task.Id);
        SignIn(member);

        (await Attach(task.Id, "brief.pdf")).Error.Should().Be(TaskErrors.Archived);
        StoredFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Add_InAnArchivedProject_ReturnsProjectArchived()
    {
        var (owner, member, project, task) = await ATaskWithAMember();
        await Archive(project, owner);
        SignIn(member);

        (await Attach(task.Id, "brief.pdf")).Error.Should().Be(ProjectErrors.Archived);
    }

    // The task goes between the file reaching the disk and its row: the save fails, and the file goes too.
    [Fact]
    public async Task Add_WhenItsRowCantBeSaved_TakesTheFileAwayAgain()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        var attaching = () => PausedAtClockCall(
            2,
            () => Attach(task.Id, "brief.pdf"),
            async () =>
            {
                StoredFiles().Should().ContainSingle("the file is on disk before its row is saved");
                await using var db = Postgres.CreateContext();
                await db.TaskItems.IgnoreQueryFilters().Where(found => found.Id == task.Id).ExecuteDeleteAsync();
            });

        await attaching.Should().ThrowAsync<DbUpdateException>();
        StoredFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Add_TellsTheBoardTheAttachmentsChanged()
    {
        var (_, member, project, task) = await ATaskWithAMember();
        RequestContext.ConnectionId.Returns("tab-1");
        BoardNotifier.ClearReceivedCalls();

        await Attach(task.Id, "brief.pdf");

        await BoardNotifier.Received(1).NotifyProjectAsync(
            project.Id, "TaskUpdated", new TaskUpdatedDto(task.Id, project.Id, member.Id, "tab-1", "Attachments"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTask_ListsTheAttachmentsFromTheFirst_DeletableByTheirUploaderOnly()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        var first = await Attached(owner, task.Id, "brief.pdf");
        var second = await Attached(member, task.Id, "schita.png");
        SignIn(member);

        var read = await Ask<GetTaskByIdQuery, TaskItemDto>(new GetTaskByIdQuery(task.Id, IncludeArchived: false));

        read.Value.Attachments.Select(attachment => (attachment.Id, attachment.FileName, attachment.UploadedBy.DisplayName, attachment.CanDelete)).Should().Equal(
            (first.Id, "brief.pdf", "Ana Ionescu", false),
            (second.Id, "schita.png", "Dan Pop", true));
    }

    [Fact]
    public async Task GetTask_ForTasksArchive_MakesEveryAttachmentDeletable_ButNoneOfAnArchivedTask()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        await Attached(member, task.Id);
        SignIn(owner);
        HoldsInToken(Permissions.TasksArchive);

        (await Ask<GetTaskByIdQuery, TaskItemDto>(new GetTaskByIdQuery(task.Id, IncludeArchived: false))).Value.Attachments.Single().CanDelete.Should().BeTrue();
        await ArchiveTask(owner, task.Id);
        (await Ask<GetTaskByIdQuery, TaskItemDto>(new GetTaskByIdQuery(task.Id, IncludeArchived: true))).Value.Attachments.Single().CanDelete.Should().BeFalse();
    }

    [Fact]
    public async Task Download_ByOneWhoSeesTheProject_GivesTheBytesUnderTheirName()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        var attachment = await Attached(member, task.Id, "Brief Final.pdf");
        await SignInWith(Permissions.ProjectsViewAll);

        var result = await Download(attachment.Id);

        result.Value.FileName.Should().Be("Brief Final.pdf");
        result.Value.ContentType.Should().Be("application/pdf");
        await using var content = result.Value.Content;
        using var read = new MemoryStream();
        await content.CopyToAsync(read);
        read.ToArray().Should().Equal(_content);
    }

    [Fact]
    public async Task Download_ForSomeoneOutsideTheProject_ReturnsMemberRequired()
    {
        var (_, member, _, task) = await ATaskWithAMember();
        var attachment = await Attached(member, task.Id);
        SignIn(await CreateUser("eva@example.com"));

        (await Download(attachment.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Download_OfAnArchivedTasksFile_IsThereOnlyForTasksArchive()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        var attachment = await Attached(member, task.Id);
        await ArchiveTask(owner, task.Id);
        SignIn(member);

        (await Download(attachment.Id)).Error.Should().Be(AttachmentErrors.NotFound);
        HoldsInToken(Permissions.TasksArchive);
        await using var content = (await Download(attachment.Id)).Value.Content;
    }

    [Fact]
    public async Task Delete_ByWhoeverUploadedIt_TakesTheRowAndTheFile()
    {
        var (_, member, project, task) = await ATaskWithAMember();
        var attachment = await Attached(member, task.Id);
        RequestContext.ConnectionId.Returns("tab-1");
        BoardNotifier.ClearReceivedCalls();

        (await Delete(attachment.Id)).IsSuccess.Should().BeTrue();

        await using var db = Postgres.CreateContext();
        (await db.Attachments.AnyAsync(found => found.Id == attachment.Id)).Should().BeFalse();
        StoredFiles().Should().BeEmpty();
        await BoardNotifier.Received(1).NotifyProjectAsync(
            project.Id, "TaskUpdated", new TaskUpdatedDto(task.Id, project.Id, member.Id, "tab-1", "Attachments"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ByAnotherMember_EvenTheOwner_ReturnsNotYoursAndKeepsBoth()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        var attachment = await Attached(member, task.Id);
        SignIn(owner);

        var result = await Delete(attachment.Id);

        result.Error.Should().Be(AttachmentErrors.NotYours);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        StoredFiles().Should().ContainSingle();
    }

    [Fact]
    public async Task Delete_WithTasksArchiveSeeingTheProject_TakesIt()
    {
        var (_, member, _, task) = await ATaskWithAMember();
        var attachment = await Attached(member, task.Id);
        await SignInWith(Permissions.ProjectsViewAll);
        HoldsInToken(Permissions.TasksArchive);

        (await Delete(attachment.Id)).IsSuccess.Should().BeTrue();
        StoredFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_ByItsUploaderNoLongerAMember_ReturnsMemberRequired()
    {
        var (_, member, project, task) = await ATaskWithAMember();
        var attachment = await Attached(member, task.Id);
        await using (var db = Postgres.CreateContext())
        {
            await db.ProjectMembers.Where(found => found.ProjectId == project.Id && found.UserId == member.Id).ExecuteDeleteAsync();
        }

        SignIn(member);

        (await Delete(attachment.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Delete_OnAnArchivedTask_EvenWithTasksArchive_Returns409TaskArchived()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        var attachment = await Attached(member, task.Id);
        await ArchiveTask(owner, task.Id);
        HoldsInToken(Permissions.TasksArchive);

        (await Delete(attachment.Id)).Error.Should().Be(TaskErrors.Archived);
        StoredFiles().Should().ContainSingle();
    }

    [Fact]
    public async Task Delete_InAnArchivedProject_ReturnsProjectArchived()
    {
        var (owner, member, project, task) = await ATaskWithAMember();
        var attachment = await Attached(member, task.Id);
        await Archive(project, owner);
        SignIn(member);

        (await Delete(attachment.Id)).Error.Should().Be(ProjectErrors.Archived);
    }

    [Fact]
    public async Task DeleteAndDownload_OfAnAttachmentThatDoesntExist_ReturnAttachmentNotFound()
    {
        await ATaskWithAMember();

        (await Delete(Guid.CreateVersion7())).Error.Should().Be(AttachmentErrors.NotFound);
        (await Download(Guid.CreateVersion7())).Error.Should().Be(AttachmentErrors.NotFound);
    }
}
