using Kanabol.Api.Authorization;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Roles.Queries.GetPermissions;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

[ApiController]
[HasPermission(Permissions.RolesManage)]
[Route("api/permissions")]
public sealed class PermissionsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPermissions(
        [FromServices] IQueryHandler<GetPermissionsQuery, string[]> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetPermissionsQuery(), ct)).ToActionResult();
}
