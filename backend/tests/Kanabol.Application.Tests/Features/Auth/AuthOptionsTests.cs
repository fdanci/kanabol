using Kanabol.Infrastructure;
using Kanabol.Infrastructure.Alerts;
using Kanabol.Infrastructure.Authentication;
using Kanabol.Infrastructure.Configuration;
using Kanabol.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Tests.Features.Auth;

/// <summary>
/// SPEC section 4.5: Auth:RefreshTokenCleanupInterval, a day by default and at least an hour, checked at
/// start; Auth:RefreshTokenCleanupEnabled, on by default. In compose they'd come as
/// Auth__RefreshTokenCleanupInterval and Auth__RefreshTokenCleanupEnabled.
/// </summary>
public sealed class AuthOptionsTests : IDisposable
{
    private readonly string _prefix = $"KANABOL_TEST_{Guid.NewGuid():N}_";
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        _providers.ForEach(provider => provider.Dispose());
        Environment.SetEnvironmentVariable($"{_prefix}Auth__RefreshTokenCleanupInterval", null);
        Environment.SetEnvironmentVariable($"{_prefix}Auth__RefreshTokenCleanupEnabled", null);
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

    private static AuthOptions Auth(ServiceProvider services) => services.GetRequiredService<IOptions<AuthOptions>>().Value;

    [Fact]
    public void Cleanup_WithoutSettings_RunsOnceADay()
    {
        var auth = Auth(Services([]));

        auth.RefreshTokenCleanupInterval.Should().Be(TimeSpan.FromDays(1));
        auth.RefreshTokenCleanupEnabled.Should().BeTrue();
    }

    [Fact]
    public void Cleanup_FromTheEnvironment_TakesTheIntervalAndTheSwitch()
    {
        var auth = Auth(Services(new()
        {
            ["Auth__RefreshTokenCleanupInterval"] = "06:00:00",
            ["Auth__RefreshTokenCleanupEnabled"] = "false",
        }));

        auth.RefreshTokenCleanupInterval.Should().Be(TimeSpan.FromHours(6));
        auth.RefreshTokenCleanupEnabled.Should().BeFalse();
    }

    [Fact]
    public void Cleanup_WithAnIntervalOfAnHour_IsAccepted() =>
        Auth(Services(new() { ["Auth__RefreshTokenCleanupInterval"] = "01:00:00" })).RefreshTokenCleanupInterval.Should().Be(TimeSpan.FromHours(1));

    [Theory]
    [InlineData("00:59:59")]
    [InlineData("00:00:00")]
    [InlineData("-1.00:00:00")]
    public void Cleanup_WithAnIntervalUnderAnHour_StopsTheStart(string interval)
    {
        var services = Services(new() { ["Auth__RefreshTokenCleanupInterval"] = interval });

        var reading = () => Auth(services);

        reading.Should().Throw<OptionsValidationException>().WithMessage("*Auth:RefreshTokenCleanupInterval*at least 01:00:00*");
    }

    // PeriodicTimer takes at most about 49 days; past the 30 days the rows are kept, a cleanup would come late anyway.
    [Fact]
    public void Cleanup_WithAnIntervalOverThirtyDays_StopsTheStart()
    {
        var services = Services(new() { ["Auth__RefreshTokenCleanupInterval"] = "30.00:00:01" });

        var reading = () => Auth(services);

        reading.Should().Throw<OptionsValidationException>().WithMessage("*Auth:RefreshTokenCleanupInterval*at most 30.00:00:00*");
    }

    [Fact]
    public void Worker_StartsAfterTheDatabaseIsReady()
    {
        var hosted = Services([]).GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        hosted.Should().ContainInOrder(typeof(DatabaseInitializer), typeof(OverdueTasksWorker), typeof(RefreshTokensCleanupWorker));
    }
}
