using Kanabol.Api.Authorization;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Search.Commands.SearchTasks;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// The trial search by meaning (SPEC section 19.6), for chat.use, which reads over every project on its own. The
/// question goes in the body, not in the address: nginx's access log keeps the addresses, and the logs never hold a
/// text sent to the model (section 19.5). A POST, too, since each search records the call it made to the model.
/// </summary>
[ApiController]
[HasPermission(Permissions.ChatUse)]
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    /// <summary>The tasks nearest in meaning to the question, nearest first, with their distance and the question's tokens.</summary>
    [HttpPost("tasks")]
    public async Task<IActionResult> SearchTasks(
        SearchTasksRequest request,
        [FromServices] ICommandHandler<SearchTasksCommand, FoundTasksDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(
            new SearchTasksCommand(
                request.Question,
                request.ProjectId,
                request.AssigneeId,
                request.From,
                request.To,
                request.IncludeArchived,
                request.Limit),
            ct)).ToActionResult();
}
