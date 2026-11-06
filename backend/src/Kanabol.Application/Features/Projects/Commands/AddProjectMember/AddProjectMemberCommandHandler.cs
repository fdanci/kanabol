using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Projects.Commands.AddProjectMember;

/// <summary>Only active users join: a deactivated one couldn't use the project, and couldn't be told apart in the picker.</summary>
public sealed class AddProjectMemberCommandHandler(
    IAppDbContext db,
    ProjectChanges changes,
    ILogger<AddProjectMemberCommandHandler> logger) : ICommandHandler<AddProjectMemberCommand, ProjectMemberDto>
{
    public async Task<Result<ProjectMemberDto>> Handle(AddProjectMemberCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpen(command.ProjectId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        if (!await db.Users.AnyAsync(user => user.Id == command.UserId && user.IsActive, ct))
        {
            return ProjectErrors.UserNotFound;
        }

        if (loaded.Value.AddMember(command.UserId, command.Role) == MembershipChange.AlreadyAMember)
        {
            return ProjectErrors.AlreadyAMember;
        }

        var saved = await changes.Save(ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        logger.LogInformation("User {MemberUserId} added to project {ProjectId} as {ProjectRole}", command.UserId, command.ProjectId, command.Role);

        return await db.MembersOf(command.ProjectId, command.UserId).SingleAsync(ct);
    }
}
