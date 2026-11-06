using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects.Queries.GetBoardColumns;

/// <summary>For whoever can see the project, archived or not (SPEC section 4.4), from the left.</summary>
public sealed class GetBoardColumnsQueryHandler(
    IAppDbContext db,
    IProjectAccess projectAccess,
    ICurrentUser currentUser) : IQueryHandler<GetBoardColumnsQuery, BoardColumnDto[]>
{
    public async Task<Result<BoardColumnDto[]>> Handle(GetBoardColumnsQuery query, CancellationToken ct)
    {
        if (!await db.ProjectExists(query.ProjectId, ct))
        {
            return ProjectErrors.NotFound;
        }

        var access = await projectAccess.EnsureMemberAsync(query.ProjectId, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        return await db.ColumnsOf(query.ProjectId).ToArrayAsync(ct);
    }
}
