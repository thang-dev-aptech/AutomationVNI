namespace Backend.Modules.MediaCaption;

/// <summary>
/// Đánh dấu ảnh đang được sinh caption bằng tay (single generate) để worker không gọi AI lần hai cho
/// cùng ảnh. Chỉ trong tiến trình: app chạy 1 instance (SQLite là provider duy nhất) nên không cần
/// bền qua DB; process chết thì dấu tự mất, không kẹt. Gỡ trong Dispose (finally) kể cả khi lỗi/huỷ.
/// </summary>
public static class CaptionInFlight
{
    private sealed class Entry
    {
        public int Count;
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, Entry> Active = [];

    public static IDisposable Begin(Guid mediaId)
    {
        lock (Gate)
        {
            if (!Active.TryGetValue(mediaId, out var entry)) Active[mediaId] = entry = new Entry();
            entry.Count++;
            return new Handle(mediaId, entry);
        }
    }

    /// <summary>Chờ lần sinh tay hiện tại của ảnh kết thúc; không có thì trả ngay. Quá hạn cũng trả
    /// (không chặn worker vô hạn nếu AI treo).</summary>
    public static async Task WaitAsync(Guid mediaId, TimeSpan timeout, CancellationToken ct)
    {
        Task? pending;
        lock (Gate) pending = Active.TryGetValue(mediaId, out var entry) ? entry.Done.Task : null;
        if (pending is null) return;
        try { await pending.WaitAsync(timeout, ct); }
        catch (TimeoutException) { }
    }

    private sealed class Handle(Guid mediaId, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            lock (Gate)
            {
                if (--entry.Count > 0) return;
                Active.Remove(mediaId);
            }
            entry.Done.TrySetResult();
        }
    }
}
