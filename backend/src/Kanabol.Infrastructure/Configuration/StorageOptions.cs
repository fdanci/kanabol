using System.ComponentModel.DataAnnotations;

namespace Kanabol.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string Root { get; set; } = "";
}
