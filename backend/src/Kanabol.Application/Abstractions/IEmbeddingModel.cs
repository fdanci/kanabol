namespace Kanabol.Application.Abstractions;

/// <summary>
/// A model that turns texts into vectors (SPEC section 19.3). Azure in Infrastructure now, a local model in step 6,
/// a fake one in the tests: the rest of the code only knows this. Two models' vectors can't be compared, so each
/// vector is stored with the model's Name.
/// </summary>
public interface IEmbeddingModel
{
    /// <summary>The model's name, such as text-embedding-3-small, from Embeddings:Model.</summary>
    string Name { get; }

    /// <summary>
    /// One call for the whole batch: the vectors come in the texts' order. Throws EmbeddingFailedException when
    /// the model doesn't answer, so a run can stop and the next one can take it up again.
    /// </summary>
    Task<EmbeddedTexts> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

/// <summary>The vectors, one per text and in their order, and the tokens the call used, for the cost calculator.</summary>
public sealed record EmbeddedTexts(IReadOnlyList<IReadOnlyList<float>> Vectors, int Tokens);
