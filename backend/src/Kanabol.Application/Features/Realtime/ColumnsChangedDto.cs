namespace Kanabol.Application.Features.Realtime;

/// <summary>
/// ColumnsChanged: a column of the project was added, renamed, moved or deleted, by whom and from which tab. A
/// board that hears it reads itself again (SPEC section 9, group 23).
/// </summary>
public sealed record ColumnsChangedDto(Guid ProjectId, Guid ByUserId, string? OriginConnectionId);
