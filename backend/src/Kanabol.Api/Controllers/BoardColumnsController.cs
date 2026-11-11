using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.AddBoardColumn;
using Kanabol.Application.Features.Projects.Commands.DeleteBoardColumn;
using Kanabol.Application.Features.Projects.Commands.MoveBoardColumn;
using Kanabol.Application.Features.Projects.Commands.UpdateBoardColumn;
using Kanabol.Application.Features.Projects.Queries.GetBoardColumnById;
using Kanabol.Application.Features.Projects.Queries.GetBoardColumns;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// The columns of a project's board (UI spec section 7.4). Whoever sees the project reads them; its owners,
/// or projects.manage, change them, which the handlers check through IProjectAccess (SPEC section 4.4).
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/board-columns")]
public sealed class BoardColumnsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetColumns(
        Guid projectId,
        [FromServices] IQueryHandler<GetBoardColumnsQuery, BoardColumnDto[]> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetBoardColumnsQuery(projectId), ct)).ToActionResult();

    [HttpGet("{columnId:guid}")]
    public async Task<IActionResult> GetColumn(
        Guid projectId,
        Guid columnId,
        [FromServices] IQueryHandler<GetBoardColumnByIdQuery, BoardColumnDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetBoardColumnByIdQuery(projectId, columnId), ct)).ToActionResult();

    [HttpPost]
    public async Task<IActionResult> AddColumn(
        Guid projectId,
        AddBoardColumnRequest request,
        [FromServices] ICommandHandler<AddBoardColumnCommand, BoardColumnDto> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new AddBoardColumnCommand(projectId, request.Name), ct);
        return result.ToCreatedResult(result.IsSuccess ? $"/api/projects/{projectId}/board-columns/{result.Value.Id}" : "");
    }

    [HttpPut("{columnId:guid}")]
    public async Task<IActionResult> UpdateColumn(
        Guid projectId,
        Guid columnId,
        UpdateBoardColumnRequest request,
        [FromServices] ICommandHandler<UpdateBoardColumnCommand, BoardColumnDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new UpdateBoardColumnCommand(projectId, columnId, request.Name), ct)).ToActionResult();

    /// <summary>Answers every column in its new order.</summary>
    [HttpPatch("{columnId:guid}/position")]
    public async Task<IActionResult> MoveColumn(
        Guid projectId,
        Guid columnId,
        MoveBoardColumnRequest request,
        [FromServices] ICommandHandler<MoveBoardColumnCommand, BoardColumnDto[]> handler,
        CancellationToken ct) =>
        (await handler.Handle(new MoveBoardColumnCommand(projectId, columnId, request.Position), ct)).ToActionResult();

    /// <summary>Only an empty column of the project's own, archived tasks counting.</summary>
    [HttpDelete("{columnId:guid}")]
    public async Task<IActionResult> DeleteColumn(
        Guid projectId,
        Guid columnId,
        [FromServices] ICommandHandler<DeleteBoardColumnCommand, Unit> handler,
        CancellationToken ct) =>
        (await handler.Handle(new DeleteBoardColumnCommand(projectId, columnId), ct)).ToActionResult();
}
