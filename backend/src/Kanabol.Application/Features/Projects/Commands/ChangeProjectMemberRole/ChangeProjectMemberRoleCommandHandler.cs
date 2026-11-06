using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Projects.Commands.ChangeProjectMemberRole;

/// <summary>Project.ChangeMemberRole refuses to leave the project without an active owner (SPEC section 4.4).</summary>
public sealed class ChangeProjectMemberRoleCommandHandler(
    IAppDbContext db,
    ProjectChanges changes,
    ILogger<ChangeProjectMemberRoleCommandHandler> logger) : ICommandHandler<ChangeProjectMemberRoleCommand, ProjectMemberDto>
{
    public async Task<Result<ProjectMemberDto>> Handle(ChangeProjectMemberRoleCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpen(command.ProjectId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var activeMemberIds = await db.ActiveMemberIdsOf(command.ProjectId, ct);
        var change = loaded.Value.ChangeMemberRole(command.UserId, command.Role, activeMemberIds);
        if (change != MembershipChange.Done)
        {
            return change == MembershipChange.LastOwner ? ProjectErrors.LastOwner : ProjectErrors.MemberNotFound;
        }

        var saved = await changes.Save(ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        logger.LogInformation("User {MemberUserId} is now {ProjectRole} in project {ProjectId}", command.UserId, command.Role, command.ProjectId);

        return await db.MembersOf(command.ProjectId, command.UserId).SingleAsync(ct);
    }
}
