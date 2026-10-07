using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.Post;
using Backend.Modules.SocialChannel.Enums;
using Xunit;

namespace Backend.Tests.Modules.Post;

/// <summary>
/// R-033 AC post-type-classification-test (1e796317): bảng Theory cho PostTypeClassifier.
/// </summary>
public class PostTypeClassifierTests
{
    public static TheoryData<string, Guid?, SocialPlatform?, string?, string?, MediaSource, string> Cases
    {
        get
        {
            var newsId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var data = new TheoryData<string, Guid?, SocialPlatform?, string?, string?, MediaSource, string>
            {
                // Tin thắng cả khi có ảnh
                { "news-with-image", newsId, SocialPlatform.Facebook, null, "image/jpeg", MediaSource.Upload, PostTypeClassifier.News },
                // Video ngắn: TikTok + video
                { "tiktok-video", null, SocialPlatform.TikTok, null, "video/mp4", MediaSource.Upload, PostTypeClassifier.ShortVideo },
                // Video ngắn: Facebook + video (PublishReelAsync)
                { "facebook-video-reels", null, SocialPlatform.Facebook, null, "video/mp4", MediaSource.Upload, PostTypeClassifier.ShortVideo },
                // Video ngắn: marker ReelsRender
                { "overlay-reels-file", null, SocialPlatform.LinkedIn, null, "video/mp4", MediaSource.Overlay, PostTypeClassifier.ShortVideo },
                // Video ngắn: ExtraJson.reelsRequested + video
                { "reels-requested", null, SocialPlatform.LinkedIn, """{"reelsRequested":true}""", "video/mp4", MediaSource.Upload, PostTypeClassifier.ShortVideo },
                // Video thường (LinkedIn, không marker)
                { "linkedin-video", null, SocialPlatform.LinkedIn, null, "video/mp4", MediaSource.Upload, PostTypeClassifier.Video },
                // Chỉ ảnh
                { "image-only", null, SocialPlatform.Facebook, null, "image/png", MediaSource.Upload, PostTypeClassifier.Image },
                // Không media
                { "text-only", null, SocialPlatform.Facebook, null, null, MediaSource.Upload, PostTypeClassifier.Text },
            };
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classify_MatchesPriorityTable(
        string label,
        Guid? newsArticleId,
        SocialPlatform? platform,
        string? extraJson,
        string? mimeType,
        MediaSource source,
        string expected)
    {
        List<PostTypeClassifier.MediaSignal> media = [];
        if (mimeType is not null)
        {
            var fileName = source == MediaSource.Overlay && mimeType.StartsWith("video/")
                ? "clip-reels.mp4"
                : "file.bin";
            var alt = source == MediaSource.Overlay && mimeType.StartsWith("video/")
                ? "Reels video for demo"
                : null;
            media.Add(new PostTypeClassifier.MediaSignal(mimeType, source, fileName, alt));
        }

        var actual = PostTypeClassifier.Classify(newsArticleId, platform, extraJson, media);
        Assert.True(actual == expected, $"case '{label}': expected {expected}, got {actual}");
    }

    [Fact]
    public void News_Beats_ShortVideo()
    {
        var media = new List<PostTypeClassifier.MediaSignal>
        {
            new("video/mp4", MediaSource.Upload, "x.mp4", null)
        };
        var actual = PostTypeClassifier.Classify(
            Guid.NewGuid(), SocialPlatform.TikTok, null, media);
        Assert.Equal(PostTypeClassifier.News, actual);
    }

    [Fact]
    public void ReelsRequested_WithoutVideo_IsImageOrText()
    {
        var images = new List<PostTypeClassifier.MediaSignal>
        {
            new("image/jpeg", MediaSource.Upload, "a.jpg", null)
        };
        Assert.Equal(
            PostTypeClassifier.Image,
            PostTypeClassifier.Classify(null, SocialPlatform.LinkedIn, """{"reelsRequested":true}""", images));

        Assert.Equal(
            PostTypeClassifier.Text,
            PostTypeClassifier.Classify(null, SocialPlatform.LinkedIn, """{"reelsRequested":true}""", []));
    }
}
