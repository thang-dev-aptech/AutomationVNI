using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Backend.Shared.Text;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Vân tay của một mẩu nội dung: hash tuyệt đối + SimHash + 4 lát 16-bit.
///
/// LƯU Ý VỀ BAND: band KHÔNG được dùng để lấy ứng viên. Ý tưởng ban đầu là dựa vào chuồng
/// bồ câu (hai vân tay cách ≤3 bit chắc chắn trùng ít nhất một lát) để khỏi quét cả bảng,
/// nhưng đo trên 39.060 cặp feed thật thì CHỈ 23 cặp có band chung, và KHÔNG cặp trùng thật
/// nào có. Lý do: tóm tắt tiếng Việt ~50 từ quá ngắn, cặp trùng thật vẫn cách nhau d∈[12,21],
/// vượt xa mức ≤3 mà bảo đảm chuồng bồ câu đòi hỏi.
///
/// Nên cách lấy ứng viên là quét cửa sổ thời gian có chặn trần (xem ContentDedupService) —
/// không mất recall, và ở quy mô vài nghìn dòng thì nhanh hơn cả việc bắn 4 câu query.
/// Cột band vẫn giữ trong DB: miễn phí, đã migrate, và nếu sau này kho phình to thì đổi sang
/// 8 lát 8-bit (bảo đảm tới d≤7) chỉ cần sửa code, khỏi đụng schema.
/// </summary>
public sealed record ContentFingerprint(
    string ContentHash, long SimHash, int Band0, int Band1, int Band2, int Band3);

/// <summary>
/// SimHash 64-bit cho tin tiếng Việt ngắn.
///
/// CẢNH BÁO QUAN TRỌNG: hàm băm ở đây là FNV-1a tự cài, KHÔNG được thay bằng
/// string.GetHashCode(). .NET randomize GetHashCode theo từng tiến trình, nên vân tay
/// sẽ đổi sau mỗi lần restart backend — chống trùng chết lặng lẽ, không log, không lỗi,
/// và chỉ lộ ra khi kho bài đã đầy tin trùng. Đây đúng là cách tính năng này hay hỏng.
/// </summary>
public static partial class SimHashCalculator
{
    private const int Bits = 64;

    public static ContentFingerprint Compute(string? title, string? summary)
    {
        var text = $"{StripOutletPrefix(title)} {StripOutletPrefix(summary)}";
        var simHash = ComputeSimHash(text);
        return new ContentFingerprint(
            ContentHash: ComputeContentHash(title, summary),
            SimHash: simHash,
            Band0: (int)((ulong)simHash & 0xFFFF),
            Band1: (int)(((ulong)simHash >> 16) & 0xFFFF),
            Band2: (int)(((ulong)simHash >> 32) & 0xFFFF),
            Band3: (int)(((ulong)simHash >> 48) & 0xFFFF));
    }

    /// <summary>SHA-256 của (tiêu đề + tóm tắt) đã chuẩn hoá — bắt bài crawl lại y nguyên.</summary>
    public static string ComputeContentHash(string? title, string? summary)
    {
        var normalized = VietnameseTextHelper.NormalizeForHash($"{title}\n{summary}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Đặc trưng = unigram (trọng số 1) ∪ bigram liền kề (trọng số 2).
    /// Bigram là bắt buộc: tin giáo dục dùng chung vốn từ rất hẹp ("học sinh", "giáo dục",
    /// "tuyển sinh"), unigram trên tóm tắt 50 từ đụng nhau loạn xạ.
    /// </summary>
    public static long ComputeSimHash(string? text)
    {
        var tokens = VietnameseTextHelper.TokenList(text);
        if (tokens.Count == 0) return 0;

        var vector = new int[Bits];
        AccumulateAll(vector, tokens, weight: 1);
        for (var i = 0; i + 1 < tokens.Count; i++)
            Accumulate(vector, Fnv1a64($"{tokens[i]} {tokens[i + 1]}"), weight: 2);

        ulong result = 0;
        for (var i = 0; i < Bits; i++)
            if (vector[i] > 0) result |= 1UL << i;
        return unchecked((long)result);
    }

    private static void AccumulateAll(int[] vector, List<string> tokens, int weight)
    {
        foreach (var token in tokens)
            Accumulate(vector, Fnv1a64(token), weight);
    }

    private static void Accumulate(int[] vector, ulong hash, int weight)
    {
        for (var i = 0; i < Bits; i++)
            vector[i] += (hash & (1UL << i)) != 0 ? weight : -weight;
    }

    /// <summary>FNV-1a 64-bit — tất định qua mọi tiến trình, mọi máy, mọi phiên bản .NET.</summary>
    private static ulong Fnv1a64(string value)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }

    public static int HammingDistance(long a, long b)
        => System.Numerics.BitOperations.PopCount(unchecked((ulong)(a ^ b)));

    /// <summary>Chồng lấp token giữa hai tiêu đề. Bắt ca "cùng tin, khác báo, đổi tít".</summary>
    public static double TitleJaccard(string? a, string? b)
    {
        var setA = VietnameseTextHelper.TokenSet(StripOutletPrefix(a));
        var setB = VietnameseTextHelper.TokenSet(StripOutletPrefix(b));
        if (setA.Count == 0 || setB.Count == 0) return 0;
        var intersection = setA.Intersect(setB, StringComparer.OrdinalIgnoreCase).Count();
        var union = setA.Count + setB.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    /// <summary>
    /// Bỏ tiền tố toà soạn "(Dân trí) - ", "(VnExpress) - "… Không bỏ thì mọi bài cùng báo
    /// đều được cộng thêm một cụm token giống nhau, thổi phồng độ tương đồng.
    /// </summary>
    public static string StripOutletPrefix(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return OutletPrefixPattern().Replace(text.Trim(), string.Empty);
    }

    [GeneratedRegex(@"^\(.{1,25}\)\s*[-–—]\s*")]
    private static partial Regex OutletPrefixPattern();
}
