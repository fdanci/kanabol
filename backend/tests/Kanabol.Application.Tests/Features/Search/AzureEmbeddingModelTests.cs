using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.AI.OpenAI;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Search;
using Kanabol.Infrastructure;
using Kanabol.Infrastructure.Configuration;
using Kanabol.Infrastructure.Embeddings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;

namespace Kanabol.Application.Tests.Features.Search;

/// <summary>
/// The Azure model against a fake answer, with no network and no key (SPEC section 19.3): what it sends, the vectors
/// put back in the texts' order, the tokens, the failures turned into EmbeddingFailedException without a text or
/// the key, and the texts cut at the models' 8191 tokens. The call to the real Azure is AzureEmbeddingModelProbe, by hand.
/// </summary>
public sealed class AzureEmbeddingModelTests
{
    private const string Key = "the-key-from-the-portal";
    private const string Endpoint = "https://example.cognitiveservices.azure.com/";

    /// <summary>The tokenizer of OpenAI's embedding models, to count what Fit kept.</summary>
    private static readonly TiktokenTokenizer _cl100k = TiktokenTokenizer.CreateForEncoding("cl100k_base");

    private static AzureEmbeddingModel Model(FakeAzure azure) => new(
        Options.Create(new EmbeddingsOptions { Enabled = true, Model = "text-embedding-3-small", BatchSize = 64 }),
        Options.Create(new AzureEmbeddingsOptions { Endpoint = Endpoint, Deployment = "my-deployment", ApiKey = Key }),
        new AzureOpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(azure)),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        });

    [Fact]
    public async Task Embed_SendsTheTextsToTheDeployment_WithTheKey()
    {
        var azure = new FakeAzure();

        await Model(azure).EmbedAsync(["Raport lunar", "Imprimanta"], CancellationToken.None);

        var request = azure.Requests.Should().ContainSingle().Subject;
        request.Uri.AbsolutePath.Should().Be("/openai/deployments/my-deployment/embeddings");
        request.ApiKey.Should().Be(Key);
        request.Inputs.Should().Equal("Raport lunar", "Imprimanta");
    }

    [Fact]
    public async Task Embed_PutsTheVectorsInTheTextsOrder_AndReadsTheTokens()
    {
        var azure = new FakeAzure { ReverseOrder = true, Tokens = 7 };

        var result = await Model(azure).EmbedAsync(["unu", "doi", "trei"], CancellationToken.None);

        result.Vectors.Should().HaveCount(3);
        result.Vectors[0].Should().Equal(FakeAzure.VectorFor(0));
        result.Vectors[1].Should().Equal(FakeAzure.VectorFor(1));
        result.Vectors[2].Should().Equal(FakeAzure.VectorFor(2));
        result.Tokens.Should().Be(7);
    }

    [Fact]
    public async Task Embed_WithNoText_CallsNothing()
    {
        var azure = new FakeAzure();

        var result = await Model(azure).EmbedAsync([], CancellationToken.None);

        result.Vectors.Should().BeEmpty();
        result.Tokens.Should().Be(0);
        azure.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Embed_WhenAzureRefuses_ThrowsEmbeddingFailed_WithTheStatus_AndNoTextOrKey(HttpStatusCode status)
    {
        var azure = new FakeAzure { Status = status };

        var embedding = () => Model(azure).EmbedAsync(["Text secret de task"], CancellationToken.None);

        var failure = (await embedding.Should().ThrowAsync<EmbeddingFailedException>()).Which;
        failure.Message.Should().Contain($"status {(int)status}");
        failure.Message.Should().NotContain("Text secret de task").And.NotContain(Key);
    }

    [Fact]
    public async Task Embed_WhenAzureIsUnreachable_ThrowsEmbeddingFailed()
    {
        var azure = new FakeAzure { Unreachable = true };

        var embedding = () => Model(azure).EmbedAsync(["Text"], CancellationToken.None);

        await embedding.Should().ThrowAsync<EmbeddingFailedException>();
    }

    [Fact]
    public async Task Embed_WhenAVectorIsMissing_ThrowsEmbeddingFailed()
    {
        var azure = new FakeAzure { DropLast = true };

        var embedding = () => Model(azure).EmbedAsync(["unu", "doi"], CancellationToken.None);

        (await embedding.Should().ThrowAsync<EmbeddingFailedException>()).WithMessage("*index 1 of 2*");
    }

    [Fact]
    public void Name_IsTheModelOfTheSettings_NotTheDeployment() =>
        Model(new FakeAzure()).Name.Should().Be("text-embedding-3-small");

    [Fact]
    public void Fit_ATextUnderTheLimit_StaysAsItIs()
    {
        var text = "Pregătește raportul lunar pentru client";

        Model(new FakeAzure()).Fit(text).Should().BeSameAs(text);
    }

    [Fact]
    public void Fit_ATextOverTheLimit_KeepsItsStart_UpTo8191Tokens()
    {
        var text = string.Concat(Enumerable.Repeat("Raportul lunar pentru client. ", 5_000));

        var fitted = Model(new FakeAzure()).Fit(text);

        text.Should().StartWith(fitted);
        var tokens = _cl100k.CountTokens(fitted);
        tokens.Should().BeLessThanOrEqualTo(AzureEmbeddingModel.TokenLimit).And.BeGreaterThan(AzureEmbeddingModel.TokenLimit - 10);
    }

    [Fact]
    public void Fit_ATextOfFewerCharactersThanTheLimit_ButMoreTokens_IsCutToo()
    {
        // 8,000 characters, but an emoji is two characters and several tokens.
        var text = string.Concat(Enumerable.Repeat("🦊", 4_000));
        _cl100k.CountTokens(text).Should().BeGreaterThan(AzureEmbeddingModel.TokenLimit);

        var fitted = Model(new FakeAzure()).Fit(text);

        fitted.Length.Should().BeLessThan(text.Length);
        _cl100k.CountTokens(fitted).Should().BeLessThanOrEqualTo(AzureEmbeddingModel.TokenLimit);
        char.IsHighSurrogate(fitted[^1]).Should().BeFalse("an emoji isn't cut in half");
    }

    [Fact]
    public async Task TurnedOff_RefusesEveryCall()
    {
        var model = new TurnedOffEmbeddingModel();
        var embedding = () => model.EmbedAsync(["Text"], CancellationToken.None);
        var fitting = () => model.Fit("Text");

        await embedding.Should().ThrowAsync<EmbeddingFailedException>().WithMessage("*Embeddings:Enabled*");
        fitting.Should().Throw<EmbeddingFailedException>().WithMessage("*Embeddings:Enabled*");
    }

    [Theory]
    [InlineData(false, typeof(TurnedOffEmbeddingModel))]
    [InlineData(true, typeof(AzureEmbeddingModel))]
    public void TheApp_GetsAzureOnlyWhenTheVectorizationIsOn(bool enabled, Type expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=unused",
                ["Jwt:Secret"] = "only-for-these-tests-and-long-enough-to-pass",
                ["Storage:Root"] = Path.GetTempPath(),
                ["Storage:AllowedExtensions"] = "pdf",
                ["Embeddings:Enabled"] = enabled.ToString(),
                ["Embeddings:Model"] = "text-embedding-3-small",
                ["Embeddings:Azure:Endpoint"] = Endpoint,
                ["Embeddings:Azure:Deployment"] = "my-deployment",
                ["Embeddings:Azure:ApiKey"] = Key,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEmbeddingModel>().Should().BeOfType(expected);
    }

    /// <summary>Answers as Azure's embeddings endpoint does, in the format the request asks for.</summary>
    private sealed class FakeAzure : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions _asAzureWrites = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public List<SeenRequest> Requests { get; } = [];

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public bool ReverseOrder { get; init; }

        public bool DropLast { get; init; }

        public bool Unreachable { get; init; }

        public int Tokens { get; init; } = 3;

        public static float[] VectorFor(int index) => [index, 0.5f, -0.25f * index];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            var inputs = body["input"]!.AsArray().Select(input => input!.GetValue<string>()).ToList();
            Requests.Add(new SeenRequest(request.RequestUri!, request.Headers.GetValues("api-key").Single(), inputs));
            if (Unreachable)
            {
                throw new HttpRequestException("No route to host.");
            }

            if (Status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(Status)
                {
                    Content = new StringContent("""{"error":{"code":"refused","message":"no"}}""", Encoding.UTF8, "application/json"),
                };
            }

            var base64 = body["encoding_format"]?.GetValue<string>() == "base64";
            var indexes = Enumerable.Range(0, DropLast ? inputs.Count - 1 : inputs.Count);
            var data = (ReverseOrder ? indexes.Reverse() : indexes).Select(index => new JsonObject
            {
                ["object"] = "embedding",
                ["index"] = index,
                ["embedding"] = base64
                    ? Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(VectorFor(index).AsSpan()))
                    : new JsonArray([.. VectorFor(index).Select(number => (JsonNode)number)]),
            });
            var answer = new JsonObject
            {
                ["object"] = "list",
                ["data"] = new JsonArray([.. data]),
                ["model"] = "text-embedding-3-small",
                ["usage"] = new JsonObject { ["prompt_tokens"] = Tokens, ["total_tokens"] = Tokens },
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                // As Azure writes it: the default encoder would write + as +, and the SDK reads the base64 raw.
                Content = new StringContent(answer.ToJsonString(_asAzureWrites), Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record SeenRequest(Uri Uri, string ApiKey, IReadOnlyList<string> Inputs);
}
