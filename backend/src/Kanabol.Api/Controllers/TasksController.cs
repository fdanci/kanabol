using Kanabol.Api.Authorization;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.AssignTask;
using Kanabol.Application.Features.Tasks.Commands.ChangeTaskDescription;
using Kanabol.Application.Features.Tasks.Commands.CreateTask;
using Kanabol.Application.Features.Tasks.Commands.MoveTask;
using Kanabol.Application.Features.Tasks.Commands.RenameTask;
using Kanabol.Application.Features.Tasks.Commands.RestoreTask;
using Kanabol.Application.Features.Tasks.Commands.SetTaskDueDate;
using Kanabol.Application.Features.Tasks.Commands.SetTaskPriority;
using Kanabol.Application.Features.Tasks.Queries.GetBoard;
using Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;
using Kanabol.Application.Features.Tasks.Queries.GetTaskById;
using Kanabol.Application.Features.Tasks.Queries.GetTaskByKey;
using Kanabol.Application.Features.Tasks.Queries.GetTaskHistory;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// Creating a task needs tasks.create, from the token, and changing one a real membership, from the
/// database, as creating one does. Reading needs to see the project: a member, projects.view_all or
/// projects.manage. Archived tasks come only with includeArchived=true and tasks.archive, which restoring
/// needs too. DELETE archives: nothing here deletes a row (SPEC sections 4.4 and 5.1).
/// </summary>
[ApiController]
[Route("api/tasks")]
public sealed class TasksController : ControllerBase
{
    /// <summary>The columns and every open task, without pages: the board filters them in the browser.</summary>
    [HttpGet("/api/projects/{projectId:guid}/board")]
    public async Task<IActionResult> GetBoard(
        Guid projectId,
        [FromServices] IQueryHandler<GetBoardQuery, BoardDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetBoardQuery(projectId), ct)).ToActionResult();

    [HttpGet("/api/projects/{projectId:guid}/tasks")]
    public async Task<IActionResult> GetProjectTasks(
        Guid projectId,
        [FromServices] IQueryHandler<GetProjectTasksQuery, Page<TaskSummaryDto>> handler,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        [FromQuery] bool includeArchived = false,
        [FromQuery] string sort = TaskSort.Default,
        [FromQuery] Guid? assigneeId = null,
        [FromQuery(Name = "priority")] TaskPriority[]? priorities = null,
        [FromQuery] bool overdue = false,
        [FromQuery] string? search = null)
    {
        var query = new GetProjectTasksQuery(projectId)
        {
            Page = page,
            PageSize = pageSize,
            IncludeArchived = includeArchived,
            Sort = sort,
            AssigneeId = assigneeId,
            Priorities = priorities ?? [],
            Overdue = overdue,
            Search = search,
        };
        return (await handler.Handle(query, ct)).ToActionResult();
    }

    [HttpPost("/api/projects/{projectId:guid}/tasks")]
    [HasPermission(Permissions.TasksCreate)]
    public async Task<IActionResult> CreateTask(
        Guid projectId,
        CreateTaskRequest request,
        [FromServices] ICommandHandler<CreateTaskCommand, TaskItemDto> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(
            new CreateTaskCommand(projectId, request.Title, request.Description, request.Priority, request.ColumnId, request.AssigneeIds ?? [], request.DueDate),
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

    /// <summary>
    /// A page of the task's changes, from the newest, each with who made it and the names of the ids in its
    /// values. Archived tasks only for tasks.archive, as their comments (SPEC section 4.4).
    /// </summary>
    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> GetTaskHistory(
        Guid id,
        [FromServices] IQueryHandler<GetTaskHistoryQuery, Page<TaskHistoryEntryDto>> handler,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await handler.Handle(new GetTaskHistoryQuery(id) { Page = page, PageSize = pageSize }, ct)).ToActionResult();

    [HttpPut("{id:guid}/title")]
    public async Task<IActionResult> RenameTask(
        Guid id,
        RenameTaskRequest request,
        [FromServices] ICommandHandler<RenameTaskCommand, TaskItemDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new RenameTaskCommand(id, request.Title), ct)).ToActionResult();

    [HttpPut("{id:guid}/description")]
    public async Task<IActionResult> ChangeTaskDescription(
        Guid id,
        ChangeTaskDescriptionRequest request,
        [FromServices] ICommandHandler<ChangeTaskDescriptionCommand, TaskItemDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ChangeTaskDescriptionCommand(id, request.Description), ct)).ToActionResult();

    [HttpPut("{id:guid}/assignees")]
    public async Task<IActionResult> AssignTask(
        Guid id,
        AssignTaskRequest request,
        [FromServices] ICommandHandler<AssignTaskCommand, TaskItemDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new AssignTaskCommand(id, request.AssigneeIds), ct)).ToActionResult();

    [HttpPut("{id:guid}/priority")]
    public async Task<IActionResult> SetTaskPriority(
        Guid id,
        SetTaskPriorityRequest request,
        [FromServices] ICommandHandler<SetTaskPriorityCommand, TaskItemDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new SetTaskPriorityCommand(id, request.Priority), ct)).ToActionResult();

    [HttpPut("{id:guid}/due-date")]
    public async Task<IActionResult> SetTaskDueDate(
        Guid id,
        SetTaskDueDateRequest request,
        [FromServices] ICommandHandler<SetTaskDueDateCommand, TaskItemDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new SetTaskDueDateCommand(id, request.DueDate), ct)).ToActionResult();

    [HttpPatch("{id:guid}/position")]
    public async Task<IActionResult> MoveTask(
        Guid id,
        MoveTaskRequest request,
        [FromServices] ICommandHandler<MoveTaskCommand, TaskItemDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new MoveTaskCommand(id, request.ColumnId, request.Position), ct)).ToActionResult();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> ArchiveTask(
        Guid id,
        [FromServices] ICommandHandler<ArchiveTaskCommand, Unit> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ArchiveTaskCommand(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/restore")]
    [HasPermission(Permissions.TasksArchive)]
    public async Task<IActionResult> RestoreTask(
        Guid id,
        [FromServices] ICommandHandler<RestoreTaskCommand, TaskItemDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new RestoreTaskCommand(id), ct)).ToActionResult();
}
