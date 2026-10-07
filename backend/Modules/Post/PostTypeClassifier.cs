using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.SocialChannel.Enums;

namespace Backend.Modules.Post;

/// <summary>
/// Nguồn duy nhất suy ra loại bài (Tin / Video ngắn / Video / Ảnh / Văn bản) cho lọc và tô màu lịch.
/// Ưu tiên: Tin &gt; Video ngắn &gt; Video &gt; Ảnh &gt; Văn bản.
/// </summary>
public static class PostTypeClassifier
{
    public const string News = "Tin";
    public const string ShortVideo = "Video ngắn";
    public const string Video = "Video";
    public const string Image = "Ảnh";
    public const string Text = "Văn bản";

    public readonly record struct MediaSignal(
        string? MimeType,
        MediaSource Source,
        string? OriginalFileName,
        string? AltText);

    public static string Classify(
        Guid? newsArticleId,
        SocialPlatform? platform,
        string? extraJson,
        IReadOnlyList<MediaSignal> media)
    {
        if (newsArticleId is Guid newsId && newsId != Guid.Empty)
            return News;

        var videos = media.Where(m => IsVideo(m.MimeType)).ToList();
        if (videos.Count > 0)
            return IsShortVideo(platform, extraJson, videos) ? ShortVideo : Video;

        if (media.Any(m => IsImage(m.MimeType)))
            return Image;

        return Text;
    }

    /// <summary>
    /// Video ngắn = có video AND (TikTok | Facebook Reels path | marker ReelsRender | reelsRequested).
    /// Provenance: decision PCS 7a59a10e; FacebookPagePublishService.cs:60-64;
    /// GenerationJobPipelineService.ProcessReelsRenderAsync ~1722-1727; PendingReelsHelper.
    /// </summary>
    public static bool IsShortVideo(
        SocialPlatform? platform,
        string? extraJson,
        IReadOnlyList<MediaSignal> videos)
    {
        if (videos.Count == 0) return false;

        if (platform == SocialPlatform.TikTok)
            return true;

        // Facebook: đúng 1 video Cover → PublishReelAsync (FacebookPagePublishService.cs:56-64).
        if (platform == SocialPlatform.Facebook)
            return true;

        if (PendingReelsHelper.TryRead(extraJson))
            return true;

        foreach (var v in videos)
        {
            if (v.Source != MediaSource.Overlay) continue;
            if (v.OriginalFileName?.Contains("-reels", StringComparison.OrdinalIgnoreCase) == true)
                return true;
            if (v.AltText?.StartsWith("Reels video", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        return false;
    }

    public static bool IsVideo(string? mimeType)
        => mimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsImage(string? mimeType)
        => mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
}
