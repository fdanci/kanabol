using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Tasks;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Attachments;

public static class AttachmentQueries
{
    /// <summary>
    /// The task's files, from the first uploaded, each with who uploaded it by name, or only the one asked for.
    /// Access is the caller's to check first. A file can be deleted, on an open task of an open project, by
    /// whoever uploaded it, still a real member, or by tasks.archive, from the token.
    /// </summary>
    public static async Task<AttachmentDto[]> ReadAttachments(this IAppDbContext db, VisibleTask task, ICurrentUser caller, CancellationToken ct, Guid? attachmentId = null)
    {
        var mayDeleteAny = caller.HasPermission(Permissions.TasksArchive);
        var rows = await (
                from attachment in db.Attachments
                join uploader in db.Users on attachment.UploadedById equals uploader.Id
                where attachment.TaskId == task.TaskId && (attachmentId == null || attachment.Id == attachmentId)
                orderby attachment.CreatedAt, attachment.Id
                select new { attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size, attachment.CreatedAt, UploaderId = uploader.Id, uploader.DisplayName })
            .AsNoTracking()
            .ToListAsync(ct);

        return
        [
            .. rows.Select(row => new AttachmentDto(
                row.Id,
                row.FileName,
                row.ContentType,
                row.Size,
                new TaskPersonDto(row.UploaderId, row.DisplayName),
                row.CreatedAt,
                task.IsOpen && (mayDeleteAny || (task.IsRealMember && row.UploaderId == caller.Id)))),
        ];
    }
}
