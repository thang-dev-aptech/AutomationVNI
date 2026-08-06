using System.Collections.Concurrent;

namespace Backend.Modules.ContentCrawl;

/// <summary>Page đang được tick trong ô chọn của một tin.</summary>
public sealed class PageSelection
{
    public required Guid ArticleId { get; init; }
    public required DateTime CreatedAt { get; init; }
    public HashSet<Guid> ChannelIds { get; } = [];
}

/// <summary>
/// Giữ tạm trạng thái tick page giữa các lần bấm nút.
///
/// Vì sao trong bộ nhớ mà không xuống DB: đây là trạng thái của MỘT thao tác đang dở trên
/// điện thoại, sống vài chục giây. Đẻ thêm bảng cho nó là phình lược đồ để lưu thứ mà mất
/// đi cũng chẳng sao — restart giữa lúc đang tick thì gõ lại /dang, hết.
///
/// Không nhét vào callback_data được: Telegram giới hạn 64 byte, chọn 5 page trong 15 page
/// là đã tràn.
/// </summary>
public class CrawlTelegramSelectionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, PageSelection> _items = new();

    public static string Key(long chatId, string shortId) => $"{chatId}:{shortId}";

    public PageSelection Start(long chatId, string shortId, Guid articleId, IEnumerable<Guid> preset)
    {
        Prune();
        var sel = new PageSelection { ArticleId = articleId, CreatedAt = DateTime.UtcNow };
        foreach (var id in preset) sel.ChannelIds.Add(id);
        _items[Key(chatId, shortId)] = sel;
        return sel;
    }

    public PageSelection? Get(long chatId, string shortId)
        => _items.TryGetValue(Key(chatId, shortId), out var s) ? s : null;

    public void Drop(long chatId, string shortId) => _items.TryRemove(Key(chatId, shortId), out _);

    /// <summary>
    /// Dọn bản ghi quá hạn. Không dọn thì mỗi lần mở ô chọn rồi bỏ ngang là một bản ghi nằm
    /// lại vĩnh viễn — rò rỉ chậm nhưng không bao giờ tự khỏi.
    /// </summary>
    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Ttl;
        foreach (var (k, v) in _items)
            if (v.CreatedAt < cutoff) _items.TryRemove(k, out _);
    }
}
