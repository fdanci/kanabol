namespace Kanabol.Application.Features.Auth;

/// <summary>What the person making the request holds right now, according to the database.</summary>
public sealed class HeldPermissions(IEnumerable<string> permissions)
{
    private readonly HashSet<string> _permissions = [.. permissions];

    public bool Covers(IEnumerable<string> permissions) => permissions.All(_permissions.Contains);
}
