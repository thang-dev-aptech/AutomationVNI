using System.Text.RegularExpressions;

namespace Backend.Modules.Crm.Customers;

/// <summary>
/// Trích/chuẩn hoá số di động VN → E.164 (+84…). Không nhận số cố định, năm, số tiền.
/// </summary>
public static partial class VietnamesePhoneNormalizer
{
    // Ứng viên: 0xxx hoặc +84/84, sau đó đầu số di động 3/5/7/8/9 + đủ 8 số còn lại.
    [GeneratedRegex(
        @"(?<!\d)(?:\+?84|0)\s*[35789](?:[\s.\-]*\d){8}(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CandidateRegex();

    /// <summary>Chuẩn hoá một chuỗi đã biết là số (hoặc null nếu không phải di động VN).</summary>
    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length >= 11)
            digits = "0" + digits[2..];
        if (digits.Length != 10 || digits[0] != '0') return null;
        var second = digits[1];
        if (second is not ('3' or '5' or '7' or '8' or '9')) return null;
        // Loại số cố định kiểu 02x đã bị loại vì second không phải 2.
        return "+84" + digits[1..];
    }

    /// <summary>Trích mọi số di động hợp lệ từ văn bản (không tự lưu hồ sơ).</summary>
    public static IReadOnlyList<(string E164, string RawMatched)> ExtractFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<(string, string)>();

        // Loại nhanh chuỗi tiền / năm trước khi match.
        if (MoneyOrYearOnly(text)) return Array.Empty<(string, string)>();

        var results = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in CandidateRegex().Matches(text))
        {
            var raw = match.Value;
            // Bỏ qua nếu match nằm trong cụm tiền (có 'đ' hoặc dấu chấm nhóm nghìn liền kề).
            if (LooksLikeMoneyContext(text, match.Index, match.Length)) continue;

            var e164 = TryNormalize(raw);
            if (e164 is null) continue;
            if (seen.Add(e164))
                results.Add((e164, raw.Trim()));
        }

        return results;
    }

    private static bool MoneyOrYearOnly(string text)
    {
        var t = text.Trim();
        if (YearOnlyRegex().IsMatch(t)) return true;
        if (t.Contains('đ', StringComparison.OrdinalIgnoreCase)
            || t.Contains("vnd", StringComparison.OrdinalIgnoreCase))
        {
            // Nếu toàn bộ chuỗi là tiền + số → bỏ; nếu có chữ khác vẫn cho Extract lọc từng match.
            var withoutMoney = MoneyTokenRegex().Replace(t, " ");
            return string.IsNullOrWhiteSpace(withoutMoney)
                   || !CandidateRegex().IsMatch(withoutMoney);
        }

        return false;
    }

    private static bool LooksLikeMoneyContext(string text, int index, int length)
    {
        var start = Math.Max(0, index - 3);
        var end = Math.Min(text.Length, index + length + 3);
        var window = text[start..end];
        return window.Contains('đ', StringComparison.OrdinalIgnoreCase)
               || window.Contains("vnd", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^\s*20\d{2}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex YearOnlyRegex();

    [GeneratedRegex(@"[\d.]+(?:\s*)(?:đ|vnd|VND)", RegexOptions.CultureInvariant)]
    private static partial Regex MoneyTokenRegex();
}
