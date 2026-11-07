using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Attachments;
using Kanabol.Application.Features.Attachments.Commands.AddAttachment;
using Kanabol.Application.Features.Attachments.Commands.DeleteAttachment;
using Kanabol.Application.Features.Attachments.Queries.DownloadAttachment;
using Kanabol.Domain.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// Files on a task (SPEC sections 4.4 and 10): one a request, as multipart in the field file, up to 25 MB
/// with an allowed extension, from a real member on an open task. Whoever sees the task downloads them, always
/// as a download; whoever uploaded one, or tasks.archive, deletes it, file and all.
/// </summary>
[ApiController]
[Route("api")]
public sealed class AttachmentsController : ControllerBase
{
    /// <summary>
    /// The request may hold a little more than the file, for the multipart's own parts. Above that the form
    /// isn't read at all, and the answer is the same 400 on file; nginx refuses above 30 MB (SPEC section 13.2).
    /// </summary>
    private const long MaxRequestSize = Attachment.MaxSize + (1024 * 1024);

    [HttpPost("tasks/{taskId:guid}/attachments")]
    [RequestSizeLimit(MaxRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestSize)]
    [DisableFormValueModelBinding]
    public async Task<IActionResult> AddAttachment(
        Guid taskId,
        [FromServices] ICommandHandler<AddAttachmentCommand, AttachmentDto> handler,
        CancellationToken ct)
    {
        IFormFileCollection files;
        try
        {
            files = Request.HasFormContentType ? (await Request.ReadFormAsync(ct)).Files : new FormFileCollection();
        }
        catch (Exception exception) when (exception is BadHttpRequestException or InvalidDataException)
        {
            return Refused("A file goes up to 25 MB.");
        }

        if (files.Count != 1 || files[0].Name != "file")
        {
            return Refused("Send one file a request, in the field file.");
        }

        var file = files[0];
        await using var content = file.OpenReadStream();
        var result = await handler.Handle(new AddAttachmentCommand(taskId, file.FileName, file.Length, content), ct);
        return result.ToCreatedResult(result.IsSuccess ? $"/api/attachments/{result.Value.Id}" : "");
    }

    /// <summary>Always as a download, never shown in the page, under the name it was uploaded with.</summary>
    [HttpGet("attachments/{id:guid}")]
    public async Task<IActionResult> DownloadAttachment(
        Guid id,
        [FromServices] IQueryHandler<DownloadAttachmentQuery, AttachmentDownload> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new DownloadAttachmentQuery(id), ct);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpDelete("attachments/{id:guid}")]
    public async Task<IActionResult> DeleteAttachment(
        Guid id,
        [FromServices] ICommandHandler<DeleteAttachmentCommand, Unit> handler,
        CancellationToken ct) =>
        (await handler.Handle(new DeleteAttachmentCommand(id), ct)).ToActionResult();

    private static IActionResult Refused(string reason) => Result.Failure(AttachmentErrors.FileRefused(reason)).ToActionResult();
}
