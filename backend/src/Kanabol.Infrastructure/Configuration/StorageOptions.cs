using System.ComponentModel.DataAnnotations;

namespace Kanabol.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string Root { get; set; } = "";

    /// <summary>
    /// The extensions a file may have, as one value separated by commas, each with or without the dot, in
    /// any case (SPEC section 10). The default is in appsettings.json, and Storage__AllowedExtensions=pdf,png
    /// replaces the whole of it.
    /// </summary>
    [Required]
    public string AllowedExtensions { get; set; } = "";

    /// <summary>Each extension in the value once, in lower case and without the dot, in the order given.</summary>
    public static IReadOnlyList<string> ExtensionsIn(string allowed) =>
    [
        .. allowed.Split(',', StringSplitOptions.TrimEntries)
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .Where(extension => extension.Length > 0)
            .Distinct(),
    ];
}
