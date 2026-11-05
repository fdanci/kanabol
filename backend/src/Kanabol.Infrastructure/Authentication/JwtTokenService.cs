using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kanabol.Infrastructure.Authentication;

public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    private const int RefreshTokenBytes = 32;

    private static readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(Guid userId, IEnumerable<string> permissions, bool mustChangePassword)
    {
        var jwt = options.Value;
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(jwt.AccessTokenMinutes);

        var claims = new List<Claim> { new(AuthClaims.Subject, userId.ToString()) };
        claims.AddRange(permissions.Select(permission => new Claim(AuthClaims.Permission, permission)));
        if (mustChangePassword)
        {
            claims.Add(new Claim(AuthClaims.MustChangePassword, "true"));
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expiresAt);
    }

    public NewRefreshToken CreateRefreshToken()
    {
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

        return new NewRefreshToken(value, HashRefreshToken(value), clock.UtcNow.AddDays(options.Value.RefreshTokenDays));
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
