using System.Text;
using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kanabol.Api.Authentication;

public static class JwtAuthenticationExtensions
{
    private const string AccessTokenParameter = "access_token";

    private static readonly PathString _hubsPath = new("/hubs");

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, options) =>
            {
                var jwt = options.Value;

                // Otherwise "sub" arrives under the long name .NET maps it to, and ICurrentUser reads "sub".
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = AuthClaims.Subject,

                    // One server signs and checks, so the default five minutes would only extend the 15.
                    ClockSkew = TimeSpan.Zero,
                };
                bearer.Events = new JwtBearerEvents { OnMessageReceived = TakeHubTokenFromTheQueryString };
            });

        return services;
    }

    /// <summary>
    /// A browser can't put a header on a WebSocket, so the hubs alone take the token from access_token in the
    /// query string (SPEC section 4.5). Everywhere else it stays in the header, out of addresses and logs.
    /// </summary>
    private static Task TakeHubTokenFromTheQueryString(MessageReceivedContext context)
    {
        if (context.Request.Path.StartsWithSegments(_hubsPath)
            && context.Request.Query.TryGetValue(AccessTokenParameter, out var token)
            && token.Count == 1)
        {
            context.Token = token[0];
        }

        return Task.CompletedTask;
    }
}
