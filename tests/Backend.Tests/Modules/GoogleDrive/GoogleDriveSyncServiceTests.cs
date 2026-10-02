using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.MediaFolder;
using Backend.Shared;
using Backend.Shared.Repositories;
using Backend.Shared.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.GoogleDrive;

/// <summary>
/// GDRIVE-01 (t3, t7) hành vi chuyển từ GoogleDriveImportWorker sang GoogleDriveSyncService
/// (GDRIVE-03, refactor thuần tuý — không đổi hành vi): gdrive-toggle-test, gdrive-cursor-dedup-test,
/// gdrive-zip-rejected-test, gdrive-bounded-retry-test, gdrive-no-file-loss-when-page-exceeds-cap.
/// GDRIVE-03 (t1) mới: gdrive03-scan-now-disabled-test (phần service), gdrive03-no-race-test.
/// GDRIVE-02 (t4): child/grandchild same-tick, unrelated/trashed excluded, no regression root files.
/// </summary>
public class GoogleDriveSyncServiceTests
{
    [Fact]
    public async Task RunTickAsync_SkipsAllPhasesWhenDisabled_ThenResumesWithoutRestart()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.SetEnabledAsync(false);
        await fixture.CreateService().RunTickAsync();
        await fixture.CreateService().RunTickAsync();

        Assert.Equal(0, fixture.Client.ListChangesCalls);
        Assert.Empty(fixture.Client.DownloadedFileIds);

        await fixture.SetEnabledAsync(true);
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "after-enable" });
        await fixture.CreateService().RunTickAsync();

        Assert.True(fixture.Client.ListChangesCalls > 0);
        Assert.Equal("after-enable", (await fixture.GetStateAsync()).PageToken);
    }

    [Fact]
    public async Task RunTickAsync_ReturnsDisabledResult_AndMakesNoClientCallsWhenStateDisabled()
    {
        // AC gdrive03-scan-now-disabled-test (84a92bd6) — phần GoogleDriveSyncService.
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(false);

        var result = await fixture.CreateService().RunTickAsync();

        Assert.False(result.Enabled);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, fixture.Client.ListChangesCalls);
        Assert.Empty(fixture.Client.DownloadedFileIds);
    }

    [Fact]
    public async Task RunTickAsync_ConcurrentCalls_DoNotInterleave_AndLeaveConsistentState()
    {
        // AC gdrive03-no-race-test (e52829ee) — worker định kỳ và scan-now thủ công dùng chung
        // GoogleDriveSyncService.Lock (static): hai lời gọi RunTickAsync gần như đồng thời, dù
        // trên 2 instance khác nhau (mô phỏng 1 từ worker, 1 từ request scan-now), không được
        // chồng lấn vào đoạn tới hạn (ListChangesAsync), và trạng thái cuối cùng phải nhất quán
        // (khớp với lượt chạy sau, không mất cập nhật giữa chừng).
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);
        fixture.Client.CriticalSectionDelayMs = 80;
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "race-token-1" });
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "race-token-2" });

        var serviceForWorker = fixture.CreateService();
        var serviceForScanNow = fixture.CreateService();

        var taskA = serviceForWorker.RunTickAsync();
        var taskB = serviceForScanNow.RunTickAsync();
        await Task.WhenAll(taskA, taskB);

        Assert.True(fixture.Client.MaxConcurrentCriticalSections <= 1);

        var finalState = await fixture.GetStateAsync();
        Assert.Contains(finalState.PageToken, new[] { "race-token-1", "race-token-2" });
    }

    [Fact]
    public async Task RunTickAsync_FirstScanImportsExistingTree_ThenLaterScanUsesChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        fixture.Client.StartToken = "after-snapshot";
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "old-1",
                    Name = "old.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [Fixture.RootFolderId]
                }
            ]
        };
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "delta-should-not-run",
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "delta-ignored",
                    Name = "delta.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [Fixture.RootFolderId]
                }
            ]
        });

        var first = await fixture.CreateService().RunTickAsync();

        Assert.Equal(1, first.ImportedCount);
        Assert.Contains("old-1", fixture.Client.DownloadedFileIds);
        Assert.DoesNotContain("delta-ignored", fixture.Client.DownloadedFileIds);
        Assert.Equal(0, fixture.Client.ListChangesCalls);
        var afterFirst = await fixture.GetStateAsync();
        Assert.Equal("after-snapshot", afterFirst.PageToken);
        Assert.NotNull(afterFirst.InitialSnapshotCompletedAt);

        while (fixture.Client.Pages.Count > 0)
            fixture.Client.Pages.Dequeue();
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "delta-2",
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "new-1",
                    Name = "new.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [Fixture.RootFolderId]
                }
            ]
        });
        var second = await fixture.CreateService().RunTickAsync();

        Assert.Equal(1, second.ImportedCount);
        Assert.Contains("new-1", fixture.Client.DownloadedFileIds);
        Assert.Equal(1, fixture.Client.ListChangesCalls);
        Assert.Equal("delta-2", (await fixture.GetStateAsync()).PageToken);
    }

    [Fact]
    public async Task RunTickAsync_ExistingCursorWithoutSnapshot_BackfillsTreeAndContinuesChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        await fixture.SetPageTokenAsync("legacy-cursor");
        fixture.Client.StartToken = "must-not-replace-legacy";
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "preexisting-1",
                    Name = "old.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [Fixture.RootFolderId]
                }
            ]
        };
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "legacy-next",
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "delta-1",
                    Name = "delta.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [Fixture.RootFolderId]
                }
            ]
        });

        var snapshot = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, snapshot.ImportedCount);
        Assert.Contains("preexisting-1", fixture.Client.DownloadedFileIds);
        Assert.DoesNotContain("delta-1", fixture.Client.DownloadedFileIds);
        var afterSnapshot = await fixture.GetStateAsync();
        Assert.Equal("legacy-cursor", afterSnapshot.PageToken);
        Assert.NotNull(afterSnapshot.InitialSnapshotCompletedAt);

        var delta = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, delta.ImportedCount);
        Assert.Contains("delta-1", fixture.Client.DownloadedFileIds);
        var state = await fixture.GetStateAsync();
        Assert.Equal("legacy-next", state.PageToken);
        Assert.NotEqual("must-not-replace-legacy", state.PageToken);
        Assert.Equal(1, state.LastImportedCount);

        var treeCallsAfterBackfill = fixture.Client.ListFolderTreeCalls;
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "legacy-after", Files = [] });
        await fixture.CreateService().RunTickAsync();
        Assert.Equal(treeCallsAfterBackfill, fixture.Client.ListFolderTreeCalls);
    }

    [Fact]
    public async Task RunTickAsync_SuccessfulRetryIsIncludedInImportedCount()
    {
        await using var fixture = await Fixture.CreateAsync(maxRetryAttempts: 5);
        await fixture.SetEnabledAsync(true);
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "seed-token" });
        await fixture.CreateService().RunTickAsync();

        const string fileId = "retry-count";
        fixture.Client.FailDownload(fileId, times: 1);
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "after-fail",
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = fileId,
                    Name = "retry.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [Fixture.RootFolderId]
                }
            ]
        });
        var failed = await fixture.CreateService().RunTickAsync();
        Assert.Equal(0, failed.ImportedCount);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "after-retry", Files = [] });
        var retried = await fixture.CreateService().RunTickAsync();

        Assert.Equal(1, retried.ImportedCount);
        Assert.Contains(fileId, fixture.Client.DownloadedFileIds);
        Assert.Null(await fixture.GetFailureAsync(fileId));
    }

    /// <summary>
    /// B1: file Drive trùng asset đã xoá mềm không được nhập lại (unique index tính cả dòng đã xoá).
    /// Tick không ném, file mới và delta vẫn được nhập, snapshot xong, cursor tiến.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_SoftDeletedDriveAsset_IsNotReimported_AndDoesNotAbortTick()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        await fixture.SetPageTokenAsync("legacy-cursor");
        await fixture.SeedSoftDeletedGoogleDriveAssetAsync("deleted-1", "gone.jpg");
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Files =
            [
                File("deleted-1", "gone.jpg"),
                File("fresh-1", "fresh.jpg"),
            ]
        };
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "legacy-next",
            Files = [File("delta-1", "delta.jpg")]
        });

        var snapshot = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, snapshot.ImportedCount);
        Assert.DoesNotContain("deleted-1", fixture.Client.DownloadedFileIds);
        Assert.Contains("fresh-1", fixture.Client.DownloadedFileIds);
        Assert.DoesNotContain("delta-1", fixture.Client.DownloadedFileIds);
        Assert.Equal("legacy-cursor", (await fixture.GetStateAsync()).PageToken);

        var delta = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, delta.ImportedCount);
        Assert.Contains("delta-1", fixture.Client.DownloadedFileIds);
        Assert.Equal(1, await fixture.CountRowsByGoogleDriveFileIdAsync("deleted-1"));
        var deleted = await fixture.GetMediaAssetByGoogleDriveFileIdRawAsync("deleted-1");
        Assert.NotNull(deleted);
        Assert.True(deleted!.IsDeleted);
        var state = await fixture.GetStateAsync();
        Assert.NotNull(state.InitialSnapshotCompletedAt);
        Assert.Equal("legacy-next", state.PageToken);
    }

    /// <summary>
    /// B1: lỗi lưu một file (entity lỗi vẫn được track) không được hủy tick. Failure row được ghi, file khác vẫn nhập.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_SaveFailureOnOneFile_DoesNotAbortTick()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        await fixture.InstallUniquePoisonTriggerAsync();
        fixture.Client.StartToken = "after-poison";
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Files =
            [
                File("poison-1", "poison.jpg"),
                File("good-1", "good.jpg"),
            ]
        };

        var result = await fixture.CreateService().RunTickAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Contains("good-1", fixture.Client.DownloadedFileIds);
        Assert.NotNull(await fixture.GetFailureAsync("poison-1"));
        Assert.Equal(0, await fixture.CountRowsByGoogleDriveFileIdAsync("poison-1"));
        var state = await fixture.GetStateAsync();
        Assert.NotNull(state.InitialSnapshotCompletedAt);
        Assert.Equal("after-poison", state.PageToken);
    }

    /// <summary>
    /// B2: startPageToken lấy TRƯỚC khi liệt kê cây. File tải lên sau lúc liệt kê phải xuất hiện ở tick sau qua changes.list.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_FileUploadedDuringSnapshot_IsImportedOnNextChangesTick()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        fixture.Client.StartToken = "after-snapshot";
        fixture.Client.LateFile = File("late-1", "late.jpg");
        fixture.Client.FolderTree = new GoogleDriveFolderTree { Files = [File("early-1", "early.jpg")] };

        var first = await fixture.CreateService().RunTickAsync();

        Assert.Equal(1, first.ImportedCount);
        Assert.Contains("early-1", fixture.Client.DownloadedFileIds);
        Assert.DoesNotContain("late-1", fixture.Client.DownloadedFileIds);
        var journal = fixture.Client.CallJournal;
        Assert.True(journal.IndexOf("GetStartPageToken") < journal.IndexOf("ListFolderTree"),
            string.Join(",", journal));

        var second = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, second.ImportedCount);
        Assert.Contains("late-1", fixture.Client.DownloadedFileIds);
    }

    /// <summary>
    /// B3: lỗi tạm của lần liệt kê cây đầu không được đánh dấu snapshot xong khi file trong thư mục con bị bỏ.
    /// Tick sau nhập đúng vào thư mục con.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_TransientTreeFailure_DoesNotCompleteSnapshotUntilChildFileImports()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        fixture.Client.StartToken = "after-child";
        fixture.Client.ListFolderTreeFailures = 1;
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Folders =
            [
                new GoogleDriveFolderInfo
                {
                    FolderId = "child-1",
                    Name = "Album",
                    ParentIds = [Fixture.RootFolderId]
                }
            ],
            Files = [File("nested-1", "nested.jpg", "child-1")]
        };

        var failed = await fixture.CreateService().RunTickAsync();
        Assert.Equal(0, failed.ImportedCount);
        Assert.Null((await fixture.GetStateAsync()).InitialSnapshotCompletedAt);
        Assert.Null(await fixture.GetMediaAssetByGoogleDriveFileIdRawAsync("nested-1"));

        var recovered = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, recovered.ImportedCount);
        var asset = await fixture.GetMediaAssetByGoogleDriveFileIdRawAsync("nested-1");
        Assert.NotNull(asset);
        Assert.False(asset!.IsDeleted);
        var folder = await fixture.GetMediaFolderAsync(asset.FolderId!.Value);
        Assert.Equal("Album", folder!.Name);
        Assert.NotNull((await fixture.GetStateAsync()).InitialSnapshotCompletedAt);
    }

    /// <summary>
    /// N1: snapshot tôn trọng MaxFilesPerTick. Cây 3×trần cần đúng 3 tick, mỗi tick nhập ≤ trần,
    /// snapshot chỉ xong ở tick cuối. Quét ngay trong lúc một đợt đang chạy không vào được critical section.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_SnapshotLargerThanMaxFilesPerTick_ImportsOneBatchPerTick()
    {
        const int maxFiles = 2;
        await using var fixture = await Fixture.CreateAsync(maxFilesPerTick: maxFiles);
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        fixture.Client.StartToken = "snapshot-cursor";
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Files = Enumerable.Range(0, maxFiles * 3)
                .Select(i => File($"snap-{i}", $"snap-{i}.jpg"))
                .ToList()
        };

        var batchDownloads = new List<int>();
        for (var tick = 0; tick < 3; tick++)
        {
            fixture.Client.ArmSingleHold();
            var before = fixture.Client.DownloadedFileIds.Count;
            var running = fixture.CreateService().RunTickAsync();
            using var entered = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await fixture.Client.CriticalSectionEntered.Task.WaitAsync(entered.Token);
            using var blocked = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var scan = fixture.CreateService().RunScanNowAsync(blocked.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
            Assert.True(fixture.Client.MaxConcurrentCriticalSections <= 1);
            fixture.Client.ReleaseCriticalSection();
            var result = await running;
            var importedThisTick = fixture.Client.DownloadedFileIds.Count - before;
            batchDownloads.Add(importedThisTick);
            Assert.InRange(importedThisTick, 1, maxFiles);
            Assert.True(result.ImportedCount <= maxFiles);
            if (tick < 2)
                Assert.Null((await fixture.GetStateAsync()).InitialSnapshotCompletedAt);
        }

        Assert.Equal(maxFiles, batchDownloads[0]);
        Assert.Equal(maxFiles, batchDownloads[1]);
        Assert.Equal(maxFiles, batchDownloads[2]);
        var state = await fixture.GetStateAsync();
        Assert.NotNull(state.InitialSnapshotCompletedAt);
        Assert.Equal("snapshot-cursor", state.PageToken);
        Assert.Equal(maxFiles * 3, await fixture.CountMediaAssetsAsync());
        Assert.Equal(maxFiles * 3, fixture.Client.DownloadedFileIds.Distinct().Count());

        var treeCalls = fixture.Client.ListFolderTreeCalls;
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "after-snapshot", Files = [] });
        await fixture.CreateService().RunTickAsync();
        Assert.Equal(treeCalls, fixture.Client.ListFolderTreeCalls);
        Assert.Equal(maxFiles * 3, await fixture.CountMediaAssetsAsync());
    }

    /// <summary>
    /// N2: file snapshot hết MaxRetryAttempts không được tick thường nhập lại. Dòng failure còn LastError.
    /// Quét ngay không thử lại file đã hết lượt.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_ExhaustedFailure_StaysVisible_AndScanNowDoesNotRetry()
    {
        await using var fixture = await Fixture.CreateAsync(maxRetryAttempts: 2, maxFilesPerTick: 20);
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        fixture.Client.StartToken = "after-exhausted";
        fixture.Client.FailDownload("stuck-1", times: 2);
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Files = [File("stuck-1", "stuck.jpg"), File("ok-1", "ok.jpg")]
        };

        var first = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, first.ImportedCount);
        var afterFirst = await fixture.GetFailureAsync("stuck-1");
        Assert.NotNull(afterFirst);
        Assert.Equal(1, afterFirst!.AttemptCount);
        Assert.False(string.IsNullOrWhiteSpace(afterFirst.LastError));
        Assert.NotNull((await fixture.GetStateAsync()).InitialSnapshotCompletedAt);

        var second = await fixture.CreateService().RunTickAsync();
        Assert.Equal(0, second.ImportedCount);
        var exhausted = await fixture.GetFailureAsync("stuck-1");
        Assert.NotNull(exhausted);
        Assert.Equal(2, exhausted!.AttemptCount);
        Assert.NotNull((await fixture.GetStateAsync()).InitialSnapshotCompletedAt);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "delta-after", Files = [] });
        var ignored = await fixture.CreateService().RunTickAsync();
        Assert.Equal(0, ignored.ImportedCount);
        Assert.NotNull(await fixture.GetFailureAsync("stuck-1"));
        Assert.Equal(2, fixture.Client.DownloadedFileIds.Count(id => id == "stuck-1"));

        var visible = await fixture.GetExhaustedFailuresAsync();
        Assert.Contains(visible, x => x.GoogleDriveFileId == "stuck-1" && x.LastError != null);

        var beforeScan = fixture.Client.DownloadedFileIds.Count(id => id == "stuck-1");
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "scan-now", Files = [] });
        var scanned = await fixture.CreateService().RunScanNowAsync();
        Assert.Equal(0, scanned.ImportedCount);
        Assert.NotNull(await fixture.GetFailureAsync("stuck-1"));
        Assert.Equal(2, (await fixture.GetFailureAsync("stuck-1"))!.AttemptCount);
        Assert.Equal(beforeScan, fixture.Client.DownloadedFileIds.Count(id => id == "stuck-1"));
    }

    /// <summary>F1a: lỗi lưu kéo dài không hủy tick. AttemptCount tăng tới max mặc định rồi dừng.</summary>
    [Fact]
    public async Task RunTickAsync_PersistentSaveFailure_DoesNotAbortTick_AndStopsAtMaxAttempts()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        await fixture.InstallUniquePoisonTriggerAsync();
        fixture.Client.StartToken = "after-poison-loop";
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Files = [File("poison-1", "poison.jpg"), File("good-1", "good.jpg")]
        };
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "delta-1",
            Files = [File("delta-1", "delta.jpg")]
        });

        for (var tick = 1; tick <= 5; tick++)
        {
            await fixture.CreateService().RunTickAsync();
            Assert.Equal(tick, (await fixture.GetFailureAsync("poison-1"))!.AttemptCount);
        }

        Assert.Contains("good-1", fixture.Client.DownloadedFileIds);
        Assert.Contains("delta-1", fixture.Client.DownloadedFileIds);
        Assert.NotNull((await fixture.GetStateAsync()).InitialSnapshotCompletedAt);
        Assert.Equal(5, fixture.Client.DownloadedFileIds.Count(id => id == "poison-1"));

        await fixture.CreateService().RunTickAsync();
        Assert.Equal(5, (await fixture.GetFailureAsync("poison-1"))!.AttemptCount);
        Assert.Equal(5, fixture.Client.DownloadedFileIds.Count(id => id == "poison-1"));
    }

    /// <summary>F1b: delta lỗi rồi nhập lại thành công thì xoá failure row, tick sau không ném.</summary>
    [Fact]
    public async Task RunTickAsync_DeltaFailureThenSuccess_DeletesFailureAndLaterTickDoesNotThrow()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);
        fixture.Client.FailDownload("flaky-delta", times: 2);
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "fail-delta",
            Files = [File("flaky-delta", "flaky.jpg")]
        });

        var failed = await fixture.CreateService().RunTickAsync();
        Assert.Equal(0, failed.ImportedCount);
        Assert.NotNull(await fixture.GetFailureAsync("flaky-delta"));

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "ok-delta",
            Files = [File("flaky-delta", "flaky.jpg")]
        });
        var recovered = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, recovered.ImportedCount);
        Assert.Null(await fixture.GetFailureAsync("flaky-delta"));

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "after-delta", Files = [] });
        var next = await fixture.CreateService().RunTickAsync();
        Assert.Equal(0, next.ImportedCount);
        Assert.Null(await fixture.GetFailureAsync("flaky-delta"));
    }

    /// <summary>F2: mỗi tick một lần thử. Thành công thì xoá failure, tick sau không ném.</summary>
    [Fact]
    public async Task RunTickAsync_SnapshotFailureThenSuccess_RetriesOncePerTickAndDeletesFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        fixture.Client.StartToken = "after-flaky";
        fixture.Client.FailDownload("flaky-1", times: 2);
        fixture.Client.FolderTree = new GoogleDriveFolderTree { Files = [File("flaky-1", "flaky.jpg")] };

        for (var tick = 1; tick <= 2; tick++)
        {
            var before = fixture.Client.DownloadedFileIds.Count;
            var result = await fixture.CreateService().RunTickAsync();
            Assert.Equal(0, result.ImportedCount);
            Assert.Equal(before + 1, fixture.Client.DownloadedFileIds.Count);
            Assert.Equal(tick, (await fixture.GetFailureAsync("flaky-1"))!.AttemptCount);
        }

        var recovered = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, recovered.ImportedCount);
        Assert.Null(await fixture.GetFailureAsync("flaky-1"));
        Assert.Equal(3, fixture.Client.DownloadedFileIds.Count(id => id == "flaky-1"));

        var next = await fixture.CreateService().RunTickAsync();
        Assert.Equal(0, next.ImportedCount);
        Assert.Equal(3, fixture.Client.DownloadedFileIds.Count);
    }

    /// <summary>F3: oversize không chiếm slot sau khi đã ghi failure. File nhỏ vào, snapshot xong, changes chạy.</summary>
    [Fact]
    public async Task RunTickAsync_OversizedFiles_DoNotBlockSmallerFileOrChanges()
    {
        await using var fixture = await Fixture.CreateAsync(maxFilesPerTick: 2);
        await fixture.SetEnabledAsync(true, completeInitialSnapshot: false);
        fixture.Client.StartToken = "after-size";
        await fixture.SeedFailureAttemptsAsync("big-1", 5);
        await fixture.SeedFailureAttemptsAsync("big-2", 5);
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Files =
            [
                File("big-1", "big-1.jpg", sizeBytes: 9_000_000),
                File("big-2", "big-2.jpg", sizeBytes: 9_000_000),
                File("small-1", "small.jpg"),
            ]
        };

        var completed = false;
        for (var i = 0; i < 4 && !completed; i++)
        {
            await fixture.CreateService().RunTickAsync();
            completed = (await fixture.GetStateAsync()).InitialSnapshotCompletedAt.HasValue;
        }

        Assert.True(completed);
        Assert.Contains("small-1", fixture.Client.DownloadedFileIds);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "delta-size",
            Files = [File("delta-size", "delta.jpg")]
        });
        var delta = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, delta.ImportedCount);
        Assert.True(fixture.Client.ListChangesCalls > 0);
    }

    /// <summary>N2-a: file hết lượt nhập được qua delta thì không còn trong danh sách hết lượt, Quét ngay không ném.</summary>
    [Fact]
    public async Task RunTickAsync_ExhaustedFileImportedByDelta_ScanNowDoesNotThrow()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);
        await fixture.SeedFailureAttemptsAsync("late-ok", 5);
        Assert.Contains(await fixture.GetExhaustedFailuresAsync(), x => x.GoogleDriveFileId == "late-ok");

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "imported",
            Files = [File("late-ok", "late-ok.jpg")]
        });
        var imported = await fixture.CreateService().RunTickAsync();
        Assert.Equal(1, imported.ImportedCount);
        Assert.Null(await fixture.GetFailureAsync("late-ok"));

        var scanned = await fixture.CreateService().RunScanNowAsync();
        Assert.Equal(0, scanned.ImportedCount);
        Assert.DoesNotContain(await fixture.GetExhaustedFailuresAsync(), x => x.GoogleDriveFileId == "late-ok");
    }

    /// <summary>Quét ngay không tải và không tăng attempt của file đã hết lượt.</summary>
    [Fact]
    public async Task RunScanNowAsync_DoesNotDownloadOrIncrementExhaustedFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);
        await fixture.SeedFailureAttemptsAsync("done-1", 5);
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "scan", Files = [] });

        var scanned = await fixture.CreateService().RunScanNowAsync();

        Assert.Equal(0, scanned.ImportedCount);
        Assert.Equal(5, (await fixture.GetFailureAsync("done-1"))!.AttemptCount);
        Assert.DoesNotContain("done-1", fixture.Client.DownloadedFileIds);
    }

    private static GoogleDriveFileInfo File(
        string id, string name, string? parent = null, long sizeBytes = 10) => new()
    {
        FileId = id,
        Name = name,
        MimeType = "image/jpeg",
        SizeBytes = sizeBytes,
        Parents = [parent ?? Fixture.RootFolderId]
    };

    [Fact]
    public async Task RunTickAsync_AdvancesPageTokenOnEmptyPage_AndSkipsAlreadyImportedFile()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        // Seed: file đã import từ trước.
        var existing = await fixture.CreateExistingAssetAsync("existing-1");

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "token-2",
            Files =
            [
                new GoogleDriveFileInfo { FileId = "existing-1", Name = "old.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [Fixture.RootFolderId] },
                new GoogleDriveFileInfo { FileId = "new-1", Name = "new.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [Fixture.RootFolderId] },
            ],
        });

        await fixture.CreateService().RunTickAsync();

        Assert.DoesNotContain("existing-1", fixture.Client.DownloadedFileIds);
        Assert.Contains("new-1", fixture.Client.DownloadedFileIds);
        Assert.Equal("token-2", (await fixture.GetStateAsync()).PageToken);
        // 1 asset đã seed sẵn (existing-1) + 1 asset mới nhập (new-1) = 2. existing-1 KHÔNG được
        // tải/nhập lại — chỉ new-1 mới tạo thêm MediaAsset.
        Assert.Equal(2, await fixture.CountMediaAssetsAsync());

        // Lượt kế tiếp: danh sách rỗng — PageToken vẫn phải advance.
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "token-3", Files = [] });
        await fixture.CreateService().RunTickAsync();

        Assert.Equal("token-3", (await fixture.GetStateAsync()).PageToken);
        // Không có file google-apps/mới nào lọt qua — tổng số MediaAsset không đổi.
        Assert.Equal(2, await fixture.CountMediaAssetsAsync());
        Assert.NotEqual(Guid.Empty, existing.Id);
    }

    [Fact]
    public async Task RunTickAsync_RejectsZipAtExtensionValidation_NoAssetNoFailureRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "token-2",
            Files = [new GoogleDriveFileInfo { FileId = "zip-1", Name = "malware.zip", MimeType = "application/zip", SizeBytes = 10, Parents = [Fixture.RootFolderId] }],
        });

        await fixture.CreateService().RunTickAsync();

        Assert.DoesNotContain("zip-1", fixture.Client.DownloadedFileIds);
        Assert.Equal(0, await fixture.CountMediaAssetsAsync());
        Assert.Empty(await fixture.GetRetryableFailuresAsync());
    }

    [Fact]
    public async Task RunTickAsync_BoundedRetry_StopsAppearingAfterMaxAttempts()
    {
        await using var fixture = await Fixture.CreateAsync(maxRetryAttempts: 3);
        await fixture.SetEnabledAsync(true);

        const string fileId = "flaky-1";
        fixture.Client.FailDownload(fileId, times: 10);

        // Tick 1: file "mới" xuất hiện qua ListChangesAsync, tải lỗi -> AttemptCount=1.
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "t2",
            Files = [new GoogleDriveFileInfo { FileId = fileId, Name = "flaky.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [Fixture.RootFolderId] }],
        });
        await fixture.CreateService().RunTickAsync();
        var failures = await fixture.GetRetryableFailuresAsync();
        Assert.Single(failures);
        Assert.Equal(1, failures[0].AttemptCount);

        // Tick 2 & 3: không còn "mới" (đã tiêu thụ), pha retry thử lại và vẫn lỗi -> AttemptCount 2 rồi 3.
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "t3", Files = [] });
        await fixture.CreateService().RunTickAsync();
        Assert.Equal(2, (await fixture.GetRetryableFailuresAsync()).Single().AttemptCount);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "t4", Files = [] });
        await fixture.CreateService().RunTickAsync();
        Assert.Equal(3, (await fixture.GetFailureAsync(fileId))!.AttemptCount);

        // Đạt MaxRetryAttempts=3: không còn được chọn để thử lại nữa, nhưng dòng vẫn còn trong DB.
        Assert.Empty(await fixture.GetRetryableFailuresAsync());
        Assert.NotNull(await fixture.GetFailureAsync(fileId));
    }

    [Fact]
    public async Task RunTickAsync_BoundedRetry_SuccessMidwayDeletesFailureAndCreatesAsset()
    {
        await using var fixture = await Fixture.CreateAsync(maxRetryAttempts: 5);
        await fixture.SetEnabledAsync(true);

        const string fileId = "flaky-2";
        fixture.Client.FailDownload(fileId, times: 2); // lỗi 2 lần đầu, lần thứ 3 thành công.

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "t2",
            Files = [new GoogleDriveFileInfo { FileId = fileId, Name = "flaky2.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [Fixture.RootFolderId] }],
        });
        await fixture.CreateService().RunTickAsync(); // AttemptCount=1
        Assert.Equal(1, (await fixture.GetFailureAsync(fileId))!.AttemptCount);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "t3", Files = [] });
        await fixture.CreateService().RunTickAsync(); // AttemptCount=2
        Assert.Equal(2, (await fixture.GetFailureAsync(fileId))!.AttemptCount);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "t4", Files = [] });
        await fixture.CreateService().RunTickAsync(); // thành công -> xoá dòng + tạo MediaAsset

        Assert.Null(await fixture.GetFailureAsync(fileId));
        Assert.Equal(1, await fixture.CountMediaAssetsAsync());
    }

    /// <summary>
    /// AC gdrive-no-file-loss-when-page-exceeds-cap (9382d03b, t7): trước bản sửa t7, worker tự
    /// Take(MaxFilesPerTick) SAU KHI đã nhận nguyên trang từ ListChangesAsync, rồi vẫn advance
    /// PageToken theo trang ĐẦY ĐỦ — phần bị cắt bỏ mất vĩnh viễn. Giờ việc giới hạn xảy ra ở
    /// tầng gọi API (maxResults truyền vào ListChangesAsync), nên PageToken luôn khớp đúng với
    /// phần đã xử lý — backlog nhiều hơn 1 trang phải được xử lý HẾT qua nhiều tick, không rơi
    /// file nào.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_BacklogLargerThanMaxFilesPerTick_ProcessesEveryFileAcrossTicks()
    {
        await using var fixture = await Fixture.CreateAsync(maxFilesPerTick: 20);
        await fixture.SetEnabledAsync(true);

        const int backlogSize = 45; // > 2x MaxFilesPerTick (20) để buộc phải trải qua >= 3 tick.
        var expectedFileIds = new List<string>();
        for (var i = 0; i < backlogSize; i++)
        {
            var fileId = $"backlog-{i}";
            expectedFileIds.Add(fileId);
            fixture.Client.Backlog.Enqueue(new GoogleDriveFileInfo
            {
                FileId = fileId,
                Name = $"backlog-{i}.jpg",
                MimeType = "image/jpeg",
                SizeBytes = 10,
                Parents = [Fixture.RootFolderId],
            });
        }

        // 3 tick là đủ để rút cạn 45 file ở mức trần 20 file/tick (20 + 20 + 5); tick thứ 4 xác
        // nhận trạng thái ổn định — không có gì mất, không có gì lặp lại.
        await fixture.CreateService().RunTickAsync();
        await fixture.CreateService().RunTickAsync();
        await fixture.CreateService().RunTickAsync();
        await fixture.CreateService().RunTickAsync();

        Assert.Equal(backlogSize, await fixture.CountMediaAssetsAsync());
        Assert.Equal(expectedFileIds.OrderBy(x => x), fixture.Client.DownloadedFileIds.OrderBy(x => x));
        // Mỗi lượt gọi Drive phải xin đúng maxResults = MaxFilesPerTick — việc giới hạn xảy ra ở
        // tầng gọi API, không phải Take() sau khi nhận về nguyên trang.
        Assert.All(fixture.Client.MaxResultsSeen, n => Assert.Equal(20, n));
        Assert.Empty(await fixture.GetRetryableFailuresAsync());
    }

    // ── GDRIVE-02: thư mục con đệ quy ───────────────────────────────────────

    /// <summary>AC gdrive02-child-and-file-same-tick-test (44b07276).</summary>
    [Fact]
    public async Task RunTickAsync_ChildFolderAndFileSameTick_ImportsFileImmediately()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        const string childId = "child-folder-1";
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive02-child",
            Folders =
            [
                new GoogleDriveFolderInfo
                {
                    FolderId = childId,
                    Name = "Child",
                    ParentIds = [Fixture.RootFolderId],
                },
            ],
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "child-file-1",
                    Name = "in-child.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [childId],
                },
            ],
        });

        await fixture.CreateService().RunTickAsync();

        Assert.Contains(childId, await fixture.GetKnownFolderIdsAsync());
        Assert.Contains("child-file-1", fixture.Client.DownloadedFileIds);
        Assert.Equal(1, await fixture.CountMediaAssetsAsync());
    }

    /// <summary>AC gdrive02-grandchild-same-tick-test (5cd2cd23).</summary>
    [Fact]
    public async Task RunTickAsync_GrandchildFolderAndFileSameTick_ImportsFileImmediately()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        const string childId = "child-folder-2";
        const string grandchildId = "grandchild-folder-1";
        // Thứ tự cố ý: cháu trước cha trong cùng page — multi-pass phải hấp thụ cả hai.
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive02-grandchild",
            Folders =
            [
                new GoogleDriveFolderInfo
                {
                    FolderId = grandchildId,
                    Name = "Grandchild",
                    ParentIds = [childId],
                },
                new GoogleDriveFolderInfo
                {
                    FolderId = childId,
                    Name = "Child",
                    ParentIds = [Fixture.RootFolderId],
                },
            ],
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "grandchild-file-1",
                    Name = "deep.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [grandchildId],
                },
            ],
        });

        await fixture.CreateService().RunTickAsync();

        var known = await fixture.GetKnownFolderIdsAsync();
        Assert.Contains(childId, known);
        Assert.Contains(grandchildId, known);
        Assert.Contains("grandchild-file-1", fixture.Client.DownloadedFileIds);
        Assert.Equal(1, await fixture.CountMediaAssetsAsync());
    }

    /// <summary>AC gdrive02-unrelated-folder-excluded-test (758b28e1).</summary>
    [Fact]
    public async Task RunTickAsync_UnrelatedFolderAndFile_AreExcluded()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        const string unrelatedId = "unrelated-folder-xyz";
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive02-unrelated",
            Folders =
            [
                new GoogleDriveFolderInfo
                {
                    FolderId = unrelatedId,
                    Name = "Unrelated",
                    ParentIds = ["some-other-drive-root-not-ours"],
                },
            ],
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "unrelated-file-1",
                    Name = "outside.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [unrelatedId],
                },
                new GoogleDriveFileInfo
                {
                    FileId = "root-file-ok",
                    Name = "ok.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [Fixture.RootFolderId],
                },
            ],
        });

        await fixture.CreateService().RunTickAsync();

        var known = await fixture.GetKnownFolderIdsAsync();
        Assert.DoesNotContain(unrelatedId, known);
        Assert.DoesNotContain("unrelated-file-1", fixture.Client.DownloadedFileIds);
        Assert.Contains("root-file-ok", fixture.Client.DownloadedFileIds);
        Assert.Equal(1, await fixture.CountMediaAssetsAsync());
    }

    /// <summary>AC gdrive02-trashed-folder-excluded-test (54ebcf6a).</summary>
    [Fact]
    public async Task RunTickAsync_TrashedChildFolder_IsNotAddedToKnownSet()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        const string trashedChildId = "trashed-child-folder";
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive02-trashed",
            Folders =
            [
                new GoogleDriveFolderInfo
                {
                    FolderId = trashedChildId,
                    Name = "Trashed",
                    ParentIds = [Fixture.RootFolderId],
                    Trashed = true,
                },
            ],
            Files = [],
        });

        await fixture.CreateService().RunTickAsync();

        var known = await fixture.GetKnownFolderIdsAsync();
        Assert.Contains(Fixture.RootFolderId, known);
        Assert.DoesNotContain(trashedChildId, known);
    }

    // ── GDRIVE-05 Task A: create / rename / move-in-tree ─────────────────────

    /// <summary>AC gdrive05-create-test (ce8bdda6) — tạo thư mục con mới.</summary>
    [Fact]
    public async Task RunTickAsync_NewChildFolder_CreatesMappedMediaFolderWithCorrectNameAndParent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);
        var dedicatedId = await fixture.GetOrCreateDedicatedFolderAsync();

        const string childId = "gdrive05-child-new";
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive05-create",
            Folders =
            [
                new GoogleDriveFolderInfo
                {
                    FolderId = childId,
                    Name = "Chiến dịch Q1",
                    ParentIds = [Fixture.RootFolderId],
                },
            ],
        });

        await fixture.CreateService().RunTickAsync();

        var map = await fixture.GetKnownFolderMapAsync();
        Assert.True(map.TryGetValue(childId, out var mapping));
        Assert.Equal("Chiến dịch Q1", mapping!.Name);
        Assert.Equal(Fixture.RootFolderId, mapping.DriveParentId);

        var media = await fixture.GetMediaFolderAsync(mapping.MediaFolderId);
        Assert.NotNull(media);
        Assert.Equal("Chiến dịch Q1", media!.Name);
        Assert.Equal(dedicatedId, media.ParentFolderId);
    }

    /// <summary>AC gdrive05-create-test (ce8bdda6) — đổi tên thư mục đã biết.</summary>
    [Fact]
    public async Task RunTickAsync_KnownFolderRenamed_UpdatesMediaFolderName()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        const string childId = "gdrive05-child-rename";
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive05-rename-1",
            Folders =
            [
                new GoogleDriveFolderInfo
                {
                    FolderId = childId,
                    Name = "Tên cũ",
                    ParentIds = [Fixture.RootFolderId],
                },
            ],
        });
        await fixture.CreateService().RunTickAsync();

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive05-rename-2",
            Folders =
            [
                new GoogleDriveFolderInfo
                {
                    FolderId = childId,
                    Name = "Tên mới",
                    ParentIds = [Fixture.RootFolderId],
                },
            ],
        });
        await fixture.CreateService().RunTickAsync();

        var map = await fixture.GetKnownFolderMapAsync();
        Assert.Equal("Tên mới", map[childId].Name);
        var media = await fixture.GetMediaFolderAsync(map[childId].MediaFolderId);
        Assert.Equal("Tên mới", media!.Name);
    }

    /// <summary>AC gdrive05-create-test (ce8bdda6) — di chuyển trong cây.</summary>
    [Fact]
    public async Task RunTickAsync_KnownFolderMovedWithinTree_UpdatesParentFolderId()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);
        var dedicatedId = await fixture.GetOrCreateDedicatedFolderAsync();

        const string folderA = "gdrive05-move-a";
        const string folderB = "gdrive05-move-b";
        const string folderC = "gdrive05-move-c";

        // root > A, B, C(parent=A)
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive05-move-1",
            Folders =
            [
                new GoogleDriveFolderInfo { FolderId = folderA, Name = "A", ParentIds = [Fixture.RootFolderId] },
                new GoogleDriveFolderInfo { FolderId = folderB, Name = "B", ParentIds = [Fixture.RootFolderId] },
                new GoogleDriveFolderInfo { FolderId = folderC, Name = "C", ParentIds = [folderA] },
            ],
        });
        await fixture.CreateService().RunTickAsync();

        var mapBefore = await fixture.GetKnownFolderMapAsync();
        Assert.Equal(dedicatedId, (await fixture.GetMediaFolderAsync(mapBefore[folderA].MediaFolderId))!.ParentFolderId);
        Assert.Equal(mapBefore[folderA].MediaFolderId,
            (await fixture.GetMediaFolderAsync(mapBefore[folderC].MediaFolderId))!.ParentFolderId);

        // Di chuyển C: A → B (cả hai vẫn trong cây)
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive05-move-2",
            Folders =
            [
                new GoogleDriveFolderInfo { FolderId = folderC, Name = "C", ParentIds = [folderB] },
            ],
        });
        await fixture.CreateService().RunTickAsync();

        var map = await fixture.GetKnownFolderMapAsync();
        Assert.Equal(folderB, map[folderC].DriveParentId);
        var mediaC = await fixture.GetMediaFolderAsync(map[folderC].MediaFolderId);
        Assert.Equal(map[folderB].MediaFolderId, mediaC!.ParentFolderId);
    }

    /// <summary>AC gdrive05-multilevel-same-tick-test (42804a0b).</summary>
    [Fact]
    public async Task RunTickAsync_MultilevelNewFoldersSameTick_CreatesCorrectParentChildTree()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);
        var dedicatedId = await fixture.GetOrCreateDedicatedFolderAsync();

        const string folderC = "gdrive05-ml-c";
        const string folderD = "gdrive05-ml-d";
        // Thứ tự cố ý: D (con) trước C (cha) — multi-pass phải hội tụ trong cùng tick.
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive05-multilevel",
            Folders =
            [
                new GoogleDriveFolderInfo
                {
                    FolderId = folderD,
                    Name = "D",
                    ParentIds = [folderC],
                },
                new GoogleDriveFolderInfo
                {
                    FolderId = folderC,
                    Name = "C",
                    ParentIds = [Fixture.RootFolderId],
                },
            ],
        });

        await fixture.CreateService().RunTickAsync();

        var map = await fixture.GetKnownFolderMapAsync();
        Assert.True(map.ContainsKey(folderC));
        Assert.True(map.ContainsKey(folderD));

        var mediaC = await fixture.GetMediaFolderAsync(map[folderC].MediaFolderId);
        var mediaD = await fixture.GetMediaFolderAsync(map[folderD].MediaFolderId);
        Assert.Equal("C", mediaC!.Name);
        Assert.Equal(dedicatedId, mediaC.ParentFolderId);
        Assert.Equal("D", mediaD!.Name);
        Assert.Equal(mediaC.Id, mediaD.ParentFolderId);
    }

    /// <summary>GDRIVE-05: file mới nằm đúng MediaFolder cha đã map (không còn luôn dedicated root).</summary>
    [Fact]
    public async Task RunTickAsync_FileUnderMappedChild_UsesChildMediaFolderId()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);
        var dedicatedId = await fixture.GetOrCreateDedicatedFolderAsync();

        const string childId = "gdrive05-file-parent";
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive05-file-map",
            Folders =
            [
                new GoogleDriveFolderInfo
                {
                    FolderId = childId,
                    Name = "Ảnh",
                    ParentIds = [Fixture.RootFolderId],
                },
            ],
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "file-in-child",
                    Name = "shot.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [childId],
                },
            ],
        });

        await fixture.CreateService().RunTickAsync();

        var map = await fixture.GetKnownFolderMapAsync();
        var assets = await fixture.GetGoogleDriveAssetsAsync();
        Assert.Single(assets);
        Assert.Equal(map[childId].MediaFolderId, assets[0].FolderId);
        Assert.NotEqual(dedicatedId, assets[0].FolderId);
    }

    /// <summary>
    /// AC gdrive05-legacy-folder-file-still-imported-test (a7c142c6) — violation 3b9e0603:
    /// dòng KnownFolder legacy (MediaFolderId=Empty) không được lọc khỏi map; file mới vẫn import.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_LegacyKnownFolderWithEmptyMediaFolderId_StillImportsNewFile()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);
        var dedicatedId = await fixture.GetOrCreateDedicatedFolderAsync();

        const string legacyChildId = "legacy-gdrive02-child";
        await fixture.SeedLegacyKnownFolderAsync(legacyChildId);

        // Tick không đổi tên/di chuyển thư mục legacy — chỉ thả file mới vào đó.
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive05-legacy-file",
            Folders = [],
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "file-in-legacy-child",
                    Name = "still-import.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [legacyChildId],
                },
            ],
        });

        await fixture.CreateService().RunTickAsync();

        Assert.Contains("file-in-legacy-child", fixture.Client.DownloadedFileIds);
        var assets = await fixture.GetGoogleDriveAssetsAsync();
        Assert.Contains(assets, a => a.GoogleDriveFileId == "file-in-legacy-child");
        var imported = assets.Single(a => a.GoogleDriveFileId == "file-in-legacy-child");
        // Option (a): tạm về dedicated root cho đến khi Task B reconcile đúng cây.
        Assert.Equal(dedicatedId, imported.FolderId);

        var map = await fixture.GetKnownFolderMapAsync();
        Assert.True(map.ContainsKey(legacyChildId));
        Assert.NotEqual(Guid.Empty, map[legacyChildId].MediaFolderId);
    }

    // ── GDRIVE-05 Task B: cascade soft-delete + reconcile toàn cây ───────────

    /// <summary>
    /// AC gdrive05-cascade-scope-test (cef282d1) — cây root > A > (B, C), B có file b1, C có
    /// file c1, cộng nhánh D độc lập (anh em của A) có file d1. Drive báo A bị xoá/trash qua
    /// RemovedOrTrashedIds phải cascade ĐÚNG A, B, C, b1, c1 — D và d1 tuyệt đối không bị ảnh hưởng.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_TrashedKnownFolder_CascadeDeletesOnlyThatBranch()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        const string folderA = "cascade-a";
        const string folderB = "cascade-b";
        const string folderC = "cascade-c";
        const string folderD = "cascade-d";

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "cascade-setup",
            Folders =
            [
                new GoogleDriveFolderInfo { FolderId = folderA, Name = "A", ParentIds = [Fixture.RootFolderId] },
                new GoogleDriveFolderInfo { FolderId = folderB, Name = "B", ParentIds = [folderA] },
                new GoogleDriveFolderInfo { FolderId = folderC, Name = "C", ParentIds = [folderA] },
                new GoogleDriveFolderInfo { FolderId = folderD, Name = "D", ParentIds = [Fixture.RootFolderId] },
            ],
            Files =
            [
                new GoogleDriveFileInfo { FileId = "b1", Name = "b1.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [folderB] },
                new GoogleDriveFileInfo { FileId = "c1", Name = "c1.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [folderC] },
                new GoogleDriveFileInfo { FileId = "d1", Name = "d1.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [folderD] },
            ],
        });
        await fixture.CreateService().RunTickAsync();

        var mapBefore = await fixture.GetKnownFolderMapAsync();
        Assert.True(mapBefore.ContainsKey(folderA));
        Assert.True(mapBefore.ContainsKey(folderB));
        Assert.True(mapBefore.ContainsKey(folderC));
        Assert.True(mapBefore.ContainsKey(folderD));
        var mediaA = mapBefore[folderA].MediaFolderId;
        var mediaB = mapBefore[folderB].MediaFolderId;
        var mediaC = mapBefore[folderC].MediaFolderId;
        var mediaD = mapBefore[folderD].MediaFolderId;

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "cascade-delete-a",
            RemovedOrTrashedIds = [folderA],
        });
        await fixture.CreateService().RunTickAsync();

        // Nhánh A: A, B, C và file bên trong đều bị soft-delete.
        Assert.True((await fixture.GetMediaFolderRawAsync(mediaA))!.IsDeleted);
        Assert.True((await fixture.GetMediaFolderRawAsync(mediaB))!.IsDeleted);
        Assert.True((await fixture.GetMediaFolderRawAsync(mediaC))!.IsDeleted);
        Assert.True((await fixture.GetMediaAssetByGoogleDriveFileIdRawAsync("b1"))!.IsDeleted);
        Assert.True((await fixture.GetMediaAssetByGoogleDriveFileIdRawAsync("c1"))!.IsDeleted);

        var mapAfter = await fixture.GetKnownFolderMapAsync();
        Assert.False(mapAfter.ContainsKey(folderA));
        Assert.False(mapAfter.ContainsKey(folderB));
        Assert.False(mapAfter.ContainsKey(folderC));

        // TUYỆT ĐỐI KHÔNG ảnh hưởng nhánh D (anh em của A) hay bất kỳ gì khác.
        Assert.False((await fixture.GetMediaFolderRawAsync(mediaD))!.IsDeleted);
        Assert.False((await fixture.GetMediaAssetByGoogleDriveFileIdRawAsync("d1"))!.IsDeleted);
        Assert.True(mapAfter.ContainsKey(folderD));
    }

    /// <summary>
    /// AC gdrive05-cascade-scope-test (cef282d1) — case "đã biết + DriveParentId đổi + cha mới
    /// KHÔNG còn trong bản đồ" để lại từ task t8: coi như di chuyển ra ngoài cây đang theo dõi,
    /// cascade soft-delete giống hệt bị xoá.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_KnownFolderMovedOutsideTrackedTree_CascadeDeletesBranch()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        const string folderE = "move-out-e";
        const string folderF = "move-out-f";

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "move-out-setup",
            Folders =
            [
                new GoogleDriveFolderInfo { FolderId = folderE, Name = "E", ParentIds = [Fixture.RootFolderId] },
                new GoogleDriveFolderInfo { FolderId = folderF, Name = "F", ParentIds = [folderE] },
            ],
            Files =
            [
                new GoogleDriveFileInfo { FileId = "f1", Name = "f1.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [folderF] },
            ],
        });
        await fixture.CreateService().RunTickAsync();

        var mapBefore = await fixture.GetKnownFolderMapAsync();
        var mediaE = mapBefore[folderE].MediaFolderId;
        var mediaF = mapBefore[folderF].MediaFolderId;

        // Tick 2: E báo cha mới là một folder NGOÀI cây đang theo dõi (không có trong map).
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "move-out-2",
            Folders =
            [
                new GoogleDriveFolderInfo { FolderId = folderE, Name = "E", ParentIds = ["some-external-untracked-folder"] },
            ],
        });
        await fixture.CreateService().RunTickAsync();

        Assert.True((await fixture.GetMediaFolderRawAsync(mediaE))!.IsDeleted);
        Assert.True((await fixture.GetMediaFolderRawAsync(mediaF))!.IsDeleted);
        Assert.True((await fixture.GetMediaAssetByGoogleDriveFileIdRawAsync("f1"))!.IsDeleted);

        var mapAfter = await fixture.GetKnownFolderMapAsync();
        Assert.False(mapAfter.ContainsKey(folderE));
        Assert.False(mapAfter.ContainsKey(folderF));
    }

    /// <summary>
    /// AC gdrive05-cascade-scope-test (cef282d1) — 1 file bị xoá trên Drive chỉ soft-delete đúng
    /// MediaAsset đó, không ảnh hưởng file khác trong cùng folder.
    /// </summary>
    [Fact]
    public async Task RunTickAsync_TrashedSingleFile_OnlyThatAssetSoftDeleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "single-file-setup",
            Files =
            [
                new GoogleDriveFileInfo { FileId = "keep-1", Name = "keep.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [Fixture.RootFolderId] },
                new GoogleDriveFileInfo { FileId = "remove-1", Name = "remove.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [Fixture.RootFolderId] },
            ],
        });
        await fixture.CreateService().RunTickAsync();
        Assert.Equal(2, await fixture.CountMediaAssetsAsync());

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "single-file-remove",
            RemovedOrTrashedIds = ["remove-1"],
        });
        await fixture.CreateService().RunTickAsync();

        Assert.True((await fixture.GetMediaAssetByGoogleDriveFileIdRawAsync("remove-1"))!.IsDeleted);
        Assert.False((await fixture.GetMediaAssetByGoogleDriveFileIdRawAsync("keep-1"))!.IsDeleted);
        Assert.Equal(1, await fixture.CountMediaAssetsAsync());
    }

    /// <summary>
    /// AC gdrive05-reconcile-test (0d49681f) — mô phỏng lỗi giữa chừng: reset FullTreeReconciledAt
    /// về null rồi chạy lại — lần 2 KHÔNG được tạo trùng GoogleDriveKnownFolderModel cho thư mục
    /// đã xử lý ở lần 1 (idempotent theo Drive FolderId, không dựa duy nhất vào cột cờ).
    /// </summary>
    [Fact]
    public async Task ReconcileFullTreeOnceAsync_RunTwice_DoesNotDuplicateKnownFolderRows()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        const string sub1 = "reconcile-sub-1";
        const string sub2 = "reconcile-sub-2";
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Folders =
            [
                new GoogleDriveFolderInfo { FolderId = sub1, Name = "Sub1", ParentIds = [Fixture.RootFolderId] },
                new GoogleDriveFolderInfo { FolderId = sub2, Name = "Sub2", ParentIds = [Fixture.RootFolderId] },
            ],
        };
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "reconcile-1" });
        await fixture.CreateService().RunTickAsync();

        var countAfterFirst = await fixture.CountKnownFoldersAsync();
        Assert.True(countAfterFirst >= 3); // dòng root/dedicated + sub1 + sub2

        // Mô phỏng lỗi giữa chừng: cờ chưa thật sự set thành công, tick sau phải chạy lại.
        await fixture.ResetFullTreeReconciledAtAsync();
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "reconcile-2" });
        await fixture.CreateService().RunTickAsync();

        var countAfterSecond = await fixture.CountKnownFoldersAsync();
        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    /// <summary>
    /// AC gdrive05-reconcile-test (0d49681f) — asset GoogleDrive nằm phẳng ở dedicated root (di
    /// sản GDRIVE-04) phải được ReconcileFullTreeOnceAsync đặt lại đúng thư mục con thật theo vị
    /// trí hiện tại trên Drive (ListFolderTreeAsync), không còn nằm phẳng.
    /// </summary>
    [Fact]
    public async Task ReconcileFullTreeOnceAsync_MovesFlatLegacyAssetsIntoRealSubfolder()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedOrphanGoogleDriveAssetAsync("legacy-flat-1", "flat1.jpg");
        var dedicatedId = await fixture.GetOrCreateDedicatedFolderAsync();

        const string realParent = "reconcile-real-parent";
        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Folders = [new GoogleDriveFolderInfo { FolderId = realParent, Name = "Thật", ParentIds = [Fixture.RootFolderId] }],
            Files = [new GoogleDriveFileInfo { FileId = "legacy-flat-1", Name = "flat1.jpg", MimeType = "image/jpeg", SizeBytes = 10, Parents = [realParent] }],
        };

        await fixture.SetEnabledAsync(true);
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "reconcile-move" });
        await fixture.CreateService().RunTickAsync();

        var map = await fixture.GetKnownFolderMapAsync();
        Assert.True(map.ContainsKey(realParent));
        var asset = await fixture.GetMediaAssetByGoogleDriveFileIdRawAsync("legacy-flat-1");
        Assert.NotNull(asset);
        Assert.Equal(map[realParent].MediaFolderId, asset!.FolderId);
        Assert.NotEqual(dedicatedId, asset.FolderId);
    }

    /// <summary>
    /// AC gdrive05-reconcile-test (0d49681f) — mô phỏng lỗi giữa chừng THẬT SỰ: một phần thư mục
    /// đã được tạo ở lần chạy trước (vd. crash mạng sau khi tạo xong Sub1 nhưng trước khi tạo
    /// Sub2 hoặc set cờ). Lần chạy lại (FullTreeReconciledAt vẫn null) phải: không tạo trùng
    /// dòng ánh xạ cho Sub1 đã có, và tạo nốt Sub2 còn thiếu — khác với test RunTwice ở trên
    /// (test đó reset cờ SAU KHI đã tạo xong toàn bộ, không thật sự mô phỏng dở dang giữa chừng).
    /// </summary>
    [Fact]
    public async Task ReconcileFullTreeOnceAsync_TruePartialFailureMidRun_CompletesWithoutDuplicating()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dedicatedId = await fixture.GetOrCreateDedicatedFolderAsync();

        const string sub1 = "partial-sub-1";
        const string sub2 = "partial-sub-2";

        // Dòng ánh xạ Sub1 đã tồn tại TRƯỚC khi tick này chạy — mô phỏng đúng những gì một lần
        // ReconcileFullTreeOnceAsync trước đó đã ghi xuống DB rồi mới crash (không đi qua
        // SyncService lần này, đúng ngữ nghĩa "đã có sẵn từ trước").
        await fixture.CreateMappedChildFolderDirectAsync(sub1, "Sub1", Fixture.RootFolderId, dedicatedId);

        fixture.Client.FolderTree = new GoogleDriveFolderTree
        {
            Folders =
            [
                new GoogleDriveFolderInfo { FolderId = sub1, Name = "Sub1", ParentIds = [Fixture.RootFolderId] },
                new GoogleDriveFolderInfo { FolderId = sub2, Name = "Sub2", ParentIds = [Fixture.RootFolderId] },
            ],
        };

        await fixture.SetEnabledAsync(true);
        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage { NextPageToken = "partial-retry" });
        await fixture.CreateService().RunTickAsync();

        var map = await fixture.GetKnownFolderMapAsync();
        Assert.True(map.ContainsKey(sub1));
        Assert.True(map.ContainsKey(sub2));

        // Sub1 (đã có TRƯỚC tick này) không bị tạo trùng — vẫn đúng 1 dòng active.
        Assert.Equal(1, await fixture.CountActiveKnownFolderRowsAsync(sub1));
        // Sub2 được tạo mới đúng 1 dòng — không nhân đôi do vòng lặp hội tụ chạy nhiều pass.
        Assert.Equal(1, await fixture.CountActiveKnownFolderRowsAsync(sub2));
    }

    // ── GDRIVE-04: folder chuyên dụng + backfill ────────────────────────────

    /// <summary>AC gdrive04-dedicated-folder-idempotent-test (1a10f3d6).</summary>
    [Fact]
    public async Task GetOrCreateDedicatedFolderAsync_IsIdempotent_AndCreateUsesThatFolder()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetEnabledAsync(true);

        var first = await fixture.GetOrCreateDedicatedFolderAsync();
        var second = await fixture.GetOrCreateDedicatedFolderAsync();
        Assert.Equal(first, second);

        fixture.Client.Pages.Enqueue(new GoogleDriveChangesPage
        {
            NextPageToken = "gdrive04-new",
            Files =
            [
                new GoogleDriveFileInfo
                {
                    FileId = "new-in-dedicated",
                    Name = "shot.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [Fixture.RootFolderId],
                },
            ],
        });
        await fixture.CreateService().RunTickAsync();

        var assets = await fixture.GetGoogleDriveAssetsAsync();
        Assert.Single(assets);
        Assert.Equal(first, assets[0].FolderId);
        Assert.DoesNotContain(assets, a => a.FolderId is null);
    }

    /// <summary>AC gdrive04-backfill-old-assets-test (e53f870a).</summary>
    [Fact]
    public async Task GetOrCreateDedicatedFolderAsync_BackfillsOrphanGoogleDriveAssets()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedOrphanGoogleDriveAssetAsync("orphan-1", "old1.jpg");
        await fixture.SeedOrphanGoogleDriveAssetAsync("orphan-2", "old2.jpg");

        var dedicatedId = await fixture.GetOrCreateDedicatedFolderAsync();

        var assets = await fixture.GetGoogleDriveAssetsAsync();
        Assert.Equal(2, assets.Count);
        Assert.All(assets, a => Assert.Equal(dedicatedId, a.FolderId));
        Assert.DoesNotContain(assets, a => a.FolderId is null);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string RootFolderId = "root-folder";

        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;

        public DbContextOptions<AppDbContext> DbOptions { get; }
        public FakeGoogleDriveClient Client { get; }
        public GoogleDriveOptions Settings { get; }
        public FileStorageOptions FileStorageSettings { get; }

        private Fixture(
            SqliteConnection connection,
            DbContextOptions<AppDbContext> dbOptions,
            FakeGoogleDriveClient client,
            GoogleDriveOptions settings,
            FileStorageOptions fileStorageOptions,
            ServiceProvider services)
        {
            _connection = connection;
            DbOptions = dbOptions;
            Client = client;
            Settings = settings;
            FileStorageSettings = fileStorageOptions;
            _services = services;
        }

        public static async Task<Fixture> CreateAsync(int maxRetryAttempts = 5, int maxFilesPerTick = 20)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

            await using (var db = new AppDbContext(dbOptions))
                await db.Database.EnsureCreatedAsync();

            var client = new FakeGoogleDriveClient();
            var gdriveOptions = new GoogleDriveOptions
            {
                Enabled = true,
                IntervalSeconds = 180,
                MaxFilesPerTick = maxFilesPerTick,
                MaxRetryAttempts = maxRetryAttempts,
                FolderId = RootFolderId,
            };
            var fileStorageOptions = new FileStorageOptions();
            var fileStorage = new RecordingFileStorageService();

            var services = new ServiceCollection()
                .AddSingleton(dbOptions)
                .AddScoped(_ => new AppDbContext(dbOptions))
                .AddScoped<IUserContext, StubUserContext>()
                .AddScoped<MediaFolderRepository>()
                .AddSingleton<IFileStorageService>(fileStorage)
                .AddScoped<MediaAssetRepository>()
                .AddScoped<GoogleDriveRepository>()
                .AddSingleton<IGoogleDriveClient>(client)
                .AddSingleton<IOptions<GoogleDriveOptions>>(Options.Create(gdriveOptions))
                .AddSingleton<IOptions<FileStorageOptions>>(Options.Create(fileStorageOptions))
                .AddScoped<GoogleDriveSyncService>()
                .AddLogging()
                .BuildServiceProvider();

            return new Fixture(connection, dbOptions, client, gdriveOptions, fileStorageOptions, services);
        }

        /// <summary>
        /// Mỗi lần gọi trả về MỘT scope + instance GoogleDriveSyncService mới — đúng với cách
        /// production dùng (worker tạo scope mỗi tick, controller nhận instance scoped theo mỗi
        /// request). Lock chia sẻ giữa các instance là static nên vẫn đúng ngữ nghĩa khoá chung.
        /// </summary>
        public GoogleDriveSyncService CreateService() =>
            _services.CreateScope().ServiceProvider.GetRequiredService<GoogleDriveSyncService>();

        public async Task SetEnabledAsync(bool enabled, bool completeInitialSnapshot = true)
        {
            await using var db = new AppDbContext(DbOptions);
            var repository = CreateRepository(db);
            await repository.SetEnabledAsync(enabled, "test");
            // Test delta giả định đã qua scan đầu. Không đánh dấu thì tick đầu import snapshot
            // và bỏ qua trang changes.list đã enqueue.
            if (enabled && completeInitialSnapshot)
            {
                var state = await db.Set<GoogleDriveSyncStateModel>()
                    .SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId);
                state.InitialSnapshotCompletedAt = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(state.PageToken))
                    state.PageToken = "already-tracking";
                await db.SaveChangesAsync();
            }
        }

        public async Task SetPageTokenAsync(string pageToken)
        {
            await using var db = new AppDbContext(DbOptions);
            var state = await db.Set<GoogleDriveSyncStateModel>()
                .SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId);
            state.PageToken = pageToken;
            state.InitialSnapshotCompletedAt = null;
            await db.SaveChangesAsync();
        }

        public async Task<GoogleDriveSyncStateModel> GetStateAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await CreateRepository(db).GetSyncStateAsync();
        }

        public async Task<List<GoogleDriveImportFailureModel>> GetRetryableFailuresAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await CreateRepository(db).GetRetryableFailuresAsync(Settings.MaxRetryAttempts, Settings.MaxFilesPerTick);
        }

        public async Task<List<GoogleDriveImportFailureModel>> GetExhaustedFailuresAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await CreateRepository(db).GetExhaustedFailuresAsync(Settings.MaxRetryAttempts, Settings.MaxFilesPerTick);
        }

        public async Task<GoogleDriveImportFailureModel?> GetFailureAsync(string fileId)
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.Set<GoogleDriveImportFailureModel>()
                .FirstOrDefaultAsync(x => x.GoogleDriveFileId == fileId);
        }

        public async Task SeedFailureAttemptsAsync(string fileId, int attempts)
        {
            await using var db = new AppDbContext(DbOptions);
            var repository = CreateRepository(db);
            for (var i = 0; i < attempts; i++)
                await repository.UpsertFailureAsync(fileId, fileId + ".jpg", "image/jpeg", 10, "seed");
        }

        public async Task<HashSet<string>> GetKnownFolderIdsAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await CreateRepository(db).GetKnownFolderIdsAsync();
        }

        public async Task<int> CountMediaAssetsAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.MediaAssets.CountAsync(x => !x.IsDeleted);
        }

        public async Task<MediaAssetModel> CreateExistingAssetAsync(string googleDriveFileId)
        {
            await using var db = new AppDbContext(DbOptions);
            var dedicatedId = await CreateRepository(db).GetOrCreateDedicatedFolderAsync(RootFolderId);
            var repo = new MediaAssetRepository(db, new StubUserContext(), new RecordingFileStorageService());
            return await repo.CreateFromGoogleDriveAsync(
                [1, 2, 3],
                new GoogleDriveFileInfo
                {
                    FileId = googleDriveFileId,
                    Name = "old.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 10,
                    Parents = [RootFolderId],
                },
                dedicatedId);
        }

        public async Task<Guid> GetOrCreateDedicatedFolderAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await CreateRepository(db).GetOrCreateDedicatedFolderAsync(RootFolderId);
        }

        public async Task<Dictionary<string, GoogleDriveKnownFolderModel>> GetKnownFolderMapAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await CreateRepository(db).GetKnownFolderMapAsync();
        }

        public async Task<MediaFolderModel?> GetMediaFolderAsync(Guid id)
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.Set<MediaFolderModel>()
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted);
        }

        /// <summary>Không lọc IsDeleted — dùng để kiểm tra cascade soft-delete đã set cờ đúng chưa.</summary>
        public async Task<MediaFolderModel?> GetMediaFolderRawAsync(Guid id)
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.Set<MediaFolderModel>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
        }

        /// <summary>Không lọc IsDeleted — dùng để kiểm tra cascade soft-delete MediaAsset.</summary>
        public async Task<MediaAssetModel?> GetMediaAssetByGoogleDriveFileIdRawAsync(string googleDriveFileId)
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.Set<MediaAssetModel>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.GoogleDriveFileId == googleDriveFileId);
        }

        /// <summary>Không lọc IsDeleted — dùng để kiểm tra bản đồ ánh xạ đã bị cascade loại bỏ chưa.</summary>
        public async Task<GoogleDriveKnownFolderModel?> GetKnownFolderRawAsync(string driveFolderId)
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.Set<GoogleDriveKnownFolderModel>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.FolderId == driveFolderId);
        }

        public async Task<int> CountKnownFoldersAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.Set<GoogleDriveKnownFolderModel>().CountAsync(x => !x.IsDeleted);
        }

        public async Task<int> CountActiveKnownFolderRowsAsync(string driveFolderId)
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.Set<GoogleDriveKnownFolderModel>()
                .CountAsync(x => x.FolderId == driveFolderId && !x.IsDeleted);
        }

        /// <summary>
        /// Ghi thẳng một dòng ánh xạ qua repository — mô phỏng trạng thái DB do một lần
        /// ReconcileFullTreeOnceAsync TRƯỚC ĐÓ đã tạo xong rồi mới crash, không đi qua SyncService
        /// của lần chạy đang test.
        /// </summary>
        public async Task<GoogleDriveKnownFolderModel> CreateMappedChildFolderDirectAsync(
            string driveFolderId, string name, string driveParentId, Guid parentMediaFolderId)
        {
            await using var db = new AppDbContext(DbOptions);
            return await CreateRepository(db).CreateMappedChildFolderAsync(
                driveFolderId, name, driveParentId, parentMediaFolderId);
        }

        /// <summary>Mô phỏng "lỗi giữa chừng": cờ chưa từng được set thật, buộc tick sau chạy lại reconcile.</summary>
        public async Task ResetFullTreeReconciledAtAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            var state = await db.Set<GoogleDriveSyncStateModel>()
                .SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId);
            state.FullTreeReconciledAt = null;
            await db.SaveChangesAsync();
        }

        public async Task SeedOrphanGoogleDriveAssetAsync(string googleDriveFileId, string name)
            => await SeedGoogleDriveAssetAsync(googleDriveFileId, name, isDeleted: false);

        public Task SeedSoftDeletedGoogleDriveAssetAsync(string googleDriveFileId, string name)
            => SeedGoogleDriveAssetAsync(googleDriveFileId, name, isDeleted: true);

        private async Task SeedGoogleDriveAssetAsync(string googleDriveFileId, string name, bool isDeleted)
        {
            await using var db = new AppDbContext(DbOptions);
            db.MediaAssets.Add(new MediaAssetModel
            {
                Id = Guid.NewGuid(),
                FileName = name,
                OriginalFileName = name,
                StoragePath = $"google-drive/orphan/{googleDriveFileId}",
                MimeType = "image/jpeg",
                FileSize = 10,
                Source = MediaSource.GoogleDrive,
                FolderId = null,
                GoogleDriveFileId = googleDriveFileId,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = isDeleted,
            });
            await db.SaveChangesAsync();
        }

        public async Task<int> CountRowsByGoogleDriveFileIdAsync(string googleDriveFileId)
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.MediaAssets.CountAsync(x => x.GoogleDriveFileId == googleDriveFileId);
        }

        /// <summary>Mô phỏng UNIQUE constraint thất bại sau khi entity đã được track — đúng lỗi save một file.</summary>
        public async Task InstallUniquePoisonTriggerAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_poison_insert
                BEFORE INSERT ON MediaAssets
                WHEN NEW.GoogleDriveFileId = 'poison-1'
                BEGIN
                    SELECT RAISE(ABORT, 'simulated save failure');
                END;
                """);
        }

        /// <summary>
        /// Mô phỏng dòng KnownFolder di sản GDRIVE-02 sau migration thêm cột
        /// (chỉ có FolderId, MediaFolderId = Guid.Empty).
        /// </summary>
        public async Task SeedLegacyKnownFolderAsync(string driveFolderId)
        {
            await using var db = new AppDbContext(DbOptions);
            db.Set<GoogleDriveKnownFolderModel>().Add(new GoogleDriveKnownFolderModel
            {
                Id = Guid.NewGuid(),
                FolderId = driveFolderId,
                MediaFolderId = Guid.Empty,
                DriveParentId = null,
                Name = string.Empty,
                DiscoveredAt = DateTime.UtcNow.AddDays(-30),
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                CreatedBy = "legacy",
                IsDeleted = false,
            });
            await db.SaveChangesAsync();
        }

        public async Task<List<MediaAssetModel>> GetGoogleDriveAssetsAsync()
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.MediaAssets
                .Where(x => !x.IsDeleted && x.Source == MediaSource.GoogleDrive)
                .ToListAsync();
        }

        private static GoogleDriveRepository CreateRepository(AppDbContext db) =>
            new(db, new StubUserContext());

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeGoogleDriveClient : IGoogleDriveClient
    {
        public string StartToken { get; set; } = "start-token";
        public Queue<GoogleDriveChangesPage> Pages { get; } = new();
        /// <summary>GDRIVE-05 Task B: cây trả về từ ListFolderTreeAsync (ReconcileFullTreeOnceAsync).</summary>
        public GoogleDriveFolderTree FolderTree { get; set; } = new();
        public int ListFolderTreeCalls { get; private set; }
        /// <summary>Số lần ListFolderTreeAsync đầu ném IOException rồi mới trả cây.</summary>
        public int ListFolderTreeFailures { get; set; }
        /// <summary>File tải lên sau khi liệt kê cây. Chỉ xuất hiện ở changes.list nếu start token lấy TRƯỚC lúc liệt kê.</summary>
        public GoogleDriveFileInfo? LateFile { get; set; }
        public List<string> CallJournal { get; } = [];
        private bool _startTokenCaptured;
        private bool _lateFileVisible;
        private bool _lateFileEmitted;
        /// <summary>
        /// Backlog "thô" — khi Pages rỗng, mỗi ListChangesAsync tự cắt tối đa maxResults phần tử
        /// từ đây, mô phỏng đúng cách Drive API thật giới hạn PageSize ở tầng SERVER, không phải
        /// worker tự Take() sau khi nhận nguyên trang (đây chính là bug đã sửa ở t7).
        /// </summary>
        public Queue<GoogleDriveFileInfo> Backlog { get; } = new();
        public List<string> DownloadedFileIds { get; } = [];
        public int ListChangesCalls { get; private set; }
        public List<int> MaxResultsSeen { get; } = [];

        /// <summary>Delay nhân tạo bên trong ListChangesAsync — dùng để ép mở "cửa sổ" đua nếu khoá không hoạt động.</summary>
        public int CriticalSectionDelayMs { get; set; }
        public int MaxConcurrentCriticalSections { get; private set; }

        private readonly Dictionary<string, Queue<Func<byte[]>>> _downloadBehaviors = new();
        private int _backlogTokenCounter;
        private int _activeCriticalSections;
        private readonly Lock _counterLock = new();

        public void FailDownload(string fileId, int times)
        {
            var queue = new Queue<Func<byte[]>>();
            for (var i = 0; i < times; i++)
                queue.Enqueue(() => throw new IOException($"simulated failure #{i + 1} for {fileId}"));
            _downloadBehaviors[fileId] = queue;
        }

        public bool IsConfigured() => true;
        public string? DescribeConfigIssue() => null;

        public Task<string> GetStartPageTokenAsync(CancellationToken ct = default)
        {
            CallJournal.Add("GetStartPageToken");
            _startTokenCaptured = true;
            return Task.FromResult(StartToken);
        }

        /// <summary>Khi set, lần vào critical section đầu của một đợt chờ đến khi được nhả.</summary>
        public TaskCompletionSource? HoldCriticalSection { get; set; }
        public TaskCompletionSource CriticalSectionEntered { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _holdClaimsRemaining;

        public void ReleaseCriticalSection()
        {
            var hold = HoldCriticalSection;
            HoldCriticalSection = null;
            _holdClaimsRemaining = 0;
            hold?.TrySetResult();
        }

        public void ArmSingleHold()
        {
            HoldCriticalSection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CriticalSectionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _holdClaimsRemaining = 1;
        }

        private async Task EnterCriticalSectionAsync(CancellationToken ct)
        {
            var active = Interlocked.Increment(ref _activeCriticalSections);
            lock (_counterLock)
                MaxConcurrentCriticalSections = Math.Max(MaxConcurrentCriticalSections, active);
            var hold = HoldCriticalSection;
            if (hold is not null && Interlocked.Decrement(ref _holdClaimsRemaining) >= 0)
            {
                CriticalSectionEntered.TrySetResult();
                await hold.Task.WaitAsync(ct);
            }
            else if (CriticalSectionDelayMs > 0)
            {
                await Task.Delay(CriticalSectionDelayMs, ct);
            }
        }

        private void LeaveCriticalSection()
            => Interlocked.Decrement(ref _activeCriticalSections);

        public async Task<GoogleDriveChangesPage> ListChangesAsync(
            string? pageToken, int maxResults, CancellationToken ct = default)
        {
            await EnterCriticalSectionAsync(ct);
            try
            {
                ListChangesCalls++;
                MaxResultsSeen.Add(maxResults);

                if (Pages.Count > 0)
                    return Pages.Dequeue();

                if (LateFile is not null && _lateFileVisible && !_lateFileEmitted)
                {
                    _lateFileEmitted = true;
                    return new GoogleDriveChangesPage
                    {
                        NextPageToken = "after-late",
                        Files = [LateFile]
                    };
                }

                if (Backlog.Count > 0)
                {
                    var batch = new List<GoogleDriveFileInfo>();
                    while (batch.Count < maxResults && Backlog.Count > 0)
                        batch.Add(Backlog.Dequeue());
                    _backlogTokenCounter++;
                    return new GoogleDriveChangesPage
                    {
                        Files = batch,
                        NextPageToken = $"backlog-token-{_backlogTokenCounter}",
                    };
                }

                return new GoogleDriveChangesPage { NextPageToken = pageToken };
            }
            finally
            {
                LeaveCriticalSection();
            }
        }

        public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
        {
            await EnterCriticalSectionAsync(ct);
            try
            {
                DownloadedFileIds.Add(fileId);
                if (_downloadBehaviors.TryGetValue(fileId, out var queue) && queue.Count > 0)
                    return queue.Dequeue()();
                return new byte[] { 1, 2, 3 };
            }
            finally
            {
                LeaveCriticalSection();
            }
        }

        public async Task<GoogleDriveFolderTree> ListFolderTreeAsync(string rootFolderId, CancellationToken ct = default)
        {
            await EnterCriticalSectionAsync(ct);
            try
            {
                CallJournal.Add("ListFolderTree");
                ListFolderTreeCalls++;
                if (ListFolderTreeFailures > 0)
                {
                    ListFolderTreeFailures--;
                    throw new IOException("simulated transient folder listing failure");
                }

                if (_startTokenCaptured && LateFile is not null)
                    _lateFileVisible = true;
                return FolderTree;
            }
            finally
            {
                LeaveCriticalSection();
            }
        }
    }

    private sealed class RecordingFileStorageService : IFileStorageService
    {
        public Task<FileSaveResult> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<FileSaveResult> SaveBytesAsync(
            byte[] data, string folder, string extension, string contentType, CancellationToken ct = default)
            => Task.FromResult(new FileSaveResult
            {
                StorageKey = $"{folder}/2026/09/28/{Guid.NewGuid():N}{extension}",
                OriginalFileName = $"{Guid.NewGuid():N}{extension}",
                ContentType = contentType,
                SizeBytes = data.Length,
            });

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default) => Task.FromResult(true);

        public Task DeleteAsync(string storageKey, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? GetCurrentUserId() => null;
        public string? GetCurrentUserName() => "test";
        public IReadOnlyList<string> GetCurrentUserRoles() => [];
    }
}
