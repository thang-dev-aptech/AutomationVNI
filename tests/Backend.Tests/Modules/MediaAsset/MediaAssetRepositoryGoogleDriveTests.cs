using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Shared.Storage;
using Backend.Tests.Modules.MediaFolder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.MediaAsset;

/// <summary>
/// GDRIVE-01 (t3) + GDRIVE-04: CreateFromGoogleDriveAsync gán Source/GoogleDriveFileId/OriginalFileName
/// đúng và FolderId = folder chuyên dụng (không còn null).
/// </summary>
public class MediaAssetRepositoryGoogleDriveTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly FakeFileStorageService _fileStorage;
    private readonly MediaAssetRepository _repo;

    public MediaAssetRepositoryGoogleDriveTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _fileStorage = new FakeFileStorageService();
        _repo = new MediaAssetRepository(_db, new TestUserContext(), _fileStorage);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateFromGoogleDriveAsync_SetsDedicatedFolderIdSourceAndOriginalFileName()
    {
        var dedicatedFolderId = Guid.NewGuid();
        var file = new GoogleDriveFileInfo
        {
            FileId = "drive-file-abc",
            Name = "Báo cáo quý.pdf",
            MimeType = "application/pdf",
            SizeBytes = 2048,
        };
        var data = new byte[] { 1, 2, 3, 4 };

        var entity = await _repo.CreateFromGoogleDriveAsync(data, file, dedicatedFolderId);

        Assert.Equal(dedicatedFolderId, entity.FolderId);
        Assert.Equal(MediaSource.GoogleDrive, entity.Source);
        Assert.Equal("drive-file-abc", entity.GoogleDriveFileId);
        Assert.Equal("Báo cáo quý.pdf", entity.OriginalFileName);
        // OriginalFileName KHÔNG được lấy từ storage key ngẫu nhiên do SaveBytesAsync sinh ra.
        Assert.NotEqual(_fileStorage.LastSaveResult!.OriginalFileName, entity.OriginalFileName);
        Assert.Equal(_fileStorage.LastSaveResult!.StorageKey, entity.StoragePath);
        Assert.Equal(".pdf", _fileStorage.LastExtension);
        Assert.Equal("google-drive", _fileStorage.LastFolder);
    }

    [Fact]
    public async Task ExistsByGoogleDriveFileIdAsync_TrueOnlyAfterImport()
    {
        Assert.False(await _repo.ExistsByGoogleDriveFileIdAsync("drive-file-xyz"));

        await _repo.CreateFromGoogleDriveAsync(
            [9, 9],
            new GoogleDriveFileInfo { FileId = "drive-file-xyz", Name = "x.txt", MimeType = "text/plain", SizeBytes = 2 },
            Guid.NewGuid());

        Assert.True(await _repo.ExistsByGoogleDriveFileIdAsync("drive-file-xyz"));
    }

    private sealed class FakeFileStorageService : IFileStorageService
    {
        public FileSaveResult? LastSaveResult { get; private set; }
        public string? LastFolder { get; private set; }
        public string? LastExtension { get; private set; }

        public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<FileSaveResult> SaveBytesAsync(
            byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
        {
            LastFolder = folder;
            LastExtension = extension;
            LastSaveResult = new FileSaveResult
            {
                StorageKey = $"{folder}/2026/09/28/{Guid.NewGuid():N}{extension}",
                OriginalFileName = $"{Guid.NewGuid():N}{extension}",
                ContentType = contentType,
                SizeBytes = data.Length,
            };
            return Task.FromResult(LastSaveResult);
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task DeleteAsync(string storageKey, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
