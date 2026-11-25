using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Search;

public static class EmbeddingErrors
{
    /// <summary>With Embeddings:Enabled false there's no model to call (SPEC section 19.4).</summary>
    public static readonly Error TurnedOff = Error.Failure("Embeddings.TurnedOff", "The vectorization is off: Embeddings:Enabled is false.");

    /// <summary>The batches saved before the failed call stay, and the next run goes on from them (SPEC section 19.5).</summary>
    public static readonly Error ModelFailed = Error.Failure(
        "Embeddings.ModelFailed",
        "The embedding model didn't answer. The batches saved before it stay, and the next run goes on from there.");
}
