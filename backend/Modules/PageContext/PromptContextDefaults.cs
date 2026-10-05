using Backend.Data;
using Backend.Modules.SocialChannel;
using Backend.Shared.Ai;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.PageContext;

/// <summary>
/// Ngữ cảnh thương hiệu gửi AI cho một Page: PageContext nếu có; không có thì rơi về tên Page kênh
/// và các mặc định ổn định để prompt vẫn mạch lạc (không để {{brand}}/{{cta}} rỗng). Dùng chung cho
/// luồng Full AI (ResolvePromptContextAsync) và caption sinh từ ảnh để hai nơi cùng quy tắc fallback.
/// </summary>
public sealed record PromptContextDefaults(
    PageContextModel? PageContext,
    string Brand,
    string Tone,
    string Cta,
    string Hashtags,
    string Hotline,
    string Website,
    string BrandColors)
{
    public const string FallbackBrand = "Page của bạn";
    public const string FallbackTone = "thân thiện, rõ ràng, chuyên nghiệp, gần gũi như nói chuyện trên Facebook";
    public const string FallbackCategory = "Chung";

    /// <summary>true khi Brand lấy được từ PageContext hoặc tên Page thật (không phải chuỗi giữ chỗ).</summary>
    public bool HasRealBrand => !string.Equals(Brand, FallbackBrand, StringComparison.Ordinal);

    public static PromptContextDefaults From(PageContextModel? pageContext, string? channelName, string category)
    {
        var brand = FirstNonEmpty(pageContext?.BrandName, channelName, FallbackBrand)!;
        var tone = FirstNonEmpty(pageContext?.ToneOfVoice, FallbackTone)!;
        var cta = FirstNonEmpty(pageContext?.CtaText, FacebookPostComposer.DefaultCta)!;
        var hashtags = FirstNonEmpty(pageContext?.DefaultHashtags, BuildFallbackHashtags(category))!;

        // Hotline/website/màu thương hiệu chỉ lấy từ PageContext — không bịa, vì model phải in
        // đúng nguyên văn lên banner. Thiếu thì để rỗng và template tự bỏ dòng liên hệ.
        var hotline = pageContext?.Hotline?.Trim() ?? string.Empty;
        var website = FirstNonEmpty(pageContext?.Website, pageContext?.CtaUrl) ?? string.Empty;
        var brandColors = pageContext?.BrandColors?.Trim() ?? string.Empty;

        return new PromptContextDefaults(pageContext, brand, tone, cta, hashtags, hotline, website, brandColors);
    }

    /// <summary>
    /// Nạp PageContext (chưa xoá) và tên Page (chưa xoá) của đúng một kênh rồi áp fallback. Không có
    /// channelId ⇒ không đọc PageContext của Page nào, chỉ dùng mặc định chung.
    /// </summary>
    public static async Task<PromptContextDefaults> ResolveAsync(
        AppDbContext db, Guid? socialChannelId, string category, CancellationToken ct = default)
    {
        if (socialChannelId is not Guid channelId || channelId == Guid.Empty)
            return From(null, null, category);

        var pageContext = await db.Set<PageContextModel>().AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.SocialChannelId == channelId, ct);
        var channelName = await db.Set<SocialChannelModel>().AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == channelId)
            .Select(x => x.PageName)
            .FirstOrDefaultAsync(ct);
        return From(pageContext, channelName, category);
    }

    public static string BuildFallbackHashtags(string category)
    {
        var slug = new string(category
            .Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '/')
            .ToArray())
            .Trim()
            .Replace('/', ' ')
            .Replace(' ', '_');
        while (slug.Contains("__", StringComparison.Ordinal))
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        slug = slug.Trim('_');
        if (string.IsNullOrWhiteSpace(slug))
            return "#Facebook #Marketing #BanHang";
        return $"#{slug} #Facebook #Marketing #BanHang";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return null;
    }
}
