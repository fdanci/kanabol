using System.Diagnostics.CodeAnalysis;
using Kanabol.Api.Authorization;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Tests.Authorization;

/// <summary>Only exists in the tests, to reach permission checks that no real endpoint needed before.</summary>
[ApiController]
[Route("api/test-permissions")]
[SuppressMessage("Performance", "CA1822", Justification = "MVC only finds instance methods.")]
public sealed class PermissionSampleController : ControllerBase
{
    public const string PermissionNobodyHas = "no.such.permission";

    [HasPermission(Permissions.UsersManage)]
    [HttpGet("users")]
    public IActionResult Users() => Ok();

    [HasPermission(Permissions.UsersManage)]
    [HasPermission(Permissions.RolesManage)]
    [HttpGet("users-and-roles")]
    public IActionResult UsersAndRoles() => Ok();

    [HasPermission(PermissionNobodyHas)]
    [HttpGet("nobody")]
    public IActionResult Nobody() => Ok();
}
