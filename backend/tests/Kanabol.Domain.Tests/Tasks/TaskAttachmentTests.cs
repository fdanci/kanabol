using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Tasks;

/// <summary>
/// A task's files (SPEC sections 9 and 10): added only to an open task and taken off one, each raising
/// AttachmentsChangedEvent for the board, neither in the history.
/// </summary>
public sealed class TaskAttachmentTests
{
    private static readonly Guid _by = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Project _project = new ProjectBuilder().Build();

    private TaskItem NewTask() => new TaskItemBuilder().InProject(_project).Build();

    [Fact]
    public void AddAttachment_ToAnOpenTask_KeepsItAndRaisesAttachmentsChanged()
    {
        var task = NewTask();

        var attachment = task.AddAttachment("brief.pdf", "application/pdf", 1234, "2026/11/key.pdf", _by);

        attachment.Should().BeEquivalentTo(new { TaskId = task.Id, FileName = "brief.pdf", ContentType = "application/pdf", Size = 1234L, StorageKey = "2026/11/key.pdf", UploadedById = _by });
        task.Attachments.Should().ContainSingle().Which.Should().BeSameAs(attachment);
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new AttachmentsChangedEvent(task.Id, _project.Id, _by));
        task.History.Should().BeEmpty();
    }

    [Fact]
    public void AddAttachment_ToAnArchivedTask_Throws()
    {
        var task = NewTask();
        task.Archive(_by, _now);
        task.ClearDomainEvents();

        var add = () => task.AddAttachment("brief.pdf", "application/pdf", 1234, "2026/11/key.pdf", _by);

        add.Should().Throw<InvalidOperationException>();
        task.Attachments.Should().BeEmpty();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RemoveAttachment_OfItsOwn_TakesItOffAndRaisesAttachmentsChanged()
    {
        var task = NewTask();
        var attachment = task.AddAttachment("brief.pdf", "application/pdf", 1234, "2026/11/key.pdf", _by);
        task.ClearDomainEvents();
        var remover = Guid.CreateVersion7();

        task.RemoveAttachment(attachment, remover);

        task.Attachments.Should().BeEmpty();
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new AttachmentsChangedEvent(task.Id, _project.Id, remover));
    }

    [Fact]
    public void RemoveAttachment_OfAnotherTask_ThrowsAndRaisesNothing()
    {
        var task = NewTask();
        var other = NewTask().AddAttachment("brief.pdf", "application/pdf", 1234, "2026/11/key.pdf", _by);

        var remove = () => task.RemoveAttachment(other, _by);

        remove.Should().Throw<ArgumentException>();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RemoveAttachment_FromAnArchivedTask_Throws()
    {
        var task = NewTask();
        var attachment = task.AddAttachment("brief.pdf", "application/pdf", 1234, "2026/11/key.pdf", _by);
        task.Archive(_by, _now);

        var remove = () => task.RemoveAttachment(attachment, _by);

        remove.Should().Throw<InvalidOperationException>();
        task.Attachments.Should().ContainSingle();
    }
}
