namespace Kanabol.Application.Abstractions;

/// <summary>The claims in an access token, under the names they have in the JWT.</summary>
public static class AuthClaims
{
    public const string Subject = "sub";
    public const string Permission = "permission";
    public const string MustChangePassword = "must_change_password";
}
