namespace Kanabol.Infrastructure.Configuration;

/// <summary>
/// The deadline alerts (SPEC section 4.3): how often OverdueTasksWorker looks for overdue tasks, and whether
/// it does at all. Alerts__OverdueCheckInterval=00:05:00 and Alerts__OverdueCheckEnabled=false in compose.
/// </summary>
public sealed class AlertsOptions
{
    public const string SectionName = "Alerts";

    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    /// <summary>A longer interval would make an alert come days late, and PeriodicTimer stops at about 49 days.</summary>
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromDays(1);

    public TimeSpan OverdueCheckInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Off in the API's tests, which run NotifyOverdueTasksCommand themselves.</summary>
    public bool OverdueCheckEnabled { get; set; } = true;
}
