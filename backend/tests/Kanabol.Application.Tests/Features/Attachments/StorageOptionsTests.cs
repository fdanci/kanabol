using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure;
using Kanabol.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Tests.Features.Attachments;

/// <summary>
/// Storage:AllowedExtensions is one value with commas (Tasks.md group 16; SPEC sections 10 and 13.3):
/// Storage__AllowedExtensions=pdf,png in compose replaces the whole default list of appsettings.json,
/// where a list bound by index replaced only the elements it named.
/// </summary>
public sealed class StorageOptionsTests : IDisposable
{
    private const string Defaults = "pdf,png,jpg,jpeg,gif,txt,md,csv,xlsx,docx,zip";

    private readonly string _prefix = $"KANABOL_TEST_{Guid.NewGuid():N}_";
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        _providers.ForEach(provider => provider.Dispose());
        Environment.SetEnvironmentVariable($"{_prefix}Storage__AllowedExtensions", null);
        Environment.SetEnvironmentVariable($"{_prefix}Storage__AllowedExtensions__0", null);
    }

    /// <summary>The settings of a file under environment variables, as the API reads them in Docker.</summary>
    private ServiceProvider Services(Dictionary<string, string?> file, Dictionary<string, string> environment)
    {
        foreach (var (name, value) in environment)
        {
            Environment.SetEnvironmentVariable(_prefix + name, value);
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=unused",
                ["Jwt:Secret"] = "only-for-these-tests-and-long-enough-to-pass",
                ["Storage:Root"] = Path.GetTempPath(),
            })
            .AddInMemoryCollection(file)
            .AddEnvironmentVariables(_prefix)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private static Dictionary<string, string?> DefaultFile() => new() { ["Storage:AllowedExtensions"] = Defaults };

    [Fact]
    public void AllowedExtensions_OneValueWithCommas_GivesEachOnceInLowerCaseWithoutTheDot()
    {
        var services = Services(new() { ["Storage:AllowedExtensions"] = " pdf, .PNG,txt ,pdf" }, []);

        services.GetRequiredService<IAttachmentRules>().AllowedExtensions.Should().Equal("pdf", "png", "txt");
    }

    [Fact]
    public void AllowedExtensions_FromTheEnvironment_ReplaceTheWholeDefaultList()
    {
        var services = Services(DefaultFile(), new() { ["Storage__AllowedExtensions"] = "pdf,png" });

        services.GetRequiredService<IAttachmentRules>().AllowedExtensions.Should().Equal("pdf", "png");
    }

    [Fact]
    public void AllowedExtensions_WithoutAnOverride_AreTheDefaultList()
    {
        var services = Services(DefaultFile(), []);

        services.GetRequiredService<IAttachmentRules>().AllowedExtensions.Should().Equal(Defaults.Split(','));
    }

    [Fact]
    public void AllowedExtensions_GivenByIndex_AsBeforeGroup16_RefuseToStart()
    {
        // Bound to one value, __0 would be dropped without a word, and the default list would stay.
        var services = Services(DefaultFile(), new() { ["Storage__AllowedExtensions__0"] = "exe" });

        var read = () => services.GetRequiredService<IOptions<StorageOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*Storage__AllowedExtensions=pdf,png*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , .,")]
    public void AllowedExtensions_WithoutAnyExtension_RefuseToStart(string allowed)
    {
        var services = Services(new() { ["Storage:AllowedExtensions"] = allowed }, []);

        var read = () => services.GetRequiredService<IOptions<StorageOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*AllowedExtensions*");
    }
}
