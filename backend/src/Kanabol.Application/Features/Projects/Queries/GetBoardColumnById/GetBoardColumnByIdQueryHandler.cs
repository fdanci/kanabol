using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects.Queries.GetBoardColumnById;

/// <summary>Where the Location of an added column points.</summary>
public sealed class GetBoardColumnByIdQueryHandler(
    IAppDbContext db,
    IProjectAccess projectAccess,
    ICurrentUser currentUser) : IQueryHandler<GetBoardColumnByIdQuery, BoardColumnDto>
{
    public async Task<Result<BoardColumnDto>> Handle(GetBoardColumnByIdQuery query, CancellationToken ct)
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

        var found = await db.BoardColumns
            .AsNoTracking()
            .FirstOrDefaultAsync(column => column.Id == query.ColumnId && column.ProjectId == query.ProjectId, ct);
        return found is null ? ColumnErrors.NotFound : found.ToDto();
    }
}
