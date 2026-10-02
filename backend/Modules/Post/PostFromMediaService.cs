using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Modules.Post.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Post;

public record PostFromMediaResult(PostResponse? Post, BulkCreateResult? Batch);

/// <summary>
/// Tạo bài Approved từ ảnh Media đã chọn. Không gọi AI. Mọi Page nằm trong một transaction.
/// </summary>
public class PostFromMediaService(
    AppDbContext context,
    PostRepository posts,
    PostWorkflowService workflow,
    PostMediaRepository postMedia,
    MediaFolderRepository mediaFolders)
{
    public const int MaxMediaCount = 10;

    public async Task<PostFromMediaResult> CreateAsync(
        CreatePostFromMediaRequest request, CancellationToken ct = default)
    {
        var content = (request.Content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Nội dung không được để trống");

        var mediaIds = request.MediaIds ?? [];
        if (mediaIds.Count is < 1 or > MaxMediaCount)
            throw new ArgumentException("Phải chọn từ 1 đến 10 ảnh");
        if (mediaIds.Distinct().Count() != mediaIds.Count)
            throw new ArgumentException("Danh sách ảnh bị trùng");

        var channelIds = request.SocialChannelIds ?? [];
        if (channelIds.Count == 0)
            throw new ArgumentException("Phải chọn ít nhất một Page");
        if (channelIds.Distinct().Count() != channelIds.Count)
            throw new ArgumentException("Danh sách Page bị trùng");

        await EnsureMediaUsableAsync(mediaIds, ct);
        await mediaFolders.EnsureChannelsWritableAsync(channelIds, ct);

        var title = ResolveTitle(request.Title, content);
        var batchId = channelIds.Count > 1 ? Guid.NewGuid() : (Guid?)null;
        var createdIds = new List<Guid>();

        await using var tx = await context.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var channelId in channelIds)
            {
                var post = await posts.CreateUserSelectedDraftAsync(
                    title, content, channelId, request.CategoryId, batchId, ct);
                await postMedia.ReplaceGalleryAsync(post.Id, mediaIds, ct);
                await workflow.SubmitForReviewAsync(post.Id, ct);
                await workflow.ApproveAsync(post.Id, ct);
                createdIds.Add(post.Id);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        if (createdIds.Count == 1)
        {
            var response = await posts.GetResponseByIdAsync(createdIds[0], ct);
            return new PostFromMediaResult(response, null);
        }

        return new PostFromMediaResult(null, new BulkCreateResult
        {
            BatchId = batchId!.Value,
            Created = createdIds.Count,
            PostIds = createdIds,
        });
    }

    private async Task EnsureMediaUsableAsync(IReadOnlyList<Guid> mediaIds, CancellationToken ct)
    {
        var assets = await context.Set<MediaAssetModel>()
            .IgnoreQueryFilters()
            .Where(x => mediaIds.Contains(x.Id))
            .ToListAsync(ct);
        var byId = assets.ToDictionary(x => x.Id);

        foreach (var id in mediaIds)
        {
            if (!byId.TryGetValue(id, out var asset)
                || asset.IsDeleted
                || string.IsNullOrWhiteSpace(asset.StoragePath)
                || !asset.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Ảnh không hợp lệ.");
            }
        }
    }

    private static string ResolveTitle(string? title, string content)
    {
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        var line = content.Split('\n', 2)[0].Trim();
        return line.Length <= 120 ? line : line[..120];
    }
}
