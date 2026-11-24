namespace Kanabol.Application.Features.Search;

/// <summary>
/// The vectorization for the search by meaning (SPEC section 19.4): off unless Embeddings:Enabled is true, and
/// then checked at start, so a machine without the model's keys still starts. Embeddings__Enabled=true and the
/// rest come from .env in compose; the Azure ones are in Embeddings:Azure. A message names the setting, never
/// its value, so a key can't end up in a log.
/// </summary>
public sealed class EmbeddingsOptions
{
    public const string SectionName = "Embeddings";

    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    /// <summary>A longer interval would leave a new task out of the search for days.</summary>
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromDays(1);

    public const int MaximumBatchSize = 2048;

    public bool Enabled { get; set; }

    /// <summary>How often the worker looks for tasks to vectorize.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The texts sent to the model in one call. Azure takes at most 2048.</summary>
    public int BatchSize { get; set; } = 64;

    /// <summary>
    /// The name of the model the deployment runs, such as text-embedding-3-small, written next to each vector.
    /// No default: a wrong name would mix vectors of two models under one.
    /// </summary>
    public string Model { get; set; } = "";
}
