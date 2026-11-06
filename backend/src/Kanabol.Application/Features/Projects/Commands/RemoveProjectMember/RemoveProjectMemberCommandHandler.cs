using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Projects.Commands.RemoveProjectMember;

/// <summary>Project.RemoveMember refuses to leave the project without an owner (SPEC section 4.4).</summary>
public sealed class RemoveProjectMemberCommandHandler(
    ProjectChanges changes,
    ILogger<RemoveProjectMemberCommandHandler> logger) : ICommandHandler<RemoveProjectMemberCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RemoveProjectMemberCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpen(command.ProjectId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var change = loaded.Value.RemoveMember(command.UserId);
        if (change != MembershipChange.Done)
        {
            return change == MembershipChange.LastOwner ? ProjectErrors.LastOwner : ProjectErrors.MemberNotFound;
        }

        var saved = await changes.Save(ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        logger.LogInformation("User {MemberUserId} removed from project {ProjectId}", command.UserId, command.ProjectId);
        return Unit.Value;
    }
}
