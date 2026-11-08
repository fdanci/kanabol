using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Dashboard.Queries.GetMySummary;

/// <summary>
/// For anyone signed in, about their own only, without a permission: their own tasks are those they are
/// assigned to, not archived, in projects not archived where they are still a member (TaskQueries.MyTasks),
/// and projects.view_all widens nothing. A deactivated account counts nothing. One query, one round trip.
/// </summary>
public sealed class GetMySummaryQueryHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<GetMySummaryQuery, MySummaryDto>
{
    public async Task<Result<MySummaryDto>> Handle(GetMySummaryQuery query, CancellationToken ct)
    {
        var userId = currentUser.Id;
        var now = clock.UtcNow;
        // Npgsql writes only UTC to a timestamptz, and the browser sends its own offset.
        var weekStart = query.WeekStart!.Value.ToUniversalTime();
        var mine = db.MyTasks(userId, includeArchived: false);

        // The tasks' filter is written out in MyTasks, which turns every filter of the query off: the
        // projects' is written out here too.
        var summary = await db.Users
            .Where(user => user.Id == userId && user.IsActive)
            .Select(user => new MySummaryDto(
                mine.Count(task => task.CompletedAt == null),
                mine.Count(task => task.CompletedAt == null && task.DueDate < now),
                mine.Count(task => task.CompletedAt >= weekStart),
                db.Projects.IgnoreQueryFilters().Count(project => project.ArchivedAt == null
                    && project.Members.Any(member => member.UserId == user.Id))))
            .FirstOrDefaultAsync(ct);

        return summary ?? new MySummaryDto(0, 0, 0, 0);
    }
}
