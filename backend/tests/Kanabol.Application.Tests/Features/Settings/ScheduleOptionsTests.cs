using Kanabol.Application.Features.Settings;
using Kanabol.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Tests.Features.Settings;

/// <summary>
/// SPEC sections 4.4 and 13.3: Schedule:TimeZone, Europe/Bucharest by default, Schedule__TimeZone=Europe/Vienna in
/// compose; a zone the system doesn't know stops the start.
/// </summary>
public sealed class ScheduleOptionsTests : IDisposable
{
    private readonly string _prefix = $"KANABOL_TEST_{Guid.NewGuid():N}_";
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        _providers.ForEach(provider => provider.Dispose());
        Environment.SetEnvironmentVariable($"{_prefix}Schedule__TimeZone", null);
    }

    private ScheduleOptions Schedule(string? zone)
    {
        if (zone is not null)
        {
            Environment.SetEnvironmentVariable($"{_prefix}Schedule__TimeZone", zone);
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
        return provider.GetRequiredService<IOptions<ScheduleOptions>>().Value;
    }

    [Fact]
    public void Schedule_WithoutSettings_IsInBucharest()
    {
        var schedule = Schedule(null);

        schedule.TimeZone.Should().Be("Europe/Bucharest");
        schedule.Zone().GetUtcOffset(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)).Should().Be(TimeSpan.FromHours(3));
    }

    [Fact]
    public void Schedule_FromTheEnvironment_TakesTheZone() =>
        Schedule("Europe/Vienna").Zone().GetUtcOffset(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)).Should().Be(TimeSpan.FromHours(1));

    [Theory]
    [InlineData("Europe/Atlantida")]
    [InlineData("nu-e-un-fus")]
    [InlineData(" ")]
    public void Schedule_WithAZoneTheSystemDoesntKnow_StopsTheStart(string zone)
    {
        var reading = () => Schedule(zone);

        reading.Should().Throw<OptionsValidationException>().WithMessage("*Schedule:TimeZone*");
    }
}
