using Kanabol.Infrastructure;
using Kanabol.Infrastructure.Alerts;
using Kanabol.Infrastructure.Configuration;
using Kanabol.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Tests.Features.Notifications;

/// <summary>
/// SPEC sections 4.3 and 13.3: Alerts:OverdueCheckInterval, every 15 minutes by default and at least one
/// minute, checked at start; Alerts:OverdueCheckEnabled, on by default. In compose they come as
/// Alerts__OverdueCheckInterval=00:05:00 and Alerts__OverdueCheckEnabled=false.
/// </summary>
public sealed class AlertsOptionsTests : IDisposable
{
    private readonly string _prefix = $"KANABOL_TEST_{Guid.NewGuid():N}_";
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        _providers.ForEach(provider => provider.Dispose());
        Environment.SetEnvironmentVariable($"{_prefix}Alerts__OverdueCheckInterval", null);
        Environment.SetEnvironmentVariable($"{_prefix}Alerts__OverdueCheckEnabled", null);
    }

    private ServiceProvider Services(Dictionary<string, string> environment)
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

    private static AlertsOptions Alerts(ServiceProvider services) => services.GetRequiredService<IOptions<AlertsOptions>>().Value;

    [Fact]
    public void Alerts_WithoutSettings_CheckEveryFifteenMinutes()
    {
        var alerts = Alerts(Services([]));

        alerts.OverdueCheckInterval.Should().Be(TimeSpan.FromMinutes(15));
        alerts.OverdueCheckEnabled.Should().BeTrue();
    }

    [Fact]
    public void Alerts_FromTheEnvironment_TakeTheIntervalAndTheSwitch()
    {
        var alerts = Alerts(Services(new()
        {
            ["Alerts__OverdueCheckInterval"] = "00:05:00",
            ["Alerts__OverdueCheckEnabled"] = "false",
        }));

        alerts.OverdueCheckInterval.Should().Be(TimeSpan.FromMinutes(5));
        alerts.OverdueCheckEnabled.Should().BeFalse();
    }

    [Fact]
    public void Alerts_WithAnIntervalOfAMinute_AreAccepted() =>
        Alerts(Services(new() { ["Alerts__OverdueCheckInterval"] = "00:01:00" })).OverdueCheckInterval.Should().Be(TimeSpan.FromMinutes(1));

    [Theory]
    [InlineData("00:00:59")]
    [InlineData("00:00:00")]
    [InlineData("-00:15:00")]
    public void Alerts_WithAnIntervalUnderAMinute_StopTheStart(string interval)
    {
        var services = Services(new() { ["Alerts__OverdueCheckInterval"] = interval });

        var reading = () => Alerts(services);

        reading.Should().Throw<OptionsValidationException>().WithMessage("*Alerts:OverdueCheckInterval*at least 00:01:00*");
    }

    [Fact]
    public void Alerts_WithAnIntervalOverADay_StopTheStart()
    {
        var services = Services(new() { ["Alerts__OverdueCheckInterval"] = "1.00:00:01" });

        var reading = () => Alerts(services);

        reading.Should().Throw<OptionsValidationException>().WithMessage("*Alerts:OverdueCheckInterval*at most 1.00:00:00*");
    }

    [Fact]
    public void Worker_StartsAfterTheDatabaseIsReady()
    {
        var hosted = Services([]).GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        hosted.Should().ContainInOrder(typeof(DatabaseInitializer), typeof(OverdueTasksWorker));
    }
}
