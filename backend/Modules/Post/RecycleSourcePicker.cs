using Backend.Data;
using Backend.Modules.Campaign.Enums;
using Backend.Modules.MediaAsset;
using Backend.Modules.Post.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Post;

/// <summary>
/// Chọn bài Published ngẫu nhiên trên một page (trừ TextOnly và bài từ tin/NewsArticle)
/// + tuỳ chọn lọc Ảnh/Video. Dùng chung cho api/Post/recycle và CampaignGenerationService.
/// </summary>
public class RecycleSourcePicker(AppDbContext context)
{
    public async Task<List<PostModel>> PickRandomPublishedAsync(
        Guid channelId,
        int take,
        CampaignMediaType? mediaFilter = null,
        CancellationToken ct = default)
    {
        if (take < 1) return [];

        // NewsArticleId != null = bài fanpage từ tin (CỬA 2 / ContentCrawl) — không tái sử dụng
        // làm bài nguồn chiến dịch. TextOnly cũng loại (thường là tin chỉ chữ + link).
        var query = context.Set<PostModel>()
            .Where(p => !p.IsDeleted
                && p.Status == PostStatus.Published
                && p.SocialChannelId == channelId
                && p.GenerationFlow != GenerationFlow.TextOnly
                && p.NewsArticleId == null);

        if (mediaFilter == CampaignMediaType.Image)
        {
            query = query.Where(p =>
                context.Set<PostMediaModel>().Any(m =>
                    !m.IsDeleted
                    && m.PostId == p.Id
                    && context.Set<MediaAssetModel>().Any(a =>
                        !a.IsDeleted
                        && a.Id == m.MediaId
                        && a.MimeType.StartsWith("image/")))
                && !context.Set<PostMediaModel>().Any(m =>
                    !m.IsDeleted
                    && m.PostId == p.Id
                    && context.Set<MediaAssetModel>().Any(a =>
                        !a.IsDeleted
                        && a.Id == m.MediaId
                        && a.MimeType.StartsWith("video/"))));
        }
        else if (mediaFilter == CampaignMediaType.Video)
        {
            query = query.Where(p =>
                context.Set<PostMediaModel>().Any(m =>
                    !m.IsDeleted
                    && m.PostId == p.Id
                    && context.Set<MediaAssetModel>().Any(a =>
                        !a.IsDeleted
                        && a.Id == m.MediaId
                        && a.MimeType.StartsWith("video/"))));
        }

        return await query
            .OrderBy(_ => EF.Functions.Random())
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>Chép PostMedia KeepOld từ bài nguồn sang bài mới (chưa SaveChanges).</summary>
    public async Task CopyMediaKeepOldAsync(
        Guid newPostId,
        Guid sourcePostId,
        DateTime now,
        string? actor,
        CancellationToken ct = default)
    {
        var sourceMediaList = await context.Set<PostMediaModel>()
            .Where(m => !m.IsDeleted && m.PostId == sourcePostId)
            .ToListAsync(ct);

        foreach (var media in sourceMediaList)
        {
            context.Add(new PostMediaModel
            {
                Id = Guid.NewGuid(),
                PostId = newPostId,
                MediaId = media.MediaId,
                MediaRole = media.MediaRole,
                SortOrder = media.SortOrder,
                CreatedAt = now,
                CreatedBy = actor,
            });
        }
    }
}
