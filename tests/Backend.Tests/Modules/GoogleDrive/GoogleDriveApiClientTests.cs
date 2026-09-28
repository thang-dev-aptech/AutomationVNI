using Backend.Modules.GoogleDrive;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.GoogleDrive;

/// <summary>
/// GDRIVE-01 (t2): GoogleDriveApiClient nói chuyện với Drive API thật nên chỉ test được phần
/// không cần mạng — DescribeConfigIssue/IsConfigured và các guard chạy TRƯỚC khi chạm network
/// (BuildDriveService/pageToken rỗng). Hành vi lọc changes.list thật thuộc IGoogleDriveClient
/// fake dùng ở test worker (task khác).
/// </summary>
public class GoogleDriveApiClientTests
{
    [Fact]
    public void DescribeConfigIssue_ReportsMissingFolderId()
    {
        var client = CreateClient(new GoogleDriveOptions { FolderId = "", CredentialsPath = "" });

        var issue = client.DescribeConfigIssue();

        Assert.NotNull(issue);
        Assert.Contains("FolderId", issue);
        Assert.False(client.IsConfigured());
    }

    [Fact]
    public void DescribeConfigIssue_ReportsMissingCredentialsPath()
    {
        var client = CreateClient(new GoogleDriveOptions { FolderId = "folder-123", CredentialsPath = "" });

        var issue = client.DescribeConfigIssue();

        Assert.NotNull(issue);
        Assert.Contains("CredentialsPath", issue);
        Assert.False(client.IsConfigured());
    }

    [Fact]
    public void DescribeConfigIssue_ReportsMissingCredentialsFile()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"gdrive-missing-{Guid.NewGuid():N}.json");
        var client = CreateClient(new GoogleDriveOptions { FolderId = "folder-123", CredentialsPath = missingPath });

        var issue = client.DescribeConfigIssue();

        Assert.NotNull(issue);
        Assert.Contains(missingPath, issue);
        Assert.False(client.IsConfigured());
    }

    [Fact]
    public void IsConfigured_TrueWhenFolderIdSetAndCredentialsFileExists()
    {
        var existingPath = Path.GetTempFileName();
        try
        {
            var client = CreateClient(new GoogleDriveOptions { FolderId = "folder-123", CredentialsPath = existingPath });

            Assert.Null(client.DescribeConfigIssue());
            Assert.True(client.IsConfigured());
        }
        finally
        {
            File.Delete(existingPath);
        }
    }

    [Fact]
    public async Task ListChangesAsync_ThrowsOnEmptyPageToken_EvenWhenUnconfigured()
    {
        var client = CreateClient(new GoogleDriveOptions { FolderId = "", CredentialsPath = "" });

        await Assert.ThrowsAsync<ArgumentException>(() => client.ListChangesAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ListChangesAsync(null));
    }

    [Fact]
    public async Task GetStartPageTokenAsync_ThrowsConfigIssue_WhenNotConfigured()
    {
        var client = CreateClient(new GoogleDriveOptions { FolderId = "", CredentialsPath = "" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetStartPageTokenAsync());
        Assert.Contains("FolderId", ex.Message);
    }

    [Fact]
    public async Task DownloadFileAsync_ThrowsConfigIssue_WhenNotConfigured()
    {
        var client = CreateClient(new GoogleDriveOptions { FolderId = "", CredentialsPath = "" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadFileAsync("file-1"));
        Assert.Contains("FolderId", ex.Message);
    }

    private static GoogleDriveApiClient CreateClient(GoogleDriveOptions options) =>
        new(Options.Create(options));
}
