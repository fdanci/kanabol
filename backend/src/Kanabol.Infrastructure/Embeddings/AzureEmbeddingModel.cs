using System.ClientModel;
using System.Text;
using Azure.AI.OpenAI;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Search;
using Kanabol.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;
using OpenAI.Embeddings;

namespace Kanabol.Infrastructure.Embeddings;

/// <summary>
/// The vectors from an Azure deployment (SPEC section 19.3), through the official SDK: one call per batch, to the
/// deployment Embeddings:Azure:Deployment names. The vectors are put back in the texts' order by their index, the
/// tokens come from the answer's usage, and any failure becomes an EmbeddingFailedException whose message has the
/// status, never a text or the key. Fit cuts a text at 8191 tokens, counted by the models' own tokenizer.
/// </summary>
public sealed class AzureEmbeddingModel : IEmbeddingModel
{
    /// <summary>The longest text OpenAI's embedding models take, in their tokens: a longer one fails the whole call.</summary>
    public const int TokenLimit = 8191;

    // cl100k_base, the encoding of OpenAI's embedding models (ada-002 and the two 3s), so the count is theirs. Its
    // vocabulary loads at the first text that might be too long, and only once.
    private static readonly Lazy<TiktokenTokenizer> _tokenizer = new(() => TiktokenTokenizer.CreateForEncoding("cl100k_base"));

    private readonly EmbeddingClient _client;

    public AzureEmbeddingModel(IOptions<EmbeddingsOptions> embeddings, IOptions<AzureEmbeddingsOptions> azure)
        : this(embeddings, azure, new AzureOpenAIClientOptions())
    {
    }

    /// <summary>With the client's options given, so the tests can send it to a fake answer instead of Azure.</summary>
    public AzureEmbeddingModel(IOptions<EmbeddingsOptions> embeddings, IOptions<AzureEmbeddingsOptions> azure, AzureOpenAIClientOptions clientOptions)
    {
        Name = embeddings.Value.Model;
        var settings = azure.Value;
        _client = new AzureOpenAIClient(new Uri(settings.Endpoint), new ApiKeyCredential(settings.ApiKey), clientOptions)
            .GetEmbeddingClient(settings.Deployment);
    }

    public string Name { get; }

    public string Fit(string text)
    {
        // A token is at least one byte of UTF-8, so a text with no more bytes than the limit fits without counting.
        if (Encoding.UTF8.GetByteCount(text) <= TokenLimit)
        {
            return text;
        }

        var end = _tokenizer.Value.GetIndexByTokenCount(text, TokenLimit, out _, out _);
        return end >= text.Length ? text : text[..end];
    }

    public async Task<EmbeddedTexts> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts.Count == 0)
        {
            return new EmbeddedTexts([], 0);
        }

        OpenAIEmbeddingCollection answer;
        try
        {
            answer = (await _client.GenerateEmbeddingsAsync(texts, cancellationToken: ct)).Value;
        }
        catch (ClientResultException failure)
        {
            // The status says enough: 401 for a wrong key, 404 for a wrong deployment, 429 for the TPM limit.
            throw new EmbeddingFailedException($"Azure refused the {texts.Count} texts with status {failure.Status}.", failure);
        }
        catch (HttpRequestException failure)
        {
            throw new EmbeddingFailedException($"Azure couldn't be reached for the {texts.Count} texts.", failure);
        }

        var vectors = new IReadOnlyList<float>?[texts.Count];
        foreach (var embedding in answer)
        {
            if (embedding.Index < 0 || embedding.Index >= texts.Count || vectors[embedding.Index] is not null)
            {
                throw new EmbeddingFailedException($"Azure answered a vector for a text it wasn't sent, at index {embedding.Index}.");
            }

            vectors[embedding.Index] = embedding.ToFloats().ToArray();
        }

        if (Array.IndexOf(vectors, null) is var missing and >= 0)
        {
            throw new EmbeddingFailedException($"Azure answered no vector for the text at index {missing} of {texts.Count}.");
        }

        return new EmbeddedTexts(vectors!, answer.Usage.InputTokenCount);
    }
}
