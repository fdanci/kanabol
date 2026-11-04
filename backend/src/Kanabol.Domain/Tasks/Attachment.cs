using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks;

public sealed class Attachment : AuditableEntity
{
    public Attachment(Guid taskId, string fileName, string contentType, long size, string storageKey, Guid uploadedById)
    {
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

    public long Size { get; private set; }

    public string StorageKey { get; private set; } = null!;

    public Guid UploadedById { get; private set; }
}
