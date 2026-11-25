namespace Kanabol.Domain.Search;

/// <summary>
/// Why the embedding model was called, stored as text, so the cost calculator of step 4 can tell the costs
/// apart: the vectorization of the tasks (SPEC section 19.5), or the question of a search (section 19.6).
/// </summary>
public enum EmbeddingPurpose
{
    Vectorization,
    Search,
}
