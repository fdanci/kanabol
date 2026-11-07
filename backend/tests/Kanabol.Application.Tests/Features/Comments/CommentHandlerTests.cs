using Kanabol.Application.Common;
using Kanabol.Application.Features.Comments;
using Kanabol.Application.Features.Comments.Commands.AddComment;
using Kanabol.Application.Features.Comments.Commands.EditComment;
using Kanabol.Application.Features.Comments.Queries.GetComment;
using Kanabol.Application.Features.Comments.Queries.GetTaskComments;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Comments;

/// <summary>
/// A task's comments (Tasks.md group 15; SPEC section 4.4): read by whoever sees the project, from the
/// oldest; written by a real member of an open project on an open task, in markdown of up to 5,000
/// characters; changed only by their author; never deleted.
/// </summary>
public sealed class CommentHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private Task<Result<CommentDto>> AddComment(Guid taskId, string body) =>
        Handle<AddCommentCommand, CommentDto>(new AddCommentCommand(taskId, body));

    private Task<Result<CommentDto>> EditComment(Guid commentId, string body) =>
        Handle<EditCommentCommand, CommentDto>(new EditCommentCommand(commentId, body));

    private Task<Result<CommentDto[]>> CommentsOf(Guid taskId) =>
        Ask<GetTaskCommentsQuery, CommentDto[]>(new GetTaskCommentsQuery(taskId));

    private Task<Result<CommentDto>> CommentById(Guid commentId) =>
        Ask<GetCommentQuery, CommentDto>(new GetCommentQuery(commentId));

    private async Task<(User Owner, User Member, Project Project, TaskItemDto Task)> ATaskWithAMember()
    {
        var owner = await CreateUserNamed("Ana Ionescu", "ana@example.com");
        var member = await CreateUserNamed("Dan Pop", "dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(member));
        var task = await CreateTaskAs(owner, project, "Login SSO");
        SignIn(member);
        return (owner, member, project, task);
    }

    private async Task<CommentDto> Commented(User author, Guid taskId, string body)
    {
        SignIn(author);
        var result = await AddComment(taskId, body);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        return result.Value;
    }

    private async Task ArchiveTask(User by, Guid taskId)
    {
        SignIn(by);
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(taskId))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Add_ByAMember_KeepsTheCommentWithItsAuthorAndWhen()
    {
        var (_, member, _, task) = await ATaskWithAMember();

        var result = await AddComment(task.Id, "Merge cu **Azure AD**\n");

        result.Value.Should().BeEquivalentTo(new
        {
            TaskId = task.Id,
            Author = new TaskPersonDto(member.Id, "Dan Pop"),
            Body = "Merge cu **Azure AD**",
            CreatedAt = Now,
            UpdatedAt = (DateTimeOffset?)null,
            CanEdit = true,
        });
        await using var db = Postgres.CreateContext();
        (await db.Comments.SingleAsync()).Should().BeEquivalentTo(new { Id = result.Value.Id, TaskId = task.Id, AuthorId = member.Id, CreatedById = (Guid?)member.Id });
    }

    [Fact]
    public async Task Add_IsNoChangeOfTheTask_NeitherInItsHistoryNorInWhenItWasUpdated()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await AddComment(task.Id, "Un comentariu")).IsSuccess.Should().BeTrue();

        var stored = await Stored(task.Id);
        stored!.History.Should().BeEmpty();
        stored.UpdatedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task Add_ByOneWhoSeesTheProjectThroughAPermissionOnly_ReturnsMemberRequired(string permission)
    {
        var (_, _, _, task) = await ATaskWithAMember();
        await SignInWith(permission);

        (await AddComment(task.Id, "De la un admin")).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Add_ByADeactivatedMember_ReturnsMemberRequired()
    {
        var (_, member, _, task) = await ATaskWithAMember();
        await Deactivate(member);

        (await AddComment(task.Id, "Prea tarziu")).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Add_ToAnArchivedTask_Returns409TaskArchived()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        await ArchiveTask(owner, task.Id);
        SignIn(member);

        var result = await AddComment(task.Id, "Pe arhivat");

        result.Error.Should().Be(TaskErrors.Archived);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Task.Archived");
    }

    [Fact]
    public async Task Add_InAnArchivedProject_ReturnsProjectArchived()
    {
        var (owner, member, project, task) = await ATaskWithAMember();
        await Archive(project, owner);
        SignIn(member);

        (await AddComment(task.Id, "In proiect arhivat")).Error.Should().Be(ProjectErrors.Archived);
    }

    [Fact]
    public async Task Add_ToATaskThatDoesntExist_ReturnsTaskNotFound()
    {
        await ATaskWithAMember();

        (await AddComment(Guid.CreateVersion7(), "Nicaieri")).Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact]
    public async Task Add_OfExactly5000Characters_IsKept()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await AddComment(task.Id, new string('a', 5_000))).Value.Body.Should().HaveLength(5_000);
    }

    [Theory]
    [InlineData(5_001)]
    [InlineData(0)]
    public async Task Add_LongerThan5000CharactersOrEmpty_ReturnsAValidationErrorOnBody(int length)
    {
        var (_, _, _, task) = await ATaskWithAMember();

        var result = await AddComment(task.Id, new string('a', length));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Body");
    }

    [Theory]
    [InlineData("   \n\t ")]
    [InlineData("Cu un NUL \0 la mijloc")]
    [InlineData("Cu un caracter ascuns ​")]
    public async Task Add_BlankOrWithAHiddenCharacter_ReturnsAValidationErrorOnBody(string body)
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await AddComment(task.Id, body)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Body");
    }

    [Fact]
    public async Task Add_WithLinesAndTabs_KeepsThem()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await AddComment(task.Id, "- unu\n\t- doi\r\n- trei")).Value.Body.Should().Be("- unu\n\t- doi\r\n- trei");
    }

    [Fact]
    public async Task Edit_ByItsAuthor_ChangesTheBodyAndSaysWhen()
    {
        var (_, member, _, task) = await ATaskWithAMember();
        var comment = await Commented(member, task.Id, "Prima forma");

        var result = await EditComment(comment.Id, "A doua forma");

        result.Value.Should().BeEquivalentTo(new { comment.Id, Body = "A doua forma", comment.CreatedAt, UpdatedAt = (DateTimeOffset?)Now, CanEdit = true });
        (await CommentById(comment.Id)).Value.Body.Should().Be("A doua forma");
    }

    [Fact]
    public async Task Edit_OfSomeoneElsesComment_EvenByTheProjectsOwner_Returns403NotYours()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        var comment = await Commented(member, task.Id, "Al lui Dan");
        SignIn(owner);

        var result = await EditComment(comment.Id, "Schimbat de Ana");

        result.Error.Should().Be(CommentErrors.NotYours);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        (await CommentById(comment.Id)).Value.Body.Should().Be("Al lui Dan");
    }

    [Fact]
    public async Task Edit_OfACommentThatDoesntExist_ReturnsCommentNotFound()
    {
        await ATaskWithAMember();

        (await EditComment(Guid.CreateVersion7(), "Nimic")).Error.Should().Be(CommentErrors.NotFound);
    }

    [Fact]
    public async Task Edit_OnAnArchivedTask_Returns409TaskArchived()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        var comment = await Commented(member, task.Id, "Inainte de arhivare");
        await ArchiveTask(owner, task.Id);
        SignIn(member);

        (await EditComment(comment.Id, "Dupa")).Error.Should().Be(TaskErrors.Archived);
    }

    [Fact]
    public async Task Edit_ByItsAuthorRemovedFromTheProject_ReturnsMemberRequired()
    {
        var (_, member, project, task) = await ATaskWithAMember();
        var comment = await Commented(member, task.Id, "Inainte de plecare");
        await using (var db = Postgres.CreateContext())
        {
            await db.ProjectMembers.Where(found => found.ProjectId == project.Id && found.UserId == member.Id).ExecuteDeleteAsync();
        }

        SignIn(member);

        (await EditComment(comment.Id, "Dupa plecare")).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Edit_WithABlankBody_ReturnsAValidationErrorOnBody()
    {
        var (_, member, _, task) = await ATaskWithAMember();
        var comment = await Commented(member, task.Id, "Prima forma");

        (await EditComment(comment.Id, " ")).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Body");
    }

    [Fact]
    public async Task List_GivesEveryCommentFromTheOldest_OnlyTheCallersOwnEditable()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        var first = await Commented(owner, task.Id, "Primul");
        var second = await Commented(member, task.Id, "Al doilea");
        var third = await Commented(owner, task.Id, "Al treilea");
        SignIn(member);

        var result = await CommentsOf(task.Id);

        result.Value.Select(comment => (comment.Id, comment.Body, comment.Author.DisplayName, comment.CanEdit)).Should().Equal(
            (first.Id, "Primul", "Ana Ionescu", false),
            (second.Id, "Al doilea", "Dan Pop", true),
            (third.Id, "Al treilea", "Ana Ionescu", false));
    }

    [Fact]
    public async Task List_OfATaskWithoutComments_IsEmpty()
    {
        var (_, _, _, task) = await ATaskWithAMember();

        (await CommentsOf(task.Id)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task List_WithProjectsViewAll_ReadsThemAndCanEditNone()
    {
        var (owner, _, _, task) = await ATaskWithAMember();
        await Commented(owner, task.Id, "Al Anei");
        await SignInWith(Permissions.ProjectsViewAll);

        (await CommentsOf(task.Id)).Value.Should().ContainSingle().Which.CanEdit.Should().BeFalse();
    }

    [Fact]
    public async Task List_ForSomeoneOutsideTheProject_ReturnsMemberRequired()
    {
        var (_, _, _, task) = await ATaskWithAMember();
        SignIn(await CreateUser("eva@example.com"));

        (await CommentsOf(task.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task List_OfAnArchivedTask_IsThereOnlyForTasksArchiveAndEditableByNobody()
    {
        var (owner, member, _, task) = await ATaskWithAMember();
        await Commented(member, task.Id, "Inainte de arhivare");
        await ArchiveTask(owner, task.Id);
        SignIn(member);

        (await CommentsOf(task.Id)).Error.Should().Be(TaskErrors.NotFound);
        HoldsInToken(Permissions.TasksArchive);
        (await CommentsOf(task.Id)).Value.Should().ContainSingle().Which.CanEdit.Should().BeFalse();
    }

    [Fact]
    public async Task List_OfATaskThatDoesntExist_ReturnsTaskNotFound()
    {
        await ATaskWithAMember();

        (await CommentsOf(Guid.CreateVersion7())).Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact]
    public async Task GetById_ForSomeoneOutsideTheProject_ReturnsMemberRequired_AndAMissingOneCommentNotFound()
    {
        var (_, member, _, task) = await ATaskWithAMember();
        var comment = await Commented(member, task.Id, "Al lui Dan");
        SignIn(await CreateUser("eva@example.com"));

        (await CommentById(comment.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
        (await CommentById(Guid.CreateVersion7())).Error.Should().Be(CommentErrors.NotFound);
    }
}
