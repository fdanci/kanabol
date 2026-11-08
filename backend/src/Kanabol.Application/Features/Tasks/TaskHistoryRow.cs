namespace Kanabol.Application.Features.Tasks;

/// <summary>A history entry as read, with who made it, before the ids in its values get their names.</summary>
internal sealed record TaskHistoryRow(Guid Id, string Field, string? OldValue, string? NewValue, DateTimeOffset ChangedAt, TaskPersonDto ChangedBy)
{
    /// <summary>The ids in both values; a text that isn't one is left without a name.</summary>
    public IEnumerable<Guid> Ids() =>
        new[] { OldValue, NewValue }
            .SelectMany(value => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(text => Guid.TryParse(text, out var id) ? id : (Guid?)null)
            .OfType<Guid>();
}
