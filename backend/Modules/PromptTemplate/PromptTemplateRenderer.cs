using System.Text.RegularExpressions;

namespace Backend.Modules.PromptTemplate;

/// <summary>
/// Thay thế placeholder {{variable}} trong Body template bằng giá trị thực (từ Post/PageContext/Category).
/// Placeholder không có giá trị → thay bằng chuỗi rỗng để không lọt "{{x}}" vào prompt gửi LLM.
/// </summary>
public static partial class PromptTemplateRenderer
{
    /// <summary>Các biến hỗ trợ — hiển thị gợi ý trên UI (endpoint /api/prompttemplate/variables).</summary>
    public static readonly IReadOnlyList<PromptVariableInfo> AvailableVariables =
    [
        new("title", "Tiêu đề bài viết"),
        new("category", "Tên danh mục"),
        new("brand", "Tên thương hiệu (PageContext)"),
        new("tone", "Giọng văn thương hiệu"),
        new("audience", "Đối tượng mục tiêu"),
        new("objective", "Mục tiêu/goal của bài"),
        new("cta", "Call to action mặc định"),
        new("hashtags", "Hashtag mặc định"),
        new("caption", "(ảnh) Nội dung bài đã sinh"),
        new("imagePrompt", "(ảnh) Gợi ý prompt ảnh từ AI")
    ];

    public static string Render(string? body, IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        return PlaceholderPattern().Replace(body, match =>
        {
            var key = match.Groups[1].Value.Trim();
            return values.TryGetValue(key, out var v) ? (v ?? string.Empty) : string.Empty;
        }).Trim();
    }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}")]
    private static partial Regex PlaceholderPattern();
}

public record PromptVariableInfo(string Name, string Description);
