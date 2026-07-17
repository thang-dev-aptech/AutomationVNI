namespace Backend.Shared.Storage;

public interface IFileStorageService
{
    Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default);

    Task<FileSaveResult> SaveBytesAsync(
        byte[] data, string folder, string extension, string contentType, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default);

    Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default);

    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
