using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Backend.Shared.OpenClaw;

public class OpenClawException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Kết quả một lượt chạy JS: URL THẬT của trang + dữ liệu trả về.</summary>
public sealed record BrowserEvalResult(string Url, JsonElement Result);

public interface IOpenClawBrowserClient
{
    bool IsConfigured { get; }
    Task<bool> PingAsync(CancellationToken ct = default);
    Task<string> NavigateAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Chạy JS trên tab hiện tại. Trả về kèm URL thật để caller đối chiếu — đây là chốt
    /// chặn lỗi "bóc nhầm trang cũ": nếu navigate chưa xong mà chạy JS luôn thì sẽ lấy
    /// nội dung trang trước đó, không có lỗi nào báo ra.
    /// </summary>
    Task<BrowserEvalResult> EvaluateAsync(string fn, CancellationToken ct = default);
}

public class OpenClawBrowserClient(
    HttpClient http,
    IOptions<OpenClawOptions> options,
    ILogger<OpenClawBrowserClient> logger) : IOpenClawBrowserClient
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public bool IsConfigured =>
        options.Value.Enabled
        && !string.IsNullOrWhiteSpace(options.Value.BaseUrl)
        && !string.IsNullOrWhiteSpace(options.Value.Token);

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            using var request = Build(HttpMethod.Get, "/tabs");
            using var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenClaw: ping thất bại");
            return false;
        }
    }

    public async Task<string> NavigateAsync(string url, CancellationToken ct = default)
    {
        // PHẢI truyền timeoutMs: mặc định page.goto của OpenClaw chỉ 20s, và trang báo Việt Nam
        // nặng (dantri ~520KB + 18 script quảng cáo) thường xuyên vượt mức đó. Tên tham số là
        // "timeoutMs" — dùng "timeout" thì bị bỏ qua trong im lặng và vẫn timeout ở 20s.
        var timeoutMs = Math.Max(5, options.Value.NavigateTimeoutSeconds) * 1000;
        var body = JsonSerializer.Serialize(new { url, timeoutMs });
        using var doc = await PostAsync("/navigate", body, options.Value.NavigateTimeoutSeconds + 15, ct);
        return doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() ?? url : url;
    }

    public async Task<BrowserEvalResult> EvaluateAsync(string fn, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { kind = "evaluate", fn });
        using var doc = await PostAsync("/act", body, options.Value.EvaluateTimeoutSeconds, ct);

        var root = doc.RootElement;
        var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
        var result = root.TryGetProperty("result", out var r) ? r.Clone() : default;
        return new BrowserEvalResult(url, result);
    }

    private async Task<JsonDocument> PostAsync(string path, string body, int timeoutSeconds, CancellationToken ct)
    {
        EnsureConfigured();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));

        using var request = Build(HttpMethod.Post, path);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OpenClawException($"OpenClaw quá hạn {timeoutSeconds}s khi gọi {path}.");
        }
        catch (HttpRequestException ex)
        {
            throw new OpenClawException(
                $"Không kết nối được OpenClaw tại {options.Value.BaseUrl}. " +
                "Kiểm tra: gateway đang chạy (`openclaw gateway status`), và biến " +
                "OPENCLAW_EAGER_BROWSER_CONTROL_SERVER=1 còn trong service-env.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cts.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new OpenClawException(
                    "OpenClaw từ chối token. Lấy lại ở ~/.openclaw/openclaw.json → gateway.auth.token " +
                    "rồi đặt vào OpenClaw:Token (user-secrets hoặc biến môi trường).");
            if (!response.IsSuccessStatusCode)
                throw new OpenClawException($"OpenClaw trả HTTP {(int)response.StatusCode} cho {path}: {Truncate(text)}");

            // 404 kèm HTML nghĩa là trúng SPA fallback → sai cổng (đang gọi 18789 thay vì 18791).
            if (text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                throw new OpenClawException(
                    $"{path} trả về HTML thay vì JSON — nhiều khả năng BaseUrl đang trỏ vào cổng gateway " +
                    "(18789) thay vì cổng browser control (18791).");

            try { return JsonDocument.Parse(text); }
            catch (JsonException ex) { throw new OpenClawException($"OpenClaw trả JSON hỏng cho {path}: {Truncate(text)}", ex); }
        }
    }

    private HttpRequestMessage Build(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{options.Value.BaseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
        return request;
    }

    private void EnsureConfigured()
    {
        if (!options.Value.Enabled)
            throw new OpenClawException("OpenClaw đang tắt trong cấu hình (OpenClaw:Enabled = false).");
        if (string.IsNullOrWhiteSpace(options.Value.Token))
            throw new OpenClawException("Chưa cấu hình OpenClaw:Token.");
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200];
}
