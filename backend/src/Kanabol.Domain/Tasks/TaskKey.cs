using Kanabol.Domain.Projects;

namespace Kanabol.Domain.Tasks;

/// <summary>
/// The key a task is shown and opened by, {Project.Key}-{Number} as KAN-123 (SPEC section 5.1). The
/// project key never changes and numbers aren't reused, so a key keeps pointing at the same task.
/// </summary>
public static class TaskKey
{
    public static string Format(string projectKey, int number) => $"{projectKey}-{number}";

    /// <summary>
    /// Only the one way of writing it: a valid project key, in any case, a dash, and a number from 1 on
    /// in the digits 0 to 9, without leading zeros. Anything else is no key at all.
    /// </summary>
    public static bool TryParse(string? key, out string projectKey, out int number)
    {
        projectKey = "";
        number = 0;

        var text = key?.Trim() ?? "";
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return false;
        }

        var prefix = text[..dash];
        var digits = text[(dash + 1)..];
        if (!Project.IsValidKey(prefix) || digits.Length == 0 || digits[0] == '0' || !digits.All(digit => digit is >= '0' and <= '9'))
        {
            return false;
        }

        if (!int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        projectKey = Project.NormalizeKey(prefix);
        return true;
    }
}
