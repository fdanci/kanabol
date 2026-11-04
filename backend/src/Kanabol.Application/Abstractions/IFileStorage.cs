namespace Kanabol.Application.Abstractions;

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string fileName, CancellationToken ct);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}
