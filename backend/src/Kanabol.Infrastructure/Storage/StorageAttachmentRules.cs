using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Kanabol.Infrastructure.Storage;

/// <summary>Storage:AllowedExtensions, in lower case and without the dot, however it was written.</summary>
public sealed class StorageAttachmentRules(IOptions<StorageOptions> options) : IAttachmentRules
{
    public IReadOnlyCollection<string> AllowedExtensions { get; } =
    [
        .. options.Value.AllowedExtensions
            .Select(extension => extension.Trim().TrimStart('.').ToLowerInvariant())
            .Where(extension => extension.Length > 0)
            .Distinct(),
    ];
}
