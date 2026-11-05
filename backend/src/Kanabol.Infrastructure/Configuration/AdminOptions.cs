using System.ComponentModel.DataAnnotations;
using Kanabol.Application.Features.Auth;

namespace Kanabol.Infrastructure.Configuration;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";
    public const int MinimumPasswordLength = PasswordPolicy.MinimumLength;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    // Only needed the first time, while no admin exists yet, so it can be missing or blank later.
    public string? InitialPassword { get; set; }
}
