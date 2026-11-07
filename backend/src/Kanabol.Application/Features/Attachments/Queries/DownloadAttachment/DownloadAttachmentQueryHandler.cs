using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Attachments.Queries.DownloadAttachment;

/// <summary>
/// The file, for whoever sees its task (SPEC section 10): a member, projects.view_all or projects.manage, and
/// an archived task's only for tasks.archive. A file of a task the caller doesn't see isn't there for them.
/// </summary>
public sealed class DownloadAttachmentQueryHandler(
    IAppDbContext db,
    TaskVisibility visibility,
    IFileStorage storage) : IQueryHandler<DownloadAttachmentQuery, AttachmentDownload>
{
    public async Task<Result<AttachmentDownload>> Handle(DownloadAttachmentQuery query, CancellationToken ct)
    {
        var attachment = await db.Attachments
            .AsNoTracking()
            .FirstOrDefaultAsync(found => found.Id == query.AttachmentId, ct);
        if (attachment is null)
        {
            return AttachmentErrors.NotFound;
        }

        var seen = await visibility.Check(attachment.TaskId, ct);
        if (seen.IsFailure)
        {
            return seen.Error == TaskErrors.NotFound ? AttachmentErrors.NotFound : seen.Error;
        }

        var content = await storage.OpenReadAsync(attachment.StorageKey, ct);
        return new AttachmentDownload(attachment.FileName, attachment.ContentType, content);
    }
}
