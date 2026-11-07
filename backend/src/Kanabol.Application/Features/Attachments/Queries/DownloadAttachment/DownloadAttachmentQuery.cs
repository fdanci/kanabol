using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Attachments.Queries.DownloadAttachment;

public sealed record DownloadAttachmentQuery(Guid AttachmentId) : IQuery<AttachmentDownload>;
