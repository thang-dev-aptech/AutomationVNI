using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Backend.Shared.Text;

/// <summary>
/// Xử lý văn bản tiếng Việt dùng chung: bỏ dấu, tách token, gỡ HTML, chuẩn hoá để băm.
/// Tách ra từ MediaIntelligenceService.Tokenize để ContentCrawl (SimHash) dùng lại cùng
/// một cách tách từ — hai nơi tách khác nhau thì điểm tương đồng không so sánh được với nhau.
/// </summary>
public static partial class VietnameseTextHelper
{
    /// <summary>
    /// Token CÓ thứ tự và GIỮ token lặp — dùng cho SimHash/bigram, nơi tần suất là trọng số.
    /// Bỏ dấu (đ giữ nguyên vì FormD không tách đ), tách theo ký tự không phải chữ/số,
    /// bỏ token dưới 2 ký tự.
    /// </summary>
    public static List<string> TokenList(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return NonWordPattern()
            .Split(StripDiacritics(input))
            .Where(x => x.Length >= 2)
            .ToList();
    }

    /// <summary>
    /// Tập token duy nhất — hành vi Y HỆT MediaIntelligenceService.Tokenize trước khi tách.
    /// Dùng cho Jaccard và so khớp từ khoá, nơi chỉ quan tâm có mặt hay không.
    /// </summary>
    public static HashSet<string> TokenSet(string? input)
        => TokenList(input).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Chữ thường + bỏ dấu thanh/dấu mũ. Không đụng tới khoảng trắng và dấu câu.</summary>
    public static string StripDiacritics(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var normalized = input.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return string.Concat(normalized.Where(c =>
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
    }

    /// <summary>
    /// Gỡ thẻ HTML và giải mã entity. Dành cho CDATA description của RSS — thứ đó là HTML
    /// HỎNG (&lt;a/&gt;&lt;img/&gt;&lt;/a&gt;&lt;/br&gt;) nên không parse bằng XML được, phải xử lý bằng regex.
    /// </summary>
    public static string StripHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var text = HtmlTagPattern().Replace(input, " ");
        text = WebUtility.HtmlDecode(text);
        return WhitespacePattern().Replace(text, " ").Trim();
    }

    /// <summary>Chuẩn hoá trước khi băm SHA-256: chữ thường + bỏ dấu + gộp khoảng trắng.</summary>
    public static string NormalizeForHash(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return WhitespacePattern().Replace(StripDiacritics(input), " ").Trim();
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWordPattern();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
