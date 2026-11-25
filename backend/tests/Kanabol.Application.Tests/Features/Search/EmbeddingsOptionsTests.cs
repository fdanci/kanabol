using Kanabol.Application.Features.Search;
using Kanabol.Infrastructure;
using Kanabol.Infrastructure.Configuration;
using Kanabol.Infrastructure.Embeddings;
using Kanabol.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Tests.Features.Search;

/// <summary>
/// SPEC section 19.4: the vectorization is off by default, and then nothing of it is checked, so the API starts
/// without any key. On, Embeddings:Model, Embeddings:Interval, Embeddings:BatchSize and Embeddings:Azure are
/// checked at start, and a refusal names the setting, never its value. In compose they come as Embeddings__*.
/// </summary>
public sealed class EmbeddingsOptionsTests : IDisposable
{
    private const string Key = "the-secret-key-from-the-portal";

    private static readonly Dictionary<string, string> _on = new()
    {
        ["Embeddings__Enabled"] = "true",
        ["Embeddings__Model"] = "text-embedding-3-small",
        ["Embeddings__Azure__Endpoint"] = "https://example.cognitiveservices.azure.com/",
        ["Embeddings__Azure__Deployment"] = "text-embedding-3-small",
        ["Embeddings__Azure__ApiKey"] = Key,
    };

    private readonly string _prefix = $"KANABOL_TEST_{Guid.NewGuid():N}_";
    private readonly List<string> _set = [];
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        _providers.ForEach(provider => provider.Dispose());
        _set.ForEach(name => Environment.SetEnvironmentVariable(name, null));
    }

    private ServiceProvider Services(Dictionary<string, string> environment)
    {
        foreach (var (name, value) in environment)
        {
            Environment.SetEnvironmentVariable(_prefix + name, value);
            _set.Add(_prefix + name);
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=unused",
                ["Jwt:Secret"] = "only-for-these-tests-and-long-enough-to-pass",
                ["Storage:Root"] = Path.GetTempPath(),
                ["Storage:AllowedExtensions"] = "pdf",
            })
            .AddEnvironmentVariables(_prefix)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private static Dictionary<string, string> On(string? without = null, Dictionary<string, string>? with = null)
    {
        var settings = new Dictionary<string, string>(_on);
        if (without is not null)
        {
            settings.Remove(without);
        }

        foreach (var (name, value) in with ?? [])
        {
            settings[name] = value;
        }

        return settings;
    }

    private static EmbeddingsOptions Embeddings(ServiceProvider services) => services.GetRequiredService<IOptions<EmbeddingsOptions>>().Value;

    private static AzureEmbeddingsOptions Azure(ServiceProvider services) => services.GetRequiredService<IOptions<AzureEmbeddingsOptions>>().Value;

    [Fact]
    public void WithoutSettings_TheVectorizationIsOff_AndNothingIsChecked()
    {
        var services = Services([]);

        var embeddings = Embeddings(services);
        var azure = Azure(services);

        embeddings.Should().BeEquivalentTo(new { Enabled = false, Interval = TimeSpan.FromMinutes(5), BatchSize = 64, Model = "" });
        azure.Should().BeEquivalentTo(new { Endpoint = "", Deployment = "", ApiKey = "" });
    }

    [Fact]
    public void Off_WithSettingsOutOfRange_StillStarts()
    {
        var services = Services(new()
        {
            ["Embeddings__Enabled"] = "false",
            ["Embeddings__Interval"] = "00:00:01",
            ["Embeddings__BatchSize"] = "0",
            ["Embeddings__Azure__Endpoint"] = "not an address",
        });

        var reading = () => (Embeddings(services), Azure(services));

        reading.Should().NotThrow();
    }

    [Fact]
    public void On_FromTheEnvironment_TakesEveryValue()
    {
        var services = Services(On(with: new() { ["Embeddings__Interval"] = "00:10:00", ["Embeddings__BatchSize"] = "16" }));

        Embeddings(services).Should().BeEquivalentTo(new { Enabled = true, Interval = TimeSpan.FromMinutes(10), BatchSize = 16, Model = "text-embedding-3-small" });
        Azure(services).Should().BeEquivalentTo(new
        {
            Endpoint = "https://example.cognitiveservices.azure.com/",
            Deployment = "text-embedding-3-small",
            ApiKey = Key,
        });
    }

    [Theory]
    [InlineData("Embeddings__Model", "Embeddings:Model is needed")]
    [InlineData("Embeddings__Azure__Endpoint", "Embeddings:Azure:Endpoint is needed")]
    [InlineData("Embeddings__Azure__Deployment", "Embeddings:Azure:Deployment is needed")]
    [InlineData("Embeddings__Azure__ApiKey", "Embeddings:Azure:ApiKey is needed")]
    public void On_WithoutOneValue_StopsTheStart_NamingIt(string missing, string message)
    {
        var services = Services(On(without: missing));

        var reading = () => (Embeddings(services), Azure(services));

        reading.Should().Throw<OptionsValidationException>().WithMessage($"*{message}*");
    }

    [Theory]
    [InlineData("http://example.cognitiveservices.azure.com/")]
    [InlineData("example.cognitiveservices.azure.com")]
    [InlineData("http://localhost.example.com/")]
    [InlineData("http://10.0.0.10:5296/")]
    public void On_WithAnEndpointThatIsntHttps_StopsTheStart_WithoutPrintingAnyValue(string endpoint)
    {
        var services = Services(On(with: new() { ["Embeddings__Azure__Endpoint"] = endpoint }));

        var reading = () => Azure(services);

        var refusal = reading.Should().Throw<OptionsValidationException>().Which.Message;
        refusal.Should().Contain("Embeddings:Azure:Endpoint").And.Contain("https");
        refusal.Should().NotContain(endpoint).And.NotContain(Key);
    }

    // The key in clear text, but only to this machine: a stand-in for Azure, as check-search.sh runs, or a local model.
    [Theory]
    [InlineData("http://127.0.0.1:5296/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://[::1]:5000/")]
    public void On_WithAnHttpEndpointOnThisMachine_Starts(string endpoint)
    {
        var services = Services(On(with: new() { ["Embeddings__Azure__Endpoint"] = endpoint }));

        Azure(services).Endpoint.Should().Be(endpoint);
    }

    [Theory]
    [InlineData("00:00:59", "Embeddings:Interval")]
    [InlineData("1.00:00:01", "Embeddings:Interval")]
    public void On_WithAnIntervalOutOfRange_StopsTheStart(string interval, string setting)
    {
        var services = Services(On(with: new() { ["Embeddings__Interval"] = interval }));

        var reading = () => Embeddings(services);

        reading.Should().Throw<OptionsValidationException>().WithMessage($"*{setting} must be between 00:01:00 and 1.00:00:00*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2049")]
    public void On_WithABatchSizeOutOfRange_StopsTheStart(string batchSize)
    {
        var services = Services(On(with: new() { ["Embeddings__BatchSize"] = batchSize }));

        var reading = () => Embeddings(services);

        reading.Should().Throw<OptionsValidationException>().WithMessage("*Embeddings:BatchSize must be between 1 and 2048*");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2048")]
    public void On_WithABatchSizeAtTheEdges_IsAccepted(string batchSize) =>
        Embeddings(Services(On(with: new() { ["Embeddings__BatchSize"] = batchSize }))).BatchSize.Should().Be(int.Parse(batchSize, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Worker_StartsAfterTheDatabaseIsReady()
    {
        var hosted = Services([]).GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        hosted.Should().ContainInOrder(typeof(DatabaseInitializer), typeof(EmbeddingsWorker));
    }
}
