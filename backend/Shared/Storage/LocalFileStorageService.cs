using Microsoft.Extensions.Options;

namespace Backend.Shared.Storage;

public class LocalFileStorageService : IFileStorageService
{
    private readonly FileStorageOptions _options;
    private readonly string _rootPath;

    public LocalFileStorageService(IOptions<FileStorageOptions> options, IWebHostEnvironment env)
    {
        _options = options.Value;
        _rootPath = Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(env.ContentRootPath, _options.RootPath);

        Directory.CreateDirectory(_rootPath);
    }

    public async Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw new ArgumentException("File upload không hợp lệ hoặc rỗng");

        if (file.Length > _options.MaxUploadBytes)
            throw new ArgumentException($"File vượt quá giới hạn {_options.MaxUploadBytes} bytes");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        ValidateExtension(extension);

        var contentType = file.ContentType?.Trim() ?? string.Empty;
        ValidateContentType(contentType);

        var storageKey = BuildStorageKey(folder, extension);
        var physicalPath = GetSafePhysicalPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        await using (var stream = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream, ct);
        }

        return new FileSaveResult
        {
            StorageKey = storageKey,
            OriginalFileName = Path.GetFileName(file.FileName),
            ContentType = contentType,
            SizeBytes = file.Length
        };
    }

    public async Task<FileSaveResult> SaveBytesAsync(
        byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
    {
        if (data is null || data.Length == 0)
            throw new ArgumentException("Dữ liệu file không hợp lệ");

        extension = extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
        ValidateExtension(extension);
        ValidateContentType(contentType);

        var storageKey = BuildStorageKey(folder, extension);
        var physicalPath = GetSafePhysicalPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await File.WriteAllBytesAsync(physicalPath, data, ct);

        return new FileSaveResult
        {
            StorageKey = storageKey,
            OriginalFileName = Path.GetFileName(storageKey),
            ContentType = contentType,
            SizeBytes = data.Length
        };
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var physicalPath = GetSafePhysicalPath(storageKey);

        if (!File.Exists(physicalPath))
            throw new FileNotFoundException("Không tìm thấy file");

        Stream stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var physicalPath = GetSafePhysicalPath(storageKey);
            return Task.FromResult(File.Exists(physicalPath));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(false);
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var physicalPath = GetSafePhysicalPath(storageKey);
        if (File.Exists(physicalPath))
            File.Delete(physicalPath);
        return Task.CompletedTask;
    }

    private static string BuildStorageKey(string folder, string extension)
    {
        var safeFolder = SanitizeFolder(folder);
        var now = DateTime.UtcNow;
        return $"{safeFolder}/{now:yyyy}/{now:MM}/{now:dd}/{Guid.NewGuid():N}{extension}";
    }

    private static string SanitizeFolder(string folder)
    {
        var trimmed = (folder ?? "uploads").Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Contains(".."))
            throw new ArgumentException("Folder không hợp lệ");
        return trimmed.Replace('\\', '/');
    }

    private string GetSafePhysicalPath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ArgumentException("Storage key không hợp lệ");

        var normalized = storageKey.Replace('\\', '/');
        if (normalized.Contains("..") || Path.IsPathRooted(normalized))
            throw new ArgumentException("Storage key không hợp lệ");

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(_rootPath);

        if (!fullPath.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, rootFull, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Storage key không hợp lệ");

        return fullPath;
    }

    private void ValidateExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)
            || !_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Định dạng file '{extension}' không được phép");
    }

    private void ValidateContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)
            || !_options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Content-Type '{contentType}' không được phép");
    }
}
