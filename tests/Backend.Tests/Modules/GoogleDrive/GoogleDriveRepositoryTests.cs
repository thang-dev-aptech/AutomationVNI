using Backend.Data;
using Backend.Modules.GoogleDrive;
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

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => null;
        public string? GetCurrentUserName() => "test";
        public IReadOnlyList<string> GetCurrentUserRoles() => [];
    }
}
