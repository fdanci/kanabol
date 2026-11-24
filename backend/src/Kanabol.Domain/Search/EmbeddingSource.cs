namespace Kanabol.Domain.Search;

/// <summary>
/// Where a vectorized text comes from, stored as text. Step 1 has only the task's title and description;
/// comments come in step 6 (SPEC section 19.2).
/// </summary>
public enum EmbeddingSource
{
    Task,
}
