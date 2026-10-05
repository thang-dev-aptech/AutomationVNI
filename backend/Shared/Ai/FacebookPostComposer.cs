using System.Text;

namespace Backend.Shared.Ai;

/// <summary>
/// Ghép bài Facebook sẵn đăng (tiêu đề + caption + CTA + hashtag). Dùng chung cho luồng Full AI
/// (GenerationJobPipelineService.MapAiResult) và caption sinh từ ảnh (MediaIntelligenceService) để hai
/// nơi luôn ra cùng một cấu trúc. Hành vi được khoá bởi FullAiPostCompositionTests.
/// </summary>
public static class FacebookPostComposer
{
    public const string DefaultCta = "Inbox ngay để được tư vấn chi tiết nhé 💬";
    public const int MaxHashtags = 8;

    /// <summary>
    /// Dòng tiêu đề: Facebook TỰ phóng to dòng đầu tiên khi nó ngắn (~≤ 80 ký tự) và có dòng trống
    /// ngăn cách với thân bài. Nội dung bài (text thuần) không có in đậm/cỡ chữ — chữ to nổi bật chỉ
    /// đến từ mẹo "dòng đầu ngắn + dòng trống" này. bannerHeadline (≤ 8 từ) là dòng lý tưởng cho việc đó.
    /// </summary>
    public const int MaxTitleLineChars = 80;

    /// <summary>CTA của AI nếu có, ngược lại CTA mặc định của ngữ cảnh (khác null kể cả rỗng thì giữ nguyên).</summary>
    public static string ResolveCta(string? aiCta, string? fallbackCta)
        => string.IsNullOrWhiteSpace(aiCta) ? (fallbackCta?.Trim() ?? DefaultCta) : aiCta.Trim();

    public static string Compose(
        string? titleLine, string? caption, string cta, IReadOnlyList<string> hashtags)
    {
        var body = (caption ?? string.Empty).Trim();
        var sb = new StringBuilder();

        // Chỉ ghép tiêu đề khi nó đủ ngắn để Facebook phóng to, và caption chưa tự mở đầu bằng nó
        // (tránh lặp khi model đã đưa headline vào ngay đầu caption).
        var title = NormalizeTitleLine(titleLine);
        if (title.Length > 0
            && title.Length <= MaxTitleLineChars
            && !StartsWithIgnoreCase(body, title))
        {
            sb.Append(title);
            if (body.Length > 0) sb.Append("\n\n");
        }

        sb.Append(body);

        if (!string.IsNullOrWhiteSpace(cta) && !ContainsIgnoreCase(body, cta))
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(cta.Trim());
        }

        if (hashtags.Count > 0)
        {
            var tagLine = string.Join(' ', hashtags);
            if (!ContainsIgnoreCase(sb.ToString(), tagLine) && !hashtags.All(t => ContainsIgnoreCase(sb.ToString(), t)))
            {
                sb.Append("\n\n");
                sb.Append(tagLine);
            }
        }

        return sb.ToString().Trim();
    }

    public static List<string> NormalizeHashtags(IEnumerable<string>? tags)
    {
        var list = new List<string>();
        if (tags is null) return list;
        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var t = raw.Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            t = t.Replace(' ', '_');
            if (!list.Contains(t, StringComparer.OrdinalIgnoreCase))
                list.Add(t);
        }
        return list.Take(MaxHashtags).ToList();
    }

    /// <summary>Tách chuỗi hashtag của PageContext ("#a #b, #c") thành danh sách rồi chuẩn hoá.</summary>
    public static List<string> NormalizeHashtagText(string? text)
        => NormalizeHashtags((text ?? string.Empty)
            .Split([' ', ',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Chuẩn hoá dòng tiêu đề: gộp về 1 dòng, VIẾT HOA (chữ to nổi bật), bỏ dấu chấm câu thừa ở cuối.
    /// ToUpperInvariant xử lý đúng nguyên âm tiếng Việt có dấu (ư→Ư, ơ→Ơ, ế→Ế...) vì chúng là ký tự
    /// tổ hợp sẵn, không bị vỡ như mẹo "in đậm" bằng ký tự Unicode toán học.
    /// </summary>
    public static string NormalizeTitleLine(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        // Gộp mọi khoảng trắng (kể cả xuống dòng) thành 1 space — tiêu đề phải nằm gọn 1 dòng.
        var line = string.Join(' ', title.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

        line = line.TrimEnd('.', ',', ';', ':', '!', '。', ' ');
        return line.ToUpperInvariant();
    }

    private static bool ContainsIgnoreCase(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithIgnoreCase(string text, string prefix)
        => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
