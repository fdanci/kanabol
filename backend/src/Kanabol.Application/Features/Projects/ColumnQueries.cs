using Kanabol.Application.Abstractions;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects;

public static class ColumnQueries
{
    /// <summary>The columns of a project, from the left.</summary>
    public static IQueryable<BoardColumnDto> ColumnsOf(this IAppDbContext db, Guid projectId) =>
        db.BoardColumns
            .AsNoTracking()
            .Where(column => column.ProjectId == projectId)
            .OrderBy(column => column.Position)
            .Select(column => new BoardColumnDto(column.Id, column.Name, column.Position, column.IsDone));

    public static BoardColumnDto ToDto(this BoardColumn column) => new(column.Id, column.Name, column.Position, column.IsDone);

    public static BoardColumnDto[] ColumnsInOrder(this Project project) =>
        [.. project.Columns.OrderBy(column => column.Position).Select(ToDto)];
}
