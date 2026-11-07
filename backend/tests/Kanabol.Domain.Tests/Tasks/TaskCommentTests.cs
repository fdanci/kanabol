using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Tasks;

/// <summary>
/// A task's comments (SPEC sections 4.3 and 4.4): markdown kept as written but for its blank end, added only
/// to an open task, which raises CommentAddedEvent and writes no history. Editing raises nothing.
/// </summary>
public sealed class TaskCommentTests
{
    private static readonly Guid _author = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Project _project = new ProjectBuilder().Build();

    private TaskItem NewTask() => new TaskItemBuilder().InProject(_project).Build();

    [Fact]
    public void AddComment_ToAnOpenTask_KeepsItAndRaisesCommentAdded()
    {
        var task = NewTask();

        var comment = task.AddComment(_author, "Merge **bine**\n\n  ");

        comment.Should().BeEquivalentTo(new { TaskId = task.Id, AuthorId = _author, Body = "Merge **bine**" });
        task.Comments.Should().ContainSingle().Which.Should().BeSameAs(comment);
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new CommentAddedEvent(comment.Id, task.Id, _project.Id, _author));
        task.History.Should().BeEmpty("a comment isn't a change of the task");
    }

    [Fact]
    public void AddComment_KeepsTheLeadingSpacesOfMarkdown()
    {
        var task = NewTask();

        task.AddComment(_author, "    un bloc de cod").Body.Should().Be("    un bloc de cod");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n\t ")]
    public void AddComment_WhenBlank_ThrowsAndAddsNothing(string body)
    {
        var task = NewTask();

        var add = () => task.AddComment(_author, body);

        add.Should().Throw<ArgumentException>();
        task.Comments.Should().BeEmpty();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AddComment_ToAnArchivedTask_Throws()
    {
        var task = NewTask();
        task.Archive(_author, _now);
        task.ClearDomainEvents();

        var add = () => task.AddComment(_author, "Prea tarziu");

        add.Should().Throw<InvalidOperationException>();
        task.Comments.Should().BeEmpty();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Edit_ChangesTheBodyAndRaisesNothing()
    {
        var task = NewTask();
        var comment = task.AddComment(_author, "Prima forma");
        task.ClearDomainEvents();

        comment.Edit("A doua forma\n");

        comment.Body.Should().Be("A doua forma");
        task.DomainEvents.Should().BeEmpty("editing a comment notifies nobody");
        comment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Edit_WhenBlank_ThrowsAndKeepsTheBody()
    {
        var comment = NewTask().AddComment(_author, "Prima forma");

        var edit = () => comment.Edit(" ");

        edit.Should().Throw<ArgumentException>();
        comment.Body.Should().Be("Prima forma");
    }
}
