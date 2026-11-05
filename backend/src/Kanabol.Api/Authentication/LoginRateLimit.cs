using System.Globalization;
using System.Threading.RateLimiting;
using Kanabol.Api.Errors;
using Kanabol.Application.Features.Auth;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using IPNetwork = System.Net.IPNetwork;

namespace Kanabol.Api.Authentication;

/// <summary>At most 5 login attempts per minute from one address (SPEC section 4.5).</summary>
public static class LoginRateLimit
{
    public const string PolicyName = "login";

    private const int PermitLimit = 5;
    private const string LoggerCategory = "Kanabol.Api.Authentication.LoginRateLimit";

    private static readonly TimeSpan _window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The API only ever sees nginx, whose address would make all the users share one limit. So it
    /// reads the client's address from X-Forwarded-For, but only from a connection on a private
    /// network, where the proxy is. nginx overwrites that header, so a client can't choose its value.
    /// </summary>
    public static IServiceCollection AddLoginRateLimit(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.KnownIPNetworks.Add(IPNetwork.Parse("10.0.0.0/8"));
            options.KnownIPNetworks.Add(IPNetwork.Parse("172.16.0.0/12"));
            options.KnownIPNetworks.Add(IPNetwork.Parse("192.168.0.0/16"));
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = Reject;
            options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = PermitLimit, Window = _window }));
        });

        return services;
    }

    private static async ValueTask Reject(OnRejectedContext rejected, CancellationToken ct)
    {
        var context = rejected.HttpContext;

        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory)
            .LogWarning("Too many login attempts from {ClientAddress}", context.Connection.RemoteIpAddress);

        var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : _window;
        context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var problem = AuthErrors.TooManyLoginAttempts.ToProblemDetails();
        problem.Status = StatusCodes.Status429TooManyRequests;
        problem.Title = "Too Many Requests";

        await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
        });
    }
}
