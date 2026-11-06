using Kanabol.Api.Authorization;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.CreateTask;
using Kanabol.Application.Features.Tasks.Queries.GetTaskById;
using Kanabol.Application.Features.Tasks.Queries.GetTaskByKey;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// Creating a task needs tasks.create, from the token, and a real membership, from the database. Reading
/// one needs to see its project, as a member, with projects.view_all or with projects.manage, and archived
/// ones come only with includeArchived=true and tasks.archive (SPEC sections 4.4 and 5.1).
/// </summary>
[ApiController]
[Route("api/tasks")]
public sealed class TasksController : ControllerBase
{
    [HttpPost("/api/projects/{projectId:guid}/tasks")]
    [HasPermission(Permissions.TasksCreate)]
    public async Task<IActionResult> CreateTask(
        Guid projectId,
        CreateTaskRequest request,
        [FromServices] ICommandHandler<CreateTaskCommand, TaskItemDto> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(
            new CreateTaskCommand(projectId, request.Title, request.Description, request.Priority, request.ColumnId, request.AssigneeId, request.DueDate),
            ct);
        return result.ToCreatedResult(result.IsSuccess ? $"/api/tasks/{result.Value.Id}" : "");
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTask(
        Guid id,
        [FromServices] IQueryHandler<GetTaskByIdQuery, TaskItemDto> handler,
        CancellationToken ct,
        [FromQuery] bool includeArchived = false) =>
        (await handler.Handle(new GetTaskByIdQuery(id, includeArchived), ct)).ToActionResult();

    [HttpGet("by-key/{key}")]
    public async Task<IActionResult> GetTaskByKey(
        string key,
        [FromServices] IQueryHandler<GetTaskByKeyQuery, TaskItemDto> handler,
        CancellationToken ct,
        [FromQuery] bool includeArchived = false) =>
        (await handler.Handle(new GetTaskByKeyQuery(key, includeArchived), ct)).ToActionResult();
}
