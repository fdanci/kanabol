using Kanabol.Api.Authorization;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Roles;
using Kanabol.Application.Features.Roles.Commands.CreateRole;
using Kanabol.Application.Features.Roles.Commands.DeleteRole;
using Kanabol.Application.Features.Roles.Commands.UpdateRole;
using Kanabol.Application.Features.Roles.Queries.GetRoleById;
using Kanabol.Application.Features.Roles.Queries.GetRoles;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

[ApiController]
[HasPermission(Permissions.RolesManage)]
[Route("api/roles")]
public sealed class RolesController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRoles(
        [FromServices] IQueryHandler<GetRolesQuery, Page<RoleDto>> handler,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await handler.Handle(new GetRolesQuery(page, pageSize), ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRole(
        Guid id,
        [FromServices] IQueryHandler<GetRoleByIdQuery, RoleDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetRoleByIdQuery(id), ct)).ToActionResult();

    [HttpPost]
    public async Task<IActionResult> CreateRole(
        CreateRoleRequest request,
        [FromServices] ICommandHandler<CreateRoleCommand, RoleDto> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new CreateRoleCommand(request.Name, request.Permissions), ct);
        return result.ToCreatedResult(result.IsSuccess ? $"/api/roles/{result.Value.Id}" : "");
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateRole(
        Guid id,
        UpdateRoleRequest request,
        [FromServices] ICommandHandler<UpdateRoleCommand, RoleDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new UpdateRoleCommand(id, request.Name, request.Permissions), ct)).ToActionResult();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRole(
        Guid id,
        [FromServices] ICommandHandler<DeleteRoleCommand, Unit> handler,
        CancellationToken ct) =>
        (await handler.Handle(new DeleteRoleCommand(id), ct)).ToActionResult();
}
