using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.MediaFolder;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Modules.GoogleDrive;

/// <summary>
/// GDRIVE-01 nền tảng (t1): kiểm chứng state CRUD của GoogleDriveRepository — chưa có
/// worker/client nên các test này chỉ phủ trực tiếp repository, không phủ toàn bộ contract
/// của requirement (poll/dedup/retry hành vi thuộc task khác).
/// </summary>
public class GoogleDriveRepositoryTests
{
    [Fact]
    public async Task GetSyncStateAsync_ReturnsSeededSingleton()
    {
        await using var fixture = await Fixture.CreateAsync();

        var state = await fixture.CreateRepository().GetSyncStateAsync();

        Assert.Equal(GoogleDriveSyncStateModel.SingletonId, state.Id);
        Assert.True(state.IsEnabled);
        Assert.Null(state.PageToken);
        Assert.Equal(0, state.LastImportedCount);
    }

    [Fact]
    public async Task UpdateSyncStateAsync_AdvancesPageTokenEvenWhenImportedCountIsZero()
    {
        await using var fixture = await Fixture.CreateAsync();
        var repository = fixture.CreateRepository();

        await repository.UpdateSyncStateAsync("token-1", 0);
        var afterEmpty = await repository.GetSyncStateAsync();

        Assert.Equal("token-1", afterEmpty.PageToken);
        Assert.Equal(0, afterEmpty.LastImportedCount);
        Assert.NotNull(afterEmpty.LastSyncAt);

        await repository.UpdateSyncStateAsync("token-2", 3);
        var afterImport = await repository.GetSyncStateAsync();

        Assert.Equal("token-2", afterImport.PageToken);
        Assert.Equal(3, afterImport.LastImportedCount);
    }

    [Fact]
    public async Task SetEnabledAsync_TogglesFlagAndRecordsUpdatedBy()
    {
        await using var fixture = await Fixture.CreateAsync();
        var repository = fixture.CreateRepository();

        await repository.SetEnabledAsync(false, "admin-user");
        var disabled = await repository.GetSyncStateAsync();
        Assert.False(disabled.IsEnabled);
        Assert.Equal("admin-user", disabled.UpdatedByUserName);

        await repository.SetEnabledAsync(true, "admin-user");
        var enabled = await repository.GetSyncStateAsync();
        Assert.True(enabled.IsEnabled);
    }

    [Fact]
    public async Task UpsertFailureAsync_CreatesThenIncrementsAttemptCount()
    {
        await using var fixture = await Fixture.CreateAsync();
        var repository = fixture.CreateRepository();

        var first = await repository.UpsertFailureAsync(
            "drive-file-1", "report.pdf", "application/pdf", 1024, "timeout");
        Assert.Equal(1, first.AttemptCount);
        Assert.Equal("timeout", first.LastError);

        var second = await repository.UpsertFailureAsync(
            "drive-file-1", "report.pdf", "application/pdf", 1024, "timeout again");
        Assert.Equal(2, second.AttemptCount);
        Assert.Equal("timeout again", second.LastError);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task GetRetryableFailuresAsync_ExcludesRowsAtOrAboveMaxAttempts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var repository = fixture.CreateRepository();

        for (var i = 0; i < 4; i++)
            await repository.UpsertFailureAsync("under-limit", "a.pdf", "application/pdf", 10, "err");
        for (var i = 0; i < 5; i++)
            await repository.UpsertFailureAsync("at-limit", "b.pdf", "application/pdf", 10, "err");

        var retryable = await repository.GetRetryableFailuresAsync(maxAttempts: 5, batchSize: 20);

        Assert.Contains(retryable, x => x.GoogleDriveFileId == "under-limit");
        Assert.DoesNotContain(retryable, x => x.GoogleDriveFileId == "at-limit");
    }

    [Fact]
    public async Task DeleteFailureAsync_RemovesRowAfterSuccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        var repository = fixture.CreateRepository();
        await repository.UpsertFailureAsync("drive-file-2", "x.pdf", "application/pdf", 10, "err");

        var deleted = await repository.DeleteFailureAsync("drive-file-2");
        var retryable = await repository.GetRetryableFailuresAsync(maxAttempts: 5, batchSize: 20);

        Assert.True(deleted);
        Assert.DoesNotContain(retryable, x => x.GoogleDriveFileId == "drive-file-2");
    }

    /// <summary>
    /// Fix bug e0cd97fc: nếu GoogleDriveSyncState.DedicatedFolderId hợp lệ (folder A) nhưng dòng
    /// ánh xạ root trong GoogleDriveKnownFolders còn trỏ MediaFolder B đã bị soft-delete (kịch
    /// bản tái hiện trên dev DB — B bị xoá sau khi A đã được tạo để thay thế), GetOrCreateDedicatedFolderAsync
    /// phải tự sửa lại dòng ánh xạ root về đúng A, KHÔNG được no-op chỉ vì MediaFolderId hiện tại
    /// không phải Guid.Empty.
    /// </summary>
    [Fact]
    public async Task GetOrCreateDedicatedFolderAsync_ResyncsRootMapping_WhenItPointsToSoftDeletedFolder()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string rootDriveFolderId = "root-drive-folder-id";

        var currentFolderId = await fixture.SeedMediaFolderAsync("Google Drive", parentFolderId: null, isDeleted: false);
        var staleFolderId = await fixture.SeedMediaFolderAsync("Google Drive (cũ)", parentFolderId: null, isDeleted: true);
        await fixture.SetDedicatedFolderIdAsync(currentFolderId);
        await fixture.SeedRootKnownFolderAsync(rootDriveFolderId, staleFolderId);

        // Dữ liệu đã lỡ "mồ côi" dưới folder cũ trước khi mapping được sửa — phải được cứu theo.
        var orphanChildId = await fixture.SeedMediaFolderAsync("Thư mục 3", parentFolderId: staleFolderId, isDeleted: false);
        var orphanAssetId = await fixture.SeedMediaAssetAsync(staleFolderId, "photo.png", "drive-file-orphan");

        var repository = fixture.CreateRepository();
        var resolvedId = await repository.GetOrCreateDedicatedFolderAsync(rootDriveFolderId);

        Assert.Equal(currentFolderId, resolvedId);

        var map = await repository.GetKnownFolderMapAsync();
        Assert.True(map.TryGetValue(rootDriveFolderId, out var rootMapping));
        Assert.Equal(currentFolderId, rootMapping!.MediaFolderId);

        var (orphanChildParent, orphanAssetFolder) = await fixture.ReadReparentedStateAsync(orphanChildId, orphanAssetId);
        Assert.Equal(currentFolderId, orphanChildParent);
        Assert.Equal(currentFolderId, orphanAssetFolder);
    }

    /// <summary>File/thư mục mới ở root Drive sau khi mapping đã được sửa phải resolve đúng
    /// dedicated folder hiện hành — không tái lập bug cho dữ liệu MỚI sau fix.</summary>
    [Fact]
    public async Task GetKnownFolderMapAsync_ResolvesRootToCurrentDedicatedFolder_AfterResync()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string rootDriveFolderId = "root-drive-folder-id";

        var currentFolderId = await fixture.SeedMediaFolderAsync("Google Drive", parentFolderId: null, isDeleted: false);
        var staleFolderId = await fixture.SeedMediaFolderAsync("Google Drive (cũ)", parentFolderId: null, isDeleted: true);
        await fixture.SetDedicatedFolderIdAsync(currentFolderId);
        await fixture.SeedRootKnownFolderAsync(rootDriveFolderId, staleFolderId);

        var repository = fixture.CreateRepository();
        await repository.GetOrCreateDedicatedFolderAsync(rootDriveFolderId);

        // Mô phỏng 1 file mới rơi thẳng vào root Drive sau khi mapping đã được sửa.
        var map = await repository.GetKnownFolderMapAsync();
        Assert.True(map.TryGetValue(rootDriveFolderId, out var rootMapping));
        Assert.Equal(currentFolderId, rootMapping!.MediaFolderId);
        Assert.NotEqual(staleFolderId, rootMapping.MediaFolderId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _dbOptions;

        private Fixture(SqliteConnection connection, DbContextOptions<AppDbContext> dbOptions)
        {
            _connection = connection;
            _dbOptions = dbOptions;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            await using (var db = new AppDbContext(dbOptions))
                await db.Database.EnsureCreatedAsync();

            return new Fixture(connection, dbOptions);
        }

        public GoogleDriveRepository CreateRepository() =>
            new(new AppDbContext(_dbOptions), new StubUserContext());

        public async Task<Guid> SeedMediaFolderAsync(string name, Guid? parentFolderId, bool isDeleted)
        {
            await using var db = new AppDbContext(_dbOptions);
            var folder = new MediaFolderModel
            {
                Id = Guid.NewGuid(),
                Name = name,
                SocialChannelId = null,
                ParentFolderId = parentFolderId,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = isDeleted,
                DeletedAt = isDeleted ? DateTime.UtcNow : null,
            };
            db.Set<MediaFolderModel>().Add(folder);
            await db.SaveChangesAsync();
            return folder.Id;
        }

        public async Task<Guid> SeedMediaAssetAsync(Guid folderId, string fileName, string googleDriveFileId)
        {
            await using var db = new AppDbContext(_dbOptions);
            var asset = new MediaAssetModel
            {
                Id = Guid.NewGuid(),
                FileName = fileName,
                OriginalFileName = fileName,
                StoragePath = $"media/{fileName}",
                MimeType = "image/png",
                FileSize = 1,
                Source = MediaSource.GoogleDrive,
                FolderId = folderId,
                GoogleDriveFileId = googleDriveFileId,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false,
            };
            db.Set<MediaAssetModel>().Add(asset);
            await db.SaveChangesAsync();
            return asset.Id;
        }

        public async Task SetDedicatedFolderIdAsync(Guid dedicatedFolderId)
        {
            await using var db = new AppDbContext(_dbOptions);
            var state = await db.Set<GoogleDriveSyncStateModel>()
                .SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId);
            state.DedicatedFolderId = dedicatedFolderId;
            await db.SaveChangesAsync();
        }

        public async Task SeedRootKnownFolderAsync(string rootDriveFolderId, Guid mediaFolderId)
        {
            await using var db = new AppDbContext(_dbOptions);
            db.Set<GoogleDriveKnownFolderModel>().Add(new GoogleDriveKnownFolderModel
            {
                Id = Guid.NewGuid(),
                FolderId = rootDriveFolderId,
                MediaFolderId = mediaFolderId,
                DriveParentId = null,
                Name = GoogleDriveRepository.DedicatedFolderName,
                DiscoveredAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false,
            });
            await db.SaveChangesAsync();
        }

        public async Task<(Guid? ChildParentFolderId, Guid? AssetFolderId)> ReadReparentedStateAsync(
            Guid childFolderId, Guid assetId)
        {
            await using var db = new AppDbContext(_dbOptions);
            var child = await db.Set<MediaFolderModel>().SingleAsync(x => x.Id == childFolderId);
            var asset = await db.Set<MediaAssetModel>().SingleAsync(x => x.Id == assetId);
            return (child.ParentFolderId, asset.FolderId);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => null;
        public string? GetCurrentUserName() => "test";
        public IReadOnlyList<string> GetCurrentUserRoles() => [];
    }
}
