using Backend.Modules.MediaAsset;
using Backend.Shared.Text;

namespace Backend.Modules.Campaign.FullMedia;

/// <summary>Đối chiếu Keywords mô tả Vision với Tags/Description cũ của MediaAsset.</summary>
public static class TagConsistency
{
    /// <summary>
    /// true nếu không có dữ liệu cũ để so, hoặc có giao nhau token đáng kể.
    /// false khi cả hai phía có nội dung nhưng không trùng token nào (lệch hẳn).
    /// </summary>
    public static bool IsConsistent(VisionDescription description, MediaAssetModel media)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(media);

        var existing = CollectExistingTokens(media);
        if (existing.Count == 0) return true;

        var vision = CollectVisionTokens(description);
        if (vision.Count == 0) return true;

        return existing.Overlaps(vision);
    }

    private static HashSet<string> CollectExistingTokens(MediaAssetModel media)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kw in MediaIntelligenceService.ParseKeywords(media.Tags))
            AddTokens(set, kw);
        AddTokens(set, media.Description);
        AddTokens(set, media.AltText);
        return set;
    }

    private static HashSet<string> CollectVisionTokens(VisionDescription description)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kw in description.Keywords)
            AddTokens(set, kw);
        AddTokens(set, description.Topic);
        AddTokens(set, description.Setting);
        return set;
    }

    private static void AddTokens(HashSet<string> set, string? text)
    {
        foreach (var token in VietnameseTextHelper.TokenSet(text))
        {
            if (token.Length >= 2)
                set.Add(token);
        }
    }
}
