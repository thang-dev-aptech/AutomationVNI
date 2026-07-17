using System.Text.Json;
using Backend.Modules.GenerationJob.Enums;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Shared.Ai;

namespace Backend.Modules.GenerationJob;

public static class MockTextGenerator
{
    public static MockTextGenerationResult Generate(PostModel post)
    {
        var brandHint = post.Title.Trim();
        var flowLabel = post.GenerationFlow == GenerationFlow.RAG ? "RAG" : "Full AI";

        return new MockTextGenerationResult
        {
            Content = $"""
                🚀 {brandHint}

                Đây là nội dung mock sinh bởi pipeline {flowLabel} của VNI Automation.
                Bài viết được tạo tự động để kiểm thử luồng generation job trước khi tích hợp LLM thật.

                ✨ Điểm nổi bật:
                • Chuẩn hóa giọng văn thương hiệu
                • Tối ưu cho mạng xã hội
                • Sẵn sàng gửi duyệt sau khi sinh xong

                #{SanitizeTag(brandHint)} #VNIAutomation #MarketingAI
                """,
            Hashtags = ["#VNIAutomation", "#MarketingAI", $"#{SanitizeTag(brandHint)}"],
            Cta = "Khám phá thêm tại website VNI — liên hệ team marketing để biết chi tiết.",
            ImagePrompt = $"Professional social media marketing visual for '{brandHint}', clean modern style, brand colors, no text overlay"
        };
    }

    public static AiTextGenerationResult GenerateFromRequest(AiTextGenerationRequest request)
    {
        var title = string.IsNullOrWhiteSpace(request.Title) ? "VNI Automation" : request.Title.Trim();
        var mock = new MockTextGenerationResult
        {
            Content = $"🚀 {title}\n\nMock preview — cấu hình ApiKey để dùng AI thật.\n\n#{SanitizeTag(title)} #VNIAutomation",
            Hashtags = ["#VNIAutomation", $"#{SanitizeTag(title)}"],
            Cta = request.CtaText?.Trim() ?? "Khám phá thêm tại website VNI.",
            ImagePrompt = $"Professional social media visual for '{title}', modern clean style, no text overlay"
        };

        return new AiTextGenerationResult
        {
            Caption = mock.Content,
            Hashtags = mock.Hashtags,
            Cta = mock.Cta,
            BannerHeadline = title,
            BannerSubheadline = request.BrandContext ?? "VNI Automation",
            BannerCta = mock.Cta,
            ImagePrompt = mock.ImagePrompt
        };
    }

    public static string ToJson(MockTextGenerationResult result)
        => JsonSerializer.Serialize(result, JsonOptions);

    public static string ToJson(TextGenerationJobOutput result)
        => JsonSerializer.Serialize(result, JsonOptions);

    private static string SanitizeTag(string input)
    {
        var tag = new string(input.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(tag) ? "VNI" : tag[..Math.Min(tag.Length, 30)];
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
}
