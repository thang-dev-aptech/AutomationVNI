using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Ai;

/// <summary>
/// Gọi AI hỏi một câu ngắn, trả về text thô. Dùng cho những việc KHÔNG hợp với
/// IAiTextGenerationService — cái đó khoá cứng vào JSON schema caption Facebook nên
/// không hỏi kiểu có/không được.
/// </summary>
public interface IAiJudgeService
{
    bool IsAvailable(string? provider = null);

    /// <summary>Trả về nội dung message, hoặc null nếu lỗi/timeout. KHÔNG ném — caller tự quyết.</summary>
    Task<string?> AskAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens = 200,
        double temperature = 0,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}

public class AiJudgeService(
    HttpClient http,
    IOptions<AiProvidersOptions> options,
    ILogger<AiJudgeService> logger) : IAiJudgeService
{
    public bool IsAvailable(string? provider = null)
    {
        try
        {
            var config = ResolveConfig(provider);
            return !string.IsNullOrWhiteSpace(config.ApiKey);
        }
        catch (AiProviderConfigException)
        {
            return false;
        }
    }

    public async Task<string?> AskAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens = 200,
        double temperature = 0,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        AiProviderConfig config;
        try { config = ResolveConfig(null); }
        catch (AiProviderConfigException ex)
        {
            logger.LogWarning("AiJudge: cấu hình provider lỗi — {Message}", ex.Message);
            return null;
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            logger.LogDebug("AiJudge: provider chưa có ApiKey, bỏ qua");
            return null;
        }

        var model = config.DefaultTextModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            logger.LogWarning("AiJudge: provider chưa cấu hình DefaultTextModel");
            return null;
        }

        var payload = JsonSerializer.Serialize(new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = maxTokens,
            temperature
        });

        // CTS liên kết: hết giờ riêng của lượt hỏi này thì huỷ, không kéo theo request cha.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));

        try
        {
            var url = $"{config.BaseUrl.TrimEnd('/')}{NormalizePath(config.ChatCompletionsPath)}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

            using var response = await http.SendAsync(request, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AiJudge: HTTP {Code} — {Body}", (int)response.StatusCode, Truncate(body, 200));
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("AiJudge: quá hạn sau {Seconds}s", (timeout ?? TimeSpan.FromSeconds(20)).TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AiJudge: gọi thất bại");
            return null;
        }
    }

    private AiProviderConfig ResolveConfig(string? provider)
    {
        var key = string.IsNullOrWhiteSpace(provider) ? options.Value.DefaultProvider : provider;
        if (!options.Value.Providers.TryGetValue(key, out var config))
            throw new AiProviderConfigException($"AI provider '{key}' không có trong AiProviders:Providers.");
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            throw new AiProviderConfigException($"AI provider '{key}' chưa cấu hình BaseUrl.");
        return config;
    }

    private static string NormalizePath(string path)
        => string.IsNullOrWhiteSpace(path) ? "/chat/completions"
            : path.StartsWith('/') ? path : $"/{path}";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
