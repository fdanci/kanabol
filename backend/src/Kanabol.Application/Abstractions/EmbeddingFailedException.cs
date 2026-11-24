namespace Kanabol.Application.Abstractions;

/// <summary>
/// The model didn't make the vectors: it didn't answer, refused the call or answered something else than a vector
/// per text. The message says what happened, never the texts or a key (SPEC section 19.5).
/// </summary>
public sealed class EmbeddingFailedException : Exception
{
    public EmbeddingFailedException()
    {
    }

    public EmbeddingFailedException(string message)
        : base(message)
    {
    }

    public EmbeddingFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
