using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure.Configuration;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace Kanabol.Infrastructure.Storage;

/// <summary>
/// Files on the local disk, under Storage:Root, a Docker volume in production (SPEC section 10). Each one is
/// {yyyy}/{MM}/{guid}{ext}, the extension in lower case: the original name never reaches the disk, and the
/// key is the only way back to the file. A relative root is taken from the folder the API runs in, its
/// project's folder under dotnet run.
/// </summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options, IClock clock) : IFileStorage
{
    private static readonly FileExtensionContentTypeProvider _contentTypes = new();

    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.Root));

    public async Task<StoredFile> SaveAsync(Stream content, string fileName, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var key = $"{now:yyyy}/{now:MM}/{Guid.CreateVersion7():N}{extension}";
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81_920, useAsync: true);
            await content.CopyToAsync(file, ct);
            return new StoredFile(key, file.Length, ContentTypeOf(extension));
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream>(new FileStream(PathOf(key), FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81_920, useAsync: true));

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        File.Delete(PathOf(key));
        return Task.CompletedTask;
    }

    private static string ContentTypeOf(string extension) =>
        _contentTypes.TryGetContentType($"file{extension}", out var contentType) ? contentType : "application/octet-stream";

    /// <summary>Keys come from this class, through the database; one that leads outside the root is refused all the same.</summary>
    private string PathOf(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("A storage key stays inside the storage root.", nameof(key));
        }

        return path;
    }
}
