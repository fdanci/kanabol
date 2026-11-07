using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks;

/// <summary>
/// A file on a task (SPEC section 10). The file is in IFileStorage under StorageKey, a name of its own: the
/// original FileName only comes back on download. It comes through TaskItem.AddAttachment, and goes, row and
/// file, through TaskItem.RemoveAttachment.
/// </summary>
public sealed class Attachment : AuditableEntity
{
    /// <summary>25 MB a file.</summary>
    public const long MaxSize = 25L * 1024 * 1024;

    public const int MaxFileNameLength = 255;

    public Attachment(Guid taskId, string fileName, string contentType, long size, string storageKey, Guid uploadedById)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        TaskId = taskId;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        StorageKey = storageKey;
        UploadedById = uploadedById;
    }

    private Attachment()
    {
    }

    public Guid TaskId { get; private set; }

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    /// <summary>In bytes.</summary>
    public long Size { get; private set; }

    public string StorageKey { get; private set; } = null!;

    public Guid UploadedById { get; private set; }
}
