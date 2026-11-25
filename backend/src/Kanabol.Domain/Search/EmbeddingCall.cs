using Kanabol.Domain.Common;

namespace Kanabol.Domain.Search;

/// <summary>
/// One call to the embedding model and the tokens it used (SPEC section 19.5), for the cost calculator of step 4:
/// the model's name, since each model has its own price, how many texts went in the call, and when. A call that
/// failed has no row, since it gave no count of tokens. Nothing in the row says which texts were sent.
/// </summary>
public sealed class EmbeddingCall : Entity
{
    public EmbeddingCall(EmbeddingPurpose purpose, string model, int textCount, int tokens, DateTimeOffset calledAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfLessThan(textCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);

        Purpose = purpose;
        Model = model;
        TextCount = textCount;
        Tokens = tokens;
        CalledAt = calledAt;
    }

    private EmbeddingCall()
    {
    }

    public EmbeddingPurpose Purpose { get; private set; }

    /// <summary>The model's name, not the deployment's, as next to the vectors.</summary>
    public string Model { get; private set; } = null!;

    public int TextCount { get; private set; }

    /// <summary>The tokens the model counted for the whole call, as it answered them.</summary>
    public int Tokens { get; private set; }

    public DateTimeOffset CalledAt { get; private set; }
}
