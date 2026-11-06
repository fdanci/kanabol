using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.ClientLogs.Commands.RecordClientLogs;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// The browser's warnings and errors, for Seq to show next to the API's (SPEC section 8.3). Only for a
/// signed-in user, like every controller: before login, the browser keeps its logs to itself.
/// </summary>
[ApiController]
[Route("api/client-logs")]
public sealed class ClientLogsController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> RecordClientLogs(
        RecordClientLogsRequest request,
        [FromServices] ICommandHandler<RecordClientLogsCommand, Unit> handler,
        CancellationToken ct)
    {
        var command = new RecordClientLogsCommand(request.Entries, request.Dropped, Request.Headers.UserAgent.ToString());
        return (await handler.Handle(command, ct)).ToActionResult();
    }
}
