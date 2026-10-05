using System.Reflection;
using Backend.Modules.GenerationJob;
using Backend.Shared.Ai;
using Xunit;

namespace Backend.Tests.Modules.GenerationJob;

/// <summary>
/// Regression R-029 (image-caption-fullai-format d): đầu ra Post.Content của luồng Full AI KHÔNG đổi khi
/// tách ComposeFacebookPost/NormalizeHashtags ra helper dùng chung. Giá trị mong đợi được ghi từ hành vi
/// TRƯỚC khi tách; MapAiResult là hàm duy nhất ghép Content nên gọi thẳng nó qua reflection.
/// </summary>
public sealed class FullAiPostCompositionTests
{
    private const string DefaultCta = "Inbox ngay để được tư vấn chi tiết nhé 💬";

    private static TextGenerationJobOutput Map(
        AiTextGenerationResult ai, string? requestCta = null, bool newsStyle = false)
    {
        var method = typeof(GenerationJobPipelineService).GetMethod(
            "MapAiResult", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MapAiResult không còn là private static");
        var request = new AiTextGenerationRequest { CtaText = requestCta };
        return (TextGenerationJobOutput)method.Invoke(null, [ai, request, newsStyle])!;
    }

    private static AiTextGenerationResult Ai(
        string caption, string headline = "", string cta = "", params string[] hashtags)
        => new() { Caption = caption, BannerHeadline = headline, Cta = cta, Hashtags = [.. hashtags] };

    [Fact]
    public void FullPost_TitleBodyCtaAndNormalizedHashtags()
    {
        var output = Map(Ai(
            "Hook 🌞\n• ý 1\n• ý 2", "Khuyến mãi mùa hè.", "Inbox ngay 💬",
            "summer", "#Sale", "sale", "kem chống nắng"));

        Assert.Equal(
            "KHUYẾN MÃI MÙA HÈ\n\nHook 🌞\n• ý 1\n• ý 2\n\nInbox ngay 💬\n\n#summer #Sale #kem_chống_nắng",
            output.Content);
        Assert.Equal(["#summer", "#Sale", "#kem_chống_nắng"], output.Hashtags);
        Assert.Equal("Inbox ngay 💬", output.Cta);
    }

    [Fact]
    public void MissingAiCta_UsesRequestCtaTrimmed()
    {
        var output = Map(Ai("Nội dung", hashtags: "a"), requestCta: "  Gọi 1900 ngay  ");

        Assert.Equal("Nội dung\n\nGọi 1900 ngay\n\n#a", output.Content);
        Assert.Equal("Gọi 1900 ngay", output.Cta);
    }

    [Fact]
    public void MissingAiAndRequestCta_UsesTheDefaultCta()
    {
        var output = Map(Ai("Nội dung"));

        Assert.Equal($"Nội dung\n\n{DefaultCta}", output.Content);
    }

    [Fact]
    public void EmptyRequestCta_IsKeptEmptyAndOmitted()
    {
        // Hành vi hiện tại: chuỗi rỗng (khác null) KHÔNG rơi về CTA mặc định.
        var output = Map(Ai("Nội dung"), requestCta: "");

        Assert.Equal("Nội dung", output.Content);
    }

    [Fact]
    public void TitleLongerThan80Chars_IsOmitted()
    {
        var longTitle = string.Join(' ', Enumerable.Repeat("tiêu", 20));

        var output = Map(Ai("Thân bài", longTitle, "CTA"));

        Assert.True(longTitle.Length > 80);
        Assert.Equal("Thân bài\n\nCTA", output.Content);
    }

    [Fact]
    public void TitleAlreadyStartingTheBody_IsNotRepeated()
    {
        var output = Map(Ai("giảm 50% hôm nay", "Giảm 50%", "CTA"));

        Assert.Equal("giảm 50% hôm nay\n\nCTA", output.Content);
    }

    [Fact]
    public void TitleKeepsVietnameseUppercaseAndTrimsTrailingPunctuation()
    {
        var output = Map(Ai("Thân", "  ưu đãi \n ơ!!  ", "CTA"));

        Assert.Equal("ƯU ĐÃI Ơ\n\nThân\n\nCTA", output.Content);
    }

    [Fact]
    public void CtaAlreadyInsideTheBody_IsNotAppended()
    {
        var output = Map(Ai("Xem ngay. Inbox ngay 💬 nhé", cta: "inbox ngay 💬"));

        Assert.Equal("Xem ngay. Inbox ngay 💬 nhé", output.Content);
    }

    [Fact]
    public void HashtagsAlreadyInsideTheBody_AreNotAppended()
    {
        var output = Map(Ai("Chốt #b rồi #a", cta: "CTA", hashtags: ["#a", "#b"]));

        Assert.Equal("Chốt #b rồi #a\n\nCTA", output.Content);
        Assert.Equal(["#a", "#b"], output.Hashtags);
    }

    [Fact]
    public void MoreThanEightHashtags_AreCappedAtEight()
    {
        var tags = Enumerable.Range(1, 10).Select(i => $"t{i}").ToArray();

        var output = Map(Ai("Nội dung", cta: "CTA", hashtags: tags));

        Assert.Equal(8, output.Hashtags.Count);
        Assert.EndsWith("\n\n#t1 #t2 #t3 #t4 #t5 #t6 #t7 #t8", output.Content);
    }

    [Fact]
    public void NewsStyle_DropsCtaAndHashtagLinesAndSalesNoise()
    {
        var output = Map(
            Ai("Bản tin\nNội dung\n#tag #tag2\nInbox ngay để được tư vấn", "Tiêu đề", "CTA", "x"),
            newsStyle: true);

        Assert.Equal("Bản tin\nNội dung", output.Content);
        Assert.Empty(output.Hashtags);
        Assert.Equal(string.Empty, output.Cta);
    }
}
