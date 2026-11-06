using Kanabol.Application.Features.Projects;

namespace Kanabol.Application.Features.Tasks.Queries.GetBoard;

/// <summary>
/// A project's board (UI spec section 7.4): its columns from the left and all its open tasks, by column
/// and place. CanMove: a real member of an open project, who moves the cards. CanManage: an owner, or
/// projects.manage, of an open project, who changes the columns. The API checks both again anyway.
/// </summary>
public sealed record BoardDto(
    Guid ProjectId,
    string ProjectKey,
    bool IsArchived,
    bool CanMove,
    bool CanManage,
    BoardColumnDto[] Columns,
    BoardTaskDto[] Tasks);
