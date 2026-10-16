using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Backend.Modules.PageContext;

/// <summary>
/// Hồ sơ page dùng để chấm điểm Full Media (tiếng Việt).
/// Chỉ BrandName, ToneOfVoice, CtaText, Topics, Exclusions — không màu/hotline/website/logo.
/// </summary>
public static partial class PageProfileSnapshot
{
    public static PageProfile Build(PageContextModel p)
    {
        ArgumentNullException.ThrowIfNull(p);

        var brand = NormalizeWhitespace(p.BrandName);
        var tone = NormalizeWhitespace(p.ToneOfVoice);
        var cta = NormalizeWhitespace(p.CtaText);
        var topics = NormalizeWhitespace(p.Topics);
        var exclusions = NormalizeWhitespace(p.Exclusions);

        var lines = new List<string>(5);
        AppendLine(lines, "Thương hiệu", brand);
        AppendLine(lines, "Giọng điệu", tone);
        AppendLine(lines, "CTA", cta);
        AppendLine(lines, "Chủ đề chính", topics);
        AppendLine(lines, "Nội dung không đăng", exclusions);

        var text = string.Join('\n', lines);
        var hashInput = string.Join('\n', new[]
        {
            $"BrandName={brand}",
            $"ToneOfVoice={tone}",
            $"CtaText={cta}",
            $"Topics={topics}",
            $"Exclusions={exclusions}",
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))
            .ToLowerInvariant();

        var isScoreable = topics.Length > 0 || tone.Length > 0;
        return new PageProfile(text, hash, isScoreable);
    }

    private static void AppendLine(List<string> lines, string label, string value)
    {
        if (value.Length == 0) return;
        lines.Add($"{label}: {value}");
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return WhitespacePattern().Replace(value.Trim(), " ");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}

public sealed record PageProfile(string Text, string Hash, bool IsScoreable);
