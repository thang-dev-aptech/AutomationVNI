using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Shared.Logging;

/// <summary>
/// Background shipper: gom batch (50 hoặc mỗi 5s) rồi POST Elasticsearch _bulk.
/// Lỗi ES chỉ ghi stderr — không bao giờ ném ra app.
/// </summary>
public sealed class ElasticsearchLogShipper : IHostedService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    private readonly ElasticsearchLogQueue _queue;
    private readonly ElasticsearchLoggingOptions _options;
    private readonly HttpClient _http;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _disposed;

    public ElasticsearchLogShipper(
        ElasticsearchLogQueue queue,
        ElasticsearchLoggingOptions options,
        IHttpClientFactory httpClientFactory)
    {
        _queue = queue;
        _options = options;
        _http = httpClientFactory.CreateClient("ElasticsearchLogging");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsReady)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _cts?.Cancel();
            if (_loop is not null)
            {
                try { await _loop.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken); }
                catch { /* ignore timeout/cancel */ }
            }

            // Flush còn lại trước khi tắt.
            await FlushPendingAsync(force: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[es-logging] shutdown flush failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var batch = new List<ElasticsearchLogEntry>(_options.BatchSize);
        var flushEvery = TimeSpan.FromSeconds(Math.Max(1, _options.FlushIntervalSeconds));
        var nextFlush = DateTime.UtcNow + flushEvery;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var waitBudget = nextFlush - DateTime.UtcNow;
                if (waitBudget < TimeSpan.Zero) waitBudget = TimeSpan.Zero;

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(waitBudget);

                try
                {
                    while (batch.Count < _options.BatchSize
                           && await _queue.WaitToReadAsync(linked.Token))
                    {
                        while (batch.Count < _options.BatchSize && _queue.TryRead(out var entry))
                            batch.Add(entry);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Đến hạn flush theo thời gian.
                }

                var dueByTime = DateTime.UtcNow >= nextFlush;
                if (batch.Count >= _options.BatchSize || (dueByTime && batch.Count > 0))
                {
                    await SendBatchAsync(batch, ct);
                    batch.Clear();
                    nextFlush = DateTime.UtcNow + flushEvery;
                }
                else if (dueByTime)
                {
                    nextFlush = DateTime.UtcNow + flushEvery;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[es-logging] loop error: {ex.GetType().Name}: {ex.Message}");
                try { await Task.Delay(1000, ct); }
                catch { /* ignore */ }
            }
        }

        // Drain sau cancel.
        while (_queue.TryRead(out var leftover))
            batch.Add(leftover);
        if (batch.Count > 0)
            await SendBatchAsync(batch, CancellationToken.None);
    }

    private async Task FlushPendingAsync(bool force, CancellationToken ct)
    {
        var batch = new List<ElasticsearchLogEntry>(_options.BatchSize);
        while (_queue.TryRead(out var entry))
        {
            batch.Add(entry);
            if (batch.Count >= _options.BatchSize)
            {
                await SendBatchAsync(batch, ct);
                batch.Clear();
            }
        }

        if (batch.Count > 0 || force)
            await SendBatchAsync(batch, ct);
    }

    private async Task SendBatchAsync(List<ElasticsearchLogEntry> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;

        var index = $"app-logs-{DateTime.UtcNow:yyyy.MM.dd}";
        var payload = BuildBulkPayload(index, batch);

        var delay = TimeSpan.FromMilliseconds(200);
        for (var attempt = 1; attempt <= Math.Max(1, _options.MaxRetries); attempt++)
        {
            try
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/x-ndjson");
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.Url}/_bulk")
                {
                    Content = content,
                };
                request.Headers.TryAddWithoutValidation("CF-Access-Client-Id", _options.CfAccessClientId);
                request.Headers.TryAddWithoutValidation("CF-Access-Client-Secret", _options.CfAccessClientSecret);

                using var response = await _http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                    return;

                var body = await response.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine(
                    $"[es-logging] bulk HTTP {(int)response.StatusCode} attempt {attempt}/{_options.MaxRetries}: {Trim(body, 300)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Console.Error.WriteLine(
                    $"[es-logging] bulk send failed attempt {attempt}/{_options.MaxRetries}: {ex.GetType().Name}: {ex.Message}");
            }

            if (attempt < _options.MaxRetries)
            {
                try { await Task.Delay(delay, ct); }
                catch { return; }
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 4000));
            }
        }

        // Hết retry → drop batch (đã log stderr).
        Console.Error.WriteLine($"[es-logging] dropping batch of {batch.Count} log(s) after retries");
    }

    private static string BuildBulkPayload(string index, List<ElasticsearchLogEntry> batch)
    {
        var sb = new StringBuilder(batch.Count * 256);
        foreach (var entry in batch)
        {
            sb.Append("{\"index\":{\"_index\":\"").Append(index).Append("\"}}").Append('\n');
            sb.Append(JsonSerializer.Serialize(entry, JsonOptions)).Append('\n');
        }
        return sb.ToString();
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts?.Cancel(); }
        catch { /* ignore */ }
        _cts?.Dispose();
        _queue.Complete();
    }
}
