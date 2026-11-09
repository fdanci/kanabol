namespace Kanabol.Infrastructure.Configuration;

/// <summary>
/// The refresh tokens' cleanup (SPEC section 4.5): how often RefreshTokensCleanupWorker deletes the rows that
/// ended over 30 days ago, and whether it does at all. Auth__RefreshTokenCleanupInterval=06:00:00 and
/// Auth__RefreshTokenCleanupEnabled=false from the environment.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(1);

    /// <summary>PeriodicTimer stops at about 49 days, and rows kept 30 days need no rarer cleanup.</summary>
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromDays(30);

    public TimeSpan RefreshTokenCleanupInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Off in the API's tests, whose old rows are their own.</summary>
    public bool RefreshTokenCleanupEnabled { get; set; } = true;
}
