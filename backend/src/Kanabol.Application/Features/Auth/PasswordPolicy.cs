using System.Collections.Frozen;

namespace Kanabol.Application.Features.Auth;

/// <summary>What a new password has to meet. The form of the rule is in <see cref="PasswordRuleExtensions"/>.</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 10;

    /// <summary>Only so nobody can make the server hash a whole request body.</summary>
    public const int MaximumLength = 256;

    public const string CommonPasswordsResource = "Kanabol.Application.common-passwords.txt";

    private static readonly FrozenSet<string> _commonPasswords = LoadCommonPasswords();

    public static bool IsCommon(string password) => _commonPasswords.Contains(password.Trim());

    private static FrozenSet<string> LoadCommonPasswords()
    {
        using var stream = typeof(PasswordPolicy).Assembly.GetManifestResourceStream(CommonPasswordsResource)
            ?? throw new InvalidOperationException($"The resource {CommonPasswordsResource} is missing.");
        using var reader = new StreamReader(stream);

        var passwords = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            passwords.Add(line);
        }

        return passwords.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
