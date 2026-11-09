namespace Kanabol.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL's own ICU collations, which initdb makes when the server is built with ICU, as the official
/// images and postgres:17-alpine are, so no migration creates them (SPEC section 5.3).
/// </summary>
public static class Collations
{
    /// <summary>
    /// Romanian: Ă after A, Â after Ă, Î after I, Ș after S and Ț after T, and upper() and lower() on every
    /// letter. On the text columns the lists sort by, so every ORDER BY on them, upper() included, follows it.
    /// </summary>
    public const string Romanian = "ro-x-icu";
}
