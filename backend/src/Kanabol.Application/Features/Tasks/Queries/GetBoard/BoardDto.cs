using Kanabol.Application.Features.Projects;

namespace Kanabol.Application.Features.Tasks.Queries.GetBoard;

/// <summary>
/// A project's board (UI spec section 7.4): its columns from the left and all its open tasks, by column
/// and place. CanMove: a real member of an open project, who moves the cards. CanManage: an owner, or
/// projects.manage, of an open project, for the project's settings and members. CanManageColumns: those, or a
/// real member whose role has columns.manage, of an open project, who change the columns (group 23). The API
/// checks them all again anyway.
/// CanSeeArchived: tasks.archive in the token, for whom the board shows the column "Arhivate" (SPEC section 4.4).
/// </summary>
public sealed record BoardDto(
    Guid ProjectId,
    string ProjectKey,
    bool IsArchived,
    bool CanMove,
    bool CanManage,
    bool CanManageColumns,
    bool CanSeeArchived,
    BoardColumnDto[] Columns,
    BoardTaskDto[] Tasks);
