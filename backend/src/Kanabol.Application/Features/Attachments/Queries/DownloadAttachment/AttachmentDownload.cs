namespace Kanabol.Application.Features.Attachments.Queries.DownloadAttachment;

/// <summary>The file and the name it was uploaded under, which the response disposes of.</summary>
public sealed record AttachmentDownload(string FileName, string ContentType, Stream Content);
