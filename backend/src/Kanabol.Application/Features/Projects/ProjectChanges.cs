using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects;

/// <summary>
/// What every change to a project goes through: it has to exist, archived or not, the person asking
/// has to pass as its owner, and an archived project can only be restored. The save turns a change
/// made meanwhile into a conflict: changing the members or the columns also changes the project's
/// version, so two requests can't each take away one of the last two owners, nor put two columns in one place.
/// </summary>
public sealed class ProjectChanges(IAppDbContext db, IProjectAccess projectAccess, ICurrentUser currentUser)
{
    public Task<Result<Project>> LoadOpen(Guid projectId, CancellationToken ct) => Load(projectId, openOnly: true, ct);

    public Task<Result<Project>> LoadOpenOrArchived(Guid projectId, CancellationToken ct) => Load(projectId, openOnly: false, ct);

    public async Task<Result> Save(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProjectErrors.ChangedMeanwhile;
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return ProjectErrors.AlreadyAMember;
        }
    }

    private async Task<Result<Project>> Load(Guid projectId, bool openOnly, CancellationToken ct)
    {
        var project = await db.Projects
            .IgnoreQueryFilters()
            .Include(found => found.Members)
            .FirstOrDefaultAsync(found => found.Id == projectId, ct);
        if (project is null)
        {
            return ProjectErrors.NotFound;
        }

        // Tracked, so they land in project.Columns.
        await db.BoardColumns.Where(column => column.ProjectId == projectId).LoadAsync(ct);

        var access = await projectAccess.EnsureOwnerAsync(projectId, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        if (openOnly && project.ArchivedAt is not null)
        {
            return ProjectErrors.Archived;
        }

        return project;
    }
}
