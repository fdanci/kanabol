using System.Diagnostics.CodeAnalysis;
using Kanabol.Api.Authorization;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Tests.Authorization;

/// <summary>Only exists in the tests. A permission on the controller holds for all its actions, on top of their own.</summary>
[ApiController]
[HasPermission(Permissions.ReportsView)]
[Route("api/test-permissions-on-controller")]
[SuppressMessage("Performance", "CA1822", Justification = "MVC only finds instance methods.")]
public sealed class PermissionOnControllerSampleController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();

    [HasPermission(Permissions.ProjectsCreate)]
    [HttpPost]
    public IActionResult Post() => Ok();
}
