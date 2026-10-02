using System.Threading.Channels;

namespace Backend.Shared.Logging;

/// <summary>
/// Hàng đợi log giới hạn — ghi không bao giờ block lâu; đầy thì drop bản ghi mới nhất
/// để không phình memory khi ES chết.
/// </summary>
public sealed class ElasticsearchLogQueue
{
    private readonly Channel<ElasticsearchLogEntry> _channel;
    private readonly int _capacity;
    private int _dropped;

    public ElasticsearchLogQueue(int capacity)
    {
        // Capacity đúng theo cấu hình (tối thiểu 1) — không floor lên 16, để drop đếm được
        // và test ép đầy với capacity nhỏ vẫn phản ánh hành vi production.
        _capacity = Math.Max(1, capacity);
        var options = new BoundedChannelOptions(_capacity)
        {
            // Wait + TryWrite (không await): khi đầy TryWrite trả false ngay — DropWrite thì
            // TryWrite vẫn true dù item bị bỏ thầm (violation 2b77fe13).
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        };
        _channel = Channel.CreateBounded<ElasticsearchLogEntry>(options);
    }

    public int Capacity => _capacity;

    public int DroppedCount => Volatile.Read(ref _dropped);

    public bool TryEnqueue(ElasticsearchLogEntry entry)
    {
        try
        {
            if (_channel.Writer.TryWrite(entry))
                return true;

            Interlocked.Increment(ref _dropped);
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[es-logging] enqueue failed: {ex.GetType().Name}: {ex.Message}");
            Interlocked.Increment(ref _dropped);
            return false;
        }
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct)
        => _channel.Reader.WaitToReadAsync(ct);

    public bool TryRead(out ElasticsearchLogEntry entry)
        => _channel.Reader.TryRead(out entry!);

    public void Complete()
    {
        try { _channel.Writer.TryComplete(); }
        catch { /* ignore */ }
    }
}
