namespace Kanabol.Application.Features.Reports;

/// <summary>
/// A finished task as the cycle-time report reads it (SPEC section 5.1.1): when it was created and finished,
/// the column it is in, and its moves between columns from the first, by the moment then the entry's id.
/// A task starts in the column its first move left, or, without a move, in the one it is in. No duration
/// is below zero, whatever the moments say.
/// </summary>
public sealed record FinishedTaskTimeline(
    Guid ProjectId,
    Guid ColumnId,
    DateTimeOffset CreatedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<ColumnMove> Moves)
{
    public TimeSpan LeadTime => Positive(CompletedAt - CreatedAt);

    /// <summary>
    /// From its first exit from the board's first column, when it started there; from its creation when it
    /// was created in another column, or never left the first one before it was finished.
    /// </summary>
    public TimeSpan CycleTime(Guid? firstColumnId)
    {
        var first = Moves.Count > 0 ? Moves[0] : null;
        var start = first is not null && first.At <= CompletedAt && firstColumnId is { } column && IsColumn(first.From, column)
            ? first.At
            : CreatedAt;
        return Positive(CompletedAt - start);
    }

    /// <summary>How long it stayed in each column, its stays in one added up, until it was finished.</summary>
    public Dictionary<Guid, TimeSpan> Stays()
    {
        var stays = new Dictionary<Guid, TimeSpan>();
        var column = Moves.Count > 0 && Guid.TryParse(Moves[0].From, out var started) ? started : ColumnId;
        var since = CreatedAt;
        foreach (var move in Moves)
        {
            if (move.At > CompletedAt)
            {
                break;
            }

            Add(stays, column, move.At - since);
            column = Guid.TryParse(move.To, out var entered) ? entered : Guid.Empty;
            since = move.At;
        }

        Add(stays, column, CompletedAt - since);
        stays.Remove(Guid.Empty);
        return stays;
    }

    private static void Add(Dictionary<Guid, TimeSpan> stays, Guid column, TimeSpan stay) =>
        stays[column] = stays.GetValueOrDefault(column) + Positive(stay);

    private static bool IsColumn(string? id, Guid column) => Guid.TryParse(id, out var parsed) && parsed == column;

    private static TimeSpan Positive(TimeSpan duration) => duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
}
