using Kanabol.Api.Authorization;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Settings;
using Kanabol.Application.Features.Settings.Commands.SetWorkSchedule;
using Kanabol.Application.Features.Settings.Queries.GetWorkSchedule;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// The general settings (SPEC section 4.4, "Setările generale"): anyone signed in reads the work schedule, and
/// settings.manage changes it, which the handler checks again in the database.
/// </summary>
[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    [HttpGet("work-schedule")]
    public async Task<IActionResult> GetWorkSchedule(
        [FromServices] IQueryHandler<GetWorkScheduleQuery, WorkScheduleDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetWorkScheduleQuery(), ct)).ToActionResult();

    [HttpPut("work-schedule")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> SetWorkSchedule(
        SetWorkScheduleRequest request,
        [FromServices] ICommandHandler<SetWorkScheduleCommand, WorkScheduleDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new SetWorkScheduleCommand(request.Days ?? [], request.Start ?? "", request.End ?? ""), ct)).ToActionResult();
}
