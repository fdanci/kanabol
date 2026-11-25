using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Search;

public static class SearchErrors
{
    /// <summary>With Embeddings:Enabled false there are no vectors to search and no model for the question (SPEC section 19.6).</summary>
    public static readonly Error TurnedOff = Error.Conflict("Search.TurnedOff", "The search is off: Embeddings:Enabled is false.");

    /// <summary>The question couldn't be vectorized, so nothing was searched (SPEC section 19.6).</summary>
    public static readonly Error ModelFailed = Error.Unavailable(
        "Search.ModelFailed",
        "The embedding model didn't answer, so nothing was searched. Try again in a moment.");
}
