using System.Security.Cryptography;
using System.Text;
using Kanabol.Application.Abstractions;

namespace Kanabol.Tests.Shared.Fakes;

/// <summary>
/// The model the tests use, since CI has no keys (SPEC section 19.3). The same text always gets the same vector,
/// made from its SHA-256, so runs are repeatable; it understands nothing, so a test that needs texts close in
/// meaning gives their vectors itself. It keeps the calls, and fails on demand, as Azure would when it's down. Like
/// Azure, it refuses the whole call when a text is over its token limit, so a test sees what Fit saves a batch from.
/// </summary>
public sealed class FakeEmbeddingModel(
    string name = FakeEmbeddingModel.DefaultName,
    int dimensions = FakeEmbeddingModel.DefaultDimensions,
    int tokenLimit = FakeEmbeddingModel.DefaultTokenLimit) : IEmbeddingModel
{
    public const string DefaultName = "fake-embedding-model";

    public const int DefaultDimensions = 8;

    /// <summary>The limit of OpenAI's embedding models.</summary>
    public const int DefaultTokenLimit = 8191;

    private readonly Dictionary<string, float[]> _given = [];
    private readonly List<IReadOnlyList<string>> _calls = [];
    private Exception? _failure;
    private int _failingFrom;

    public string Name { get; } = name;

    /// <summary>Each call's texts, in order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Calls => _calls;

    /// <summary>This text gets this vector from now on, in place of the one from its SHA-256.</summary>
    public FakeEmbeddingModel Give(string text, params float[] vector)
    {
        _given[text] = vector;
        return this;
    }

    /// <summary>
    /// Every call fails, until Recover, with an EmbeddingFailedException unless another is given: from now on, or
    /// after the given number of calls that still answer, as Azure going down in the middle of a run.
    /// </summary>
    public FakeEmbeddingModel Fail(Exception? failure = null, int afterCalls = 0)
    {
        _failure = failure ?? new EmbeddingFailedException("The fake model was told to fail.");
        _failingFrom = _calls.Count + afterCalls;
        return this;
    }

    public FakeEmbeddingModel Recover()
    {
        _failure = null;
        return this;
    }

    /// <summary>The vector a text gets when no test gave one.</summary>
    public float[] VectorOf(string text)
    {
        if (_given.TryGetValue(text, out var given))
        {
            return given;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return [.. Enumerable.Range(0, dimensions).Select(i => (hash[i % hash.Length] - 127.5f) / 127.5f)];
    }

    /// <summary>About one token per four characters, at least one per text, as the real models count.</summary>
    public static int TokensOf(string text) => Math.Max(1, (text.Length + 3) / 4);

    /// <summary>The text's first tokenLimit tokens, four characters each, as TokensOf counts them.</summary>
    public string Fit(string text) => TokensOf(text) <= tokenLimit ? text : text[..(tokenLimit * 4)];

    public Task<EmbeddedTexts> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _calls.Add([.. texts]);
        if (_failure is not null && _calls.Count > _failingFrom)
        {
            throw _failure;
        }

        if (texts.Any(text => TokensOf(text) > tokenLimit))
        {
            throw new EmbeddingFailedException($"The fake model refused the {texts.Count} texts with status 400: one is over {tokenLimit} tokens.");
        }

        IReadOnlyList<IReadOnlyList<float>> vectors = [.. texts.Select(VectorOf)];
        return Task.FromResult(new EmbeddedTexts(vectors, texts.Sum(TokensOf)));
    }
}
