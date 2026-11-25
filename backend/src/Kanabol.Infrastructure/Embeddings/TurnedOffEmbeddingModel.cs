using Kanabol.Application.Abstractions;

namespace Kanabol.Infrastructure.Embeddings;

/// <summary>
/// The model while Embeddings:Enabled is false (SPEC section 19.4): there's no model to call, so a call fails at
/// once instead of going anywhere. Nothing should call it: the worker doesn't run while the vectorization is off.
/// </summary>
public sealed class TurnedOffEmbeddingModel : IEmbeddingModel
{
    private const string Off = "The vectorization is off: Embeddings:Enabled is false.";

    public string Name => "";

    public string Fit(string text) => throw new EmbeddingFailedException(Off);

    public Task<EmbeddedTexts> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
        throw new EmbeddingFailedException(Off);
}
