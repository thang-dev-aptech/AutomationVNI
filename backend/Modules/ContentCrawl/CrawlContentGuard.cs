using System.Text.RegularExpressions;
using Backend.Shared.Text;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Soi caption AI sinh ra, tìm dữ kiện KHÔNG có trong tư liệu gốc.
///
/// Vì sao cần: tư liệu RSS chỉ ~50 từ, mà bài Facebook cần 60–110 từ. Ràng buộc trong prompt
/// làm GIẢM bịa chứ không triệt tiêu được. Một con số học phí bịa trên fanpage trường học là
/// chuyện có thật ngoài đời, nên phải có một lớp kiểm tra tất định, không phụ thuộc model.
///
/// Chỉ soi thứ kiểm chứng được bằng máy: số, phần trăm, năm, tiền. Không soi tên riêng —
/// tách tên tiếng Việt bằng regex sai nhiều hơn đúng, báo nhầm liên tục thì người dùng sẽ
/// bỏ qua cảnh báo, mà cảnh báo bị bỏ qua thì tệ hơn không có.
/// </summary>
public static partial class CrawlContentGuard
{
    /// <summary>Số nhỏ 1..10 hay xuất hiện tự nhiên ("3 điều cần biết") nên bỏ qua để đỡ ồn.</summary>
    private const int IgnoreNumbersBelow = 11;

    public static List<string> FindUnsupportedFacts(string? generatedContent, string? sourceMaterial)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(generatedContent)) return warnings;

        var source = VietnameseTextHelper.NormalizeForHash(sourceMaterial);
        var sourceDigits = ExtractDigitGroups(sourceMaterial);

        foreach (var number in ExtractDigitGroups(generatedContent).Distinct())
        {
            if (sourceDigits.Contains(number)) continue;
            if (long.TryParse(number, out var value) && value < IgnoreNumbersBelow) continue;
            warnings.Add($"Con số \"{number}\" không có trong tư liệu gốc");
        }

        foreach (var percent in PercentPattern().Matches(generatedContent)
                     .Select(m => m.Value.Trim()).Distinct())
        {
            if (source.Contains(VietnameseTextHelper.NormalizeForHash(percent))) continue;
            warnings.Add($"Tỉ lệ \"{percent}\" không có trong tư liệu gốc");
        }

        foreach (var money in MoneyPattern().Matches(generatedContent)
                     .Select(m => m.Value.Trim()).Distinct())
        {
            if (source.Contains(VietnameseTextHelper.NormalizeForHash(money))) continue;
            warnings.Add($"Số tiền \"{money}\" không có trong tư liệu gốc");
        }

        return warnings.Distinct().Take(10).ToList();
    }

    /// <summary>Lấy các cụm chữ số, bỏ dấu phân cách nghìn để "700.000" và "700000" so được với nhau.</summary>
    private static HashSet<string> ExtractDigitGroups(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return DigitGroupPattern().Matches(text)
            .Select(m => m.Value.Replace(".", "").Replace(",", "").Replace(" ", ""))
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\d[\d.,]*")]
    private static partial Regex DigitGroupPattern();

    [GeneratedRegex(@"\d[\d.,]*\s*%")]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"\d[\d.,]*\s*(?:đồng|VNĐ|VND|tỷ|triệu|nghìn|USD|\$)", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyPattern();
}
