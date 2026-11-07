using Kanabol.Application.Features.Tasks;

namespace Kanabol.Application.Features.Attachments;

/// <summary>
/// A file on a task, as its panel lists it (UI spec section 7.5): its name, size in bytes, who and when.
/// CanDelete: whoever uploaded it, still a real member, or tasks.archive, on an open task of an open project.
/// </summary>
public sealed record AttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long Size,
    TaskPersonDto UploadedBy,
    DateTimeOffset UploadedAt,
    bool CanDelete);
