using System.ComponentModel.DataAnnotations;

namespace Kanabol.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    // Comes from ConnectionStrings:Default, where the rest of the tooling expects it.
    [Required]
    public string? ConnectionString { get; set; }

    public bool MigrateOnStartup { get; set; }
}
