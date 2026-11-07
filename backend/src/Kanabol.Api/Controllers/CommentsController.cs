using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Comments;
using Kanabol.Application.Features.Comments.Commands.AddComment;
using Kanabol.Application.Features.Comments.Commands.EditComment;
using Kanabol.Application.Features.Comments.Queries.GetComment;
using Kanabol.Application.Features.Comments.Queries.GetTaskComments;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// A task's comments (SPEC section 4.4): read by whoever sees its project, written by a real member of an
/// open project on an open task, and changed only by their author. Nothing deletes one in v1.
/// </summary>
[ApiController]
[Route("api")]
public sealed class CommentsController : ControllerBase
{
    /// <summary>Every comment, from the oldest, without pages.</summary>
    [HttpGet("tasks/{taskId:guid}/comments")]
    public async Task<IActionResult> GetTaskComments(
        Guid taskId,
        [FromServices] IQueryHandler<GetTaskCommentsQuery, CommentDto[]> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetTaskCommentsQuery(taskId), ct)).ToActionResult();

    [HttpPost("tasks/{taskId:guid}/comments")]
    public async Task<IActionResult> AddComment(
        Guid taskId,
        AddCommentRequest request,
        [FromServices] ICommandHandler<AddCommentCommand, CommentDto> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new AddCommentCommand(taskId, request.Body), ct);
        return result.ToCreatedResult(result.IsSuccess ? $"/api/comments/{result.Value.Id}" : "");
    }

    [HttpGet("comments/{id:guid}")]
    public async Task<IActionResult> GetComment(
        Guid id,
        [FromServices] IQueryHandler<GetCommentQuery, CommentDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetCommentQuery(id), ct)).ToActionResult();

    [HttpPut("comments/{id:guid}")]
    public async Task<IActionResult> EditComment(
        Guid id,
        EditCommentRequest request,
        [FromServices] ICommandHandler<EditCommentCommand, CommentDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new EditCommentCommand(id, request.Body), ct)).ToActionResult();
}
