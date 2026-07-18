using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Ai;

public class OpenAiCompatibleTextGenerationService(
    HttpClient httpClient,
    IOptions<AiProvidersOptions> options,
    ILogger<OpenAiCompatibleTextGenerationService> logger) : IAiTextGenerationService
{
    private const string SystemPrompt = """
        You are a social media content writer for Vietnamese marketing teams.
        Respond with valid JSON only (no markdown fences, no extra commentary).
        Use this exact schema:
        {
          "caption": "main post caption in Vietnamese",
          "hashtags": ["#tag1", "#tag2"],
          "cta": "call to action text",
          "bannerHeadline": "short banner headline",
          "bannerSubheadline": "supporting banner line",
          "bannerCta": "banner button text",
          "imagePrompt": "English image generation prompt, no text in image"
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsAvailable(string? provider = null)
    {
        try
        {
            var config = ResolveProviderConfig(provider, modelOverride: null);
            return !string.IsNullOrWhiteSpace(config.ApiKey);
        }
        catch (AiProviderConfigException)
        {
            return false;
        }
    }

    public async Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        CancellationToken ct = default)
    {
        var providerKey = request.Provider ?? options.Value.DefaultProvider;
        var config = ResolveProviderConfig(providerKey, request.Model);
        var model = request.Model ?? config.DefaultTextModel;

        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new AiProviderUnavailableException(
                $"AI provider '{providerKey}' has no ApiKey configured. Set via user-secrets or environment variable.");

        if (!string.Equals(config.Api, "openai-completions", StringComparison.OrdinalIgnoreCase))
            throw new AiProviderConfigException($"AI provider '{providerKey}' API '{config.Api}' is not supported.");

        var url = BuildCompletionsUrl(config);
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = BuildUserPrompt(request) }
            },
            max_tokens = config.MaxTokens,
            temperature = config.Temperature
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI text generation HTTP call failed for provider {Provider}", providerKey);
            throw new AiTextGenerationException("AI provider request failed. Check BaseUrl and network connectivity.");
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "AI provider {Provider} returned HTTP {StatusCode}",
                providerKey, (int)response.StatusCode);
            throw new AiTextGenerationException(
                $"AI provider returned HTTP {(int)response.StatusCode}. Verify model and provider configuration.");
        }

        var content = ExtractMessageContent(responseBody);
        var stripped = StripMarkdownJson(content);
        return ParseModelJson(stripped, content);
    }

    public async Task<List<string>> SuggestIdeasAsync(
        string topic, int count, string? category, CancellationToken ct = default)
    {
        count = Math.Clamp(count <= 0 ? 5 : count, 1, 30);
        var config = ResolveProviderConfig(null, null);
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new AiProviderUnavailableException("AI provider chưa cấu hình ApiKey.");

        const string sys =
            "Bạn tạo ý tưởng bài đăng mạng xã hội cho đội marketing Việt Nam. " +
            "CHỈ trả về một JSON array các chuỗi ý tưởng ngắn (tiếng Việt), không thêm chữ nào khác.";
        var user = new StringBuilder();
        user.AppendLine($"Chủ đề: {topic.Trim()}");
        if (!string.IsNullOrWhiteSpace(category)) user.AppendLine($"Danh mục: {category.Trim()}");
        user.AppendLine($"Đề xuất {count} ý tưởng bài đăng, mỗi ý tưởng là một tiêu đề ngắn gọn, hấp dẫn.");
        user.Append($"Trả về JSON array gồm đúng {count} chuỗi.");

        var payload = new
        {
            model = config.DefaultTextModel,
            messages = new object[]
            {
                new { role = "system", content = sys },
                new { role = "user", content = user.ToString() }
            },
            max_tokens = config.MaxTokens,
            temperature = 0.9
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildCompletionsUrl(config));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI suggest-ideas HTTP call failed");
            throw new AiTextGenerationException("AI provider request failed. Check BaseUrl and network connectivity.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("AI suggest-ideas provider returned HTTP {StatusCode}", (int)response.StatusCode);
            throw new AiTextGenerationException(
                $"AI provider returned HTTP {(int)response.StatusCode}. Verify model and provider configuration.");
        }

        var content = StripMarkdownJson(ExtractMessageContent(body));
        return ParseIdeas(content, count);
    }

    private static List<string> ParseIdeas(string json, int count)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement arr;
            if (root.ValueKind == JsonValueKind.Array) arr = root;
            else if (root.TryGetProperty("ideas", out var i) && i.ValueKind == JsonValueKind.Array) arr = i;
            else return [];

            var list = new List<string>();
            foreach (var el in arr.EnumerateArray())
            {
                string? s = el.ValueKind == JsonValueKind.String
                    ? el.GetString()
                    : el.TryGetProperty("idea", out var ie) ? ie.GetString()
                    : el.TryGetProperty("title", out var te) ? te.GetString() : null;
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s!.Trim());
            }
            return list.Take(count).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private AiProviderConfig ResolveProviderConfig(string? providerKey, string? modelOverride)
    {
        var key = string.IsNullOrWhiteSpace(providerKey)
            ? options.Value.DefaultProvider
            : providerKey.Trim();

        if (!options.Value.Providers.TryGetValue(key, out var config))
            throw new AiProviderConfigException($"AI provider '{key}' not found in AiProviders:Providers configuration.");

        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            throw new AiProviderConfigException($"AI provider '{key}' BaseUrl is not configured.");

        var model = modelOverride ?? config.DefaultTextModel;
        if (string.IsNullOrWhiteSpace(model))
            throw new AiProviderConfigException($"AI provider '{key}' DefaultTextModel is not configured.");

        return config;
    }

    private static string BuildCompletionsUrl(AiProviderConfig config)
    {
        var baseUrl = config.BaseUrl.TrimEnd('/');
        var path = config.ChatCompletionsPath.StartsWith('/')
            ? config.ChatCompletionsPath
            : $"/{config.ChatCompletionsPath}";
        return $"{baseUrl}{path}";
    }

    private static string BuildUserPrompt(AiTextGenerationRequest request)
    {
        // Prompt từ template (đã thay biến) → dùng thẳng, chỉ nhắc lại ràng buộc ngôn ngữ.
        if (!string.IsNullOrWhiteSpace(request.PromptOverride))
            return request.PromptOverride.Trim();

        var sb = new StringBuilder();
        sb.AppendLine("Generate social post content with the following context:");
        AppendLine(sb, "Title", request.Title);
        AppendLine(sb, "Objective/Goal", request.Objective);
        AppendLine(sb, "Category", request.Category);
        AppendLine(sb, "Audience", request.Audience);
        AppendLine(sb, "Tone", request.Tone);
        AppendLine(sb, "Brand", request.BrandContext);
        AppendLine(sb, "CTA hint", request.CtaText);
        AppendLine(sb, "Hashtag hints", request.Hashtags);
        sb.AppendLine("Write in Vietnamese unless audience requires otherwise.");
        return sb.ToString().Trim();
    }

    private static void AppendLine(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"{label}: {value.Trim()}");
    }

    private static string ExtractMessageContent(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                throw new AiTextGenerationException("AI provider returned empty message content.");

            return content;
        }
        catch (AiTextGenerationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AiTextGenerationException("AI provider response format is invalid.");
        }
    }

    private static string StripMarkdownJson(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var lines = trimmed.Split('\n');
        if (lines.Length < 2)
            return trimmed;

        var start = 1;
        var end = lines.Length;
        if (lines[^1].Trim().StartsWith("```", StringComparison.Ordinal))
            end = lines.Length - 1;

        return string.Join('\n', lines[start..end]).Trim();
    }

    private static AiTextGenerationResult ParseModelJson(string json, string rawContent)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<AiContentJson>(json, JsonOptions)
                ?? throw new AiTextGenerationException("AI returned empty JSON payload.");

            if (string.IsNullOrWhiteSpace(parsed.Caption))
                throw new AiTextGenerationException("AI JSON is missing required field 'caption'.");

            return new AiTextGenerationResult
            {
                Caption = parsed.Caption.Trim(),
                Hashtags = parsed.Hashtags ?? [],
                Cta = parsed.Cta?.Trim() ?? string.Empty,
                BannerHeadline = parsed.BannerHeadline?.Trim() ?? string.Empty,
                BannerSubheadline = parsed.BannerSubheadline?.Trim() ?? string.Empty,
                BannerCta = parsed.BannerCta?.Trim() ?? string.Empty,
                ImagePrompt = parsed.ImagePrompt?.Trim() ?? string.Empty,
                RawResponse = rawContent
            };
        }
        catch (AiTextGenerationException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new AiTextGenerationException("AI response is not valid JSON matching the expected schema.");
        }
    }

    private sealed class AiContentJson
    {
        public string Caption { get; set; } = string.Empty;
        public List<string>? Hashtags { get; set; }
        public string? Cta { get; set; }
        public string? BannerHeadline { get; set; }
        public string? BannerSubheadline { get; set; }
        public string? BannerCta { get; set; }
        public string? ImagePrompt { get; set; }
    }
}
