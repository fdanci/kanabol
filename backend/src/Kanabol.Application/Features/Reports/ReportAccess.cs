using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Reports;

/// <summary>
/// Who reads a report, checked in the handler, not on the controller (SPEC section 5.1.1): reports.view,
/// from the token as on every endpoint outside user and role management, sees every project, archived ones
/// too. Anyone else reads only the project given, which they have to be able to manage, as for the project's
/// "Rapoarte" tab: an owner, or projects.manage (IProjectAccess.EnsureOwnerAsync, from the database, which
/// keeps the owners of an archived project and refuses a deactivated account). Without a project that is
/// 400 on projectId, and a plain member or an outsider gets 403 Project.OwnerRequired. A project asked for
/// has to exist, whoever asks. userId can be anyone: the report is on the project anyway.
/// </summary>
public sealed class ReportAccess(IAppDbContext db, ICurrentUser currentUser, IProjectAccess projectAccess, ILogger<ReportAccess> logger)
{
    public async Task<Result> Check(IReportFilter filter, CancellationToken ct)
    {
        if (filter.ProjectId is { } asked
            && !await db.Projects.IgnoreQueryFilters().AnyAsync(project => project.Id == asked, ct))
        {
            return ProjectErrors.NotFound;
        }

        if (currentUser.HasPermission(Permissions.ReportsView))
        {
            return Result.Success();
        }

        if (filter.ProjectId is not { } projectId)
        {
            logger.LogWarning("User {RefusedUserId} asked for a report of every project without {Permission}", currentUser.Id, Permissions.ReportsView);
            return ReportErrors.ProjectRequired;
        }

        var owner = await projectAccess.EnsureOwnerAsync(projectId, currentUser.Id, ct);
        if (owner.IsFailure)
        {
            logger.LogWarning(
                "User {RefusedUserId} asked for a report of project {ProjectId}, which they can't manage, without {Permission}",
                currentUser.Id,
                projectId,
                Permissions.ReportsView);
        }

        return owner;
    }
}
