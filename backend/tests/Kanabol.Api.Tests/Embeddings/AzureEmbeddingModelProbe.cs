using Kanabol.Application.Features.Search;
using Kanabol.Infrastructure.Configuration;
using Kanabol.Infrastructure.Embeddings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Kanabol.Api.Tests.Embeddings;

/// <summary>
/// The one call to the real Azure, by hand and outside CI (SPEC section 19.3): it runs only with
/// KANABOL_AZURE_PROBE=1 and the Embeddings:Azure user secrets of kanabol-api, and is skipped otherwise, which CI,
/// with no keys, always is. It costs a fraction of a cent. From backend/:
/// KANABOL_AZURE_PROBE=1 dotnet test tests/Kanabol.Api.Tests --filter AzureEmbeddingModelProbe --logger "console;verbosity=detailed"
/// </summary>
public sealed class AzureEmbeddingModelProbe(ITestOutputHelper output)
{
    [AzureProbeFact]
    public async Task RealAzure_GivesCloseVectorsForTextsCloseInMeaning()
    {
        var settings = AzureProbeFactAttribute.Settings();
        var model = new AzureEmbeddingModel(
            Options.Create(new EmbeddingsOptions { Enabled = true, Model = settings["Embeddings:Model"] ?? "text-embedding-3-small" }),
            Options.Create(new AzureEmbeddingsOptions
            {
                Endpoint = settings["Embeddings:Azure:Endpoint"]!,
                Deployment = settings["Embeddings:Azure:Deployment"]!,
                ApiKey = settings["Embeddings:Azure:ApiKey"]!,
            }));

        var result = await model.EmbedAsync(
            ["Pregătește raportul lunar pentru client", "Fă situația pe luna asta pentru client", "Repară imprimanta de la etajul doi"],
            CancellationToken.None);

        var close = Cosine(result.Vectors[0], result.Vectors[1]);
        var far = Cosine(result.Vectors[0], result.Vectors[2]);
        output.WriteLine($"{result.Vectors[0].Count} numbers per vector, {result.Tokens} tokens");
        output.WriteLine($"raport vs situatie:   {close:F3}");
        output.WriteLine($"raport vs imprimanta: {far:F3}");
        result.Vectors.Should().HaveCount(3).And.OnlyContain(vector => vector.Count == result.Vectors[0].Count);
        result.Tokens.Should().BePositive();
        close.Should().BeGreaterThan(far, "two texts about the monthly report are closer than one about a printer");
    }

    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right) =>
        left.Zip(right, (a, b) => (double)a * b).Sum()
        / (Math.Sqrt(left.Sum(a => (double)a * a)) * Math.Sqrt(right.Sum(b => (double)b * b)));
}

/// <summary>Skips the probe unless KANABOL_AZURE_PROBE=1 and the three Azure user secrets are there.</summary>
public sealed class AzureProbeFactAttribute : FactAttribute
{
    public AzureProbeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KANABOL_AZURE_PROBE") != "1")
        {
            Skip = "Calls the real Azure: run it by hand with KANABOL_AZURE_PROBE=1.";
            return;
        }

        var settings = Settings();
        string[] needed = ["Embeddings:Azure:Endpoint", "Embeddings:Azure:Deployment", "Embeddings:Azure:ApiKey"];
        var missing = needed.Where(name => string.IsNullOrWhiteSpace(settings[name])).ToList();
        if (missing.Count > 0)
        {
            Skip = $"The kanabol-api user secrets have no {string.Join(", ", missing)}.";
        }
    }

    public static IConfiguration Settings() => new ConfigurationBuilder()
        .AddUserSecrets("kanabol-api")
        .AddEnvironmentVariables()
        .Build();
}
