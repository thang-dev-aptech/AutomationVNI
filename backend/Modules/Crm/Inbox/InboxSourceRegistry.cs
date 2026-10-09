using Backend.Modules.SocialChannel.Enums;

namespace Backend.Modules.Crm.Inbox;

/// <summary>
/// Một nguồn hội thoại trong hộp thư (chip ở đầu danh sách). Là NƠI DUY NHẤT định nghĩa nguồn:
/// thêm nguồn mới (vd. Zalo) = thêm giá trị SocialPlatform + một mục ở đây + một logo ở PlatformLogo (frontend).
/// </summary>
public sealed record InboxSource(
    string Key,
    string Label,
    SocialPlatform Platform,
    bool IncludesMessages,
    bool IncludesComments,
    int SortOrder);

public interface IInboxSourceRegistry
{
    IReadOnlyList<InboxSource> All { get; }
    InboxSource? Find(string? key);
}

public sealed class DefaultInboxSourceRegistry : IInboxSourceRegistry
{
    private static readonly IReadOnlyList<InboxSource> Defaults =
    [
        // Phải khớp resolveInboxSource của SourceBadge (frontend).
        new("messenger", "Messenger", SocialPlatform.Facebook, IncludesMessages: true, IncludesComments: false, SortOrder: 1),
        new("facebook", "Facebook", SocialPlatform.Facebook, IncludesMessages: false, IncludesComments: true, SortOrder: 2),
        new("instagram", "Instagram", SocialPlatform.Instagram, IncludesMessages: true, IncludesComments: true, SortOrder: 3),
    ];

    public IReadOnlyList<InboxSource> All => Defaults;

    public InboxSource? Find(string? key)
        => string.IsNullOrWhiteSpace(key)
            ? null
            : Defaults.FirstOrDefault(x => string.Equals(x.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
}
