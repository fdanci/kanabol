namespace Kanabol.Domain.Search;

/// <summary>
/// Why the embedding model was called, stored as text, so the cost calculator of step 4 can tell the costs
/// apart. Step 1 has the vectorization of the tasks; the search's question comes with the search (SPEC
/// section 19.6).
/// </summary>
public enum EmbeddingPurpose
{
    Vectorization,
}
