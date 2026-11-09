using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Projects.Commands.RemoveProjectMember;

/// <summary>
/// Project.RemoveMember refuses to leave the project without an active owner (SPEC section 4.4). Once saved,
/// the person's boards stop hearing the project, unless a permission still shows them every project (SPEC
/// section 9).
/// </summary>
public sealed class RemoveProjectMemberCommandHandler(
    IAppDbContext db,
    ProjectChanges changes,
    IProjectAccess projectAccess,
    IRealtimeAccess realtimeAccess,
    ILogger<RemoveProjectMemberCommandHandler> logger) : ICommandHandler<RemoveProjectMemberCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RemoveProjectMemberCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpen(command.ProjectId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var activeMemberIds = await db.ActiveMemberIdsOf(command.ProjectId, ct);
        var change = loaded.Value.RemoveMember(command.UserId, activeMemberIds);
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

        // The change is saved: the boards lose the project even when the request is gone by now.
        if ((await projectAccess.EnsureMemberAsync(command.ProjectId, command.UserId, CancellationToken.None)).IsFailure)
        {
            await realtimeAccess.RemoveFromProjectAsync(command.UserId, command.ProjectId, CancellationToken.None);
        }

        return Unit.Value;
    }
}
