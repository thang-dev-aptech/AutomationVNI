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
/// Chỉ soi thứ kiểm chứng được bằng máy: NGÀY THÁNG, số, phần trăm, tiền. Không soi tên riêng —
/// tách tên tiếng Việt bằng regex sai nhiều hơn đúng, báo nhầm liên tục thì người dùng sẽ
/// bỏ qua cảnh báo, mà cảnh báo bị bỏ qua thì tệ hơn không có.
///
/// NGÀY THÁNG là loại nguy hiểm nhất với bản tin giáo dục và phải xử riêng. Đo thật: cho AI
/// viết một bài từ toàn văn 8.676 ký tự, nó bịa ra bốn mốc "10.8, 20.8, 21.8, 28.8" không có
/// ở bất kỳ đâu trong tư liệu. Phụ huynh đọc nhầm một mốc là lỡ hạn nộp hồ sơ thật.
///
/// Ngày phải so theo DẠNG CHUẨN vì báo viết mỗi nơi một kiểu: "10/8", "10.8", "ngày 10-8",
/// "10/08/2026". Nếu chỉ bóc cụm chữ số như trước thì "10.8" thành "108" còn "10/8" thành hai
/// cụm "10" và "8" — không bao giờ khớp nhau, cảnh báo nhầm hàng loạt.
/// </summary>
public static partial class CrawlContentGuard
{
    /// <summary>
    /// Bỏ qua số trần trụi dưới 32.
    ///
    /// Đây là ĐÁNH ĐỔI CÓ CHỦ Ý, không phải cho tiện. Ngày tháng viết rời rạc làm bộ soi số
    /// báo nhầm liên tục: tư liệu ghi "14 - 15.8" bị gỡ cả cụm, còn bài viết "14 và 15.8" chỉ
    /// gỡ được "15.8", để lại "14" trơ trọi rồi bị coi là bịa. Đo trên một bài đúng hoàn toàn:
    /// bốn lần báo nhầm chỉ vì chuyện này.
    ///
    /// Cái mất: số bịa trong khoảng 11–31 sẽ lọt. Cái được: 0 báo nhầm.
    /// Chọn vế sau vì cảnh báo bị bỏ qua thì tệ hơn không có cảnh báo — và ba loại nguy hiểm
    /// nhất (mốc thời gian, tiền, tỉ lệ phần trăm) đều đã có bộ soi riêng không phụ thuộc
    /// ngưỡng này.
    /// </summary>
    private const int IgnoreNumbersBelow = 32;

    public static List<string> FindUnsupportedFacts(string? generatedContent, string? sourceMaterial)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(generatedContent)) return warnings;

        var source = VietnameseTextHelper.NormalizeForHash(sourceMaterial);
        var sourceDigits = ExtractDigitGroups(sourceMaterial);
        var sourceDates = ExtractDates(sourceMaterial);

        foreach (var date in ExtractDates(generatedContent))
        {
            if (sourceDates.Contains(date)) continue;
            var (d, m) = (date / 100, date % 100);
            warnings.Add($"Mốc thời gian \"{d}/{m}\" không có trong tư liệu gốc");
        }

        // Gỡ ngày tháng khỏi văn bản TRƯỚC khi soi số. Bộ soi số bỏ dấu chấm phân cách nên
        // "10.8" thành "108", trong khi tư liệu viết "10/08" thành hai cụm "10" và "08" —
        // không bao giờ khớp. Đo thật trên một bài đúng hoàn toàn: bốn mốc hợp lệ bị báo
        // nhầm là số bịa. Ngày đã có bộ soi riêng ở trên nên ở đây gỡ ra là đủ.
        var contentNoDates = StripDates(generatedContent);

        foreach (var number in ExtractDigitGroups(contentNoDates).Distinct())
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

    /// <summary>
    /// Bóc ngày tháng về một số duy nhất ddMM để mọi cách viết đều so được với nhau.
    /// Nhận "10/8", "10.8", "10-8", "10/08/2026". Chỉ nhận ngày 1–31 và tháng 1–12 để khỏi
    /// nuốt nhầm những thứ như "18.5 điểm" hay tỉ số.
    /// </summary>
    /// <summary>Thay mọi cặp ngày/tháng bằng khoảng trắng để bộ soi số không đụng tới.</summary>
    private static string StripDates(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? ""
            : DateStripPattern().Replace(text, " ");

    private static HashSet<int> ExtractDates(string? text)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var m in DatePattern().Matches(text).Cast<Match>())
        {
            if (!int.TryParse(m.Groups[1].Value, out var day)) continue;
            if (!int.TryParse(m.Groups[2].Value, out var month)) continue;
            if (day is < 1 or > 31 || month is < 1 or > 12) continue;
            result.Add(day * 100 + month);
        }
        return result;
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

    /// <summary>
    /// Lookahead zero-width để các cặp ngày ĐƯỢC PHÉP CHỒNG NHAU.
    ///
    /// Bắt buộc, và đã trả giá: báo viết dải ngày kiểu "14 - 15.8". Regex thường khớp cặp
    /// (14,15) rồi nuốt luôn ký tự, bỏ sót "15.8" — bộ soi báo nhầm rằng bài bịa mốc 15/8
    /// trong khi tư liệu có. Cảnh báo nhầm còn tệ hơn không cảnh báo: người dùng sẽ bỏ qua
    /// hết, kể cả lần báo đúng.
    ///
    /// Cặp vô lý như (14,15) tự bị loại ở bước kiểm tháng ≤ 12.
    /// </summary>
    [GeneratedRegex(@"(?=\b(\d{1,2})\s*[/.\-]\s*(\d{1,2})\b)")]
    private static partial Regex DatePattern();

    /// <summary>Bản KHÔNG lookahead của DatePattern — dùng để cắt bỏ, nên cần nuốt ký tự thật.</summary>
    [GeneratedRegex(@"\b\d{1,2}\s*[/.\-]\s*\d{1,2}(\s*[/.\-]\s*\d{2,4})?\b")]
    private static partial Regex DateStripPattern();

    [GeneratedRegex(@"\d[\d.,]*")]
    private static partial Regex DigitGroupPattern();

    [GeneratedRegex(@"\d[\d.,]*\s*%")]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"\d[\d.,]*\s*(?:đồng|VNĐ|VND|tỷ|triệu|nghìn|USD|\$)", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyPattern();
}
