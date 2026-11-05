using System.Diagnostics.CodeAnalysis;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Tests.Authorization;

/// <summary>Only exists in the tests, until group 11 brings real endpoints that use IProjectAccess.</summary>
[ApiController]
[Route("api/test-project-access")]
[SuppressMessage("Performance", "CA1822", Justification = "MVC only finds instance methods.")]
public sealed class ProjectAccessSampleController : ControllerBase
{
    [HttpGet("{projectId:guid}/member")]
    public async Task<IActionResult> Member(
        Guid projectId,
        [FromServices] IProjectAccess access,
        [FromServices] ICurrentUser currentUser,
        CancellationToken ct) =>
        (await access.EnsureMemberAsync(projectId, currentUser.Id, ct)).ToActionResult();

    [HttpGet("{projectId:guid}/owner")]
    public async Task<IActionResult> Owner(
        Guid projectId,
        [FromServices] IProjectAccess access,
        [FromServices] ICurrentUser currentUser,
        CancellationToken ct) =>
        (await access.EnsureOwnerAsync(projectId, currentUser.Id, ct)).ToActionResult();
}
