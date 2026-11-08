using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Projects;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Reports;

/// <summary>
/// Who reads a report, checked in the handler, not on the controller (SPEC section 5.1.1): reports.view,
/// from the token as on every endpoint outside user and role management, sees every project, archived
/// ones too. A project asked for has to exist, whoever asks.
/// </summary>
public sealed class ReportAccess(IAppDbContext db, ICurrentUser currentUser, ILogger<ReportAccess> logger)
{
    public async Task<Result> Check(IReportFilter filter, CancellationToken ct)
    {
        if (filter.ProjectId is { } projectId
            && !await db.Projects.IgnoreQueryFilters().AnyAsync(project => project.Id == projectId, ct))
        {
            return ProjectErrors.NotFound;
        }

        if (currentUser.HasPermission(Permissions.ReportsView))
        {
            return Result.Success();
        }

        logger.LogWarning("User {RefusedUserId} asked for a report without {Permission}", currentUser.Id, Permissions.ReportsView);
        return AuthErrors.PermissionRequired;
    }
}
