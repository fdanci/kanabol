using Kanabol.Api.Authorization;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.ActivateUser;
using Kanabol.Application.Features.Users.Commands.ChangeUserRole;
using Kanabol.Application.Features.Users.Commands.CreateUser;
using Kanabol.Application.Features.Users.Commands.DeactivateUser;
using Kanabol.Application.Features.Users.Commands.ResetUserPassword;
using Kanabol.Application.Features.Users.Commands.UpdateUser;
using Kanabol.Application.Features.Users.Queries.GetAssignableRoles;
using Kanabol.Application.Features.Users.Queries.GetUserById;
using Kanabol.Application.Features.Users.Queries.GetUsers;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

[ApiController]
[HasPermission(Permissions.UsersManage)]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromServices] IQueryHandler<GetUsersQuery, Page<UserDto>> handler,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await handler.Handle(new GetUsersQuery(page, pageSize), ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetUser(
        Guid id,
        [FromServices] IQueryHandler<GetUserByIdQuery, UserDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetUserByIdQuery(id), ct)).ToActionResult();

    [HttpGet("assignable-roles")]
    public async Task<IActionResult> GetAssignableRoles(
        [FromServices] IQueryHandler<GetAssignableRolesQuery, AssignableRoleDto[]> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetAssignableRolesQuery(), ct)).ToActionResult();

    [HttpPost]
    public async Task<IActionResult> CreateUser(
        CreateUserRequest request,
        [FromServices] ICommandHandler<CreateUserCommand, UserDto> handler,
        CancellationToken ct)
    {
        var command = new CreateUserCommand(request.Email, request.DisplayName, request.RoleId, request.InitialPassword);
        var result = await handler.Handle(command, ct);
        return result.ToCreatedResult(result.IsSuccess ? $"/api/users/{result.Value.Id}" : "");
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        UpdateUserRequest request,
        [FromServices] ICommandHandler<UpdateUserCommand, UserDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new UpdateUserCommand(id, request.Email, request.DisplayName), ct)).ToActionResult();

    [HttpPut("{id:guid}/role")]
    public async Task<IActionResult> ChangeUserRole(
        Guid id,
        ChangeUserRoleRequest request,
        [FromServices] ICommandHandler<ChangeUserRoleCommand, UserDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ChangeUserRoleCommand(id, request.RoleId), ct)).ToActionResult();

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateUser(
        Guid id,
        [FromServices] ICommandHandler<DeactivateUserCommand, UserDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new DeactivateUserCommand(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> ActivateUser(
        Guid id,
        [FromServices] ICommandHandler<ActivateUserCommand, UserDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ActivateUserCommand(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetUserPassword(
        Guid id,
        ResetUserPasswordRequest request,
        [FromServices] ICommandHandler<ResetUserPasswordCommand, UserDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ResetUserPasswordCommand(id, request.InitialPassword), ct)).ToActionResult();
}
