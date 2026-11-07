using System.ComponentModel.DataAnnotations;

namespace Kanabol.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string Root { get; set; } = "";

    /// <summary>
    /// The extensions a file may have, with or without the dot, in any case (SPEC section 10). The list is in
    /// appsettings.json, not here: binding a list adds to the one already there.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<string> AllowedExtensions { get; set; } = [];
}
