using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Ai;

/// <summary>
/// Text-to-image via Google Gemini REST (generateContent). Returns raw image bytes decoded
/// from the inline base64 the model emits. Endpoint/model/modalities are config-driven so the
/// integration survives Gemini API/model changes without code edits.
/// </summary>
public class GeminiImageGenerationService(
    HttpClient httpClient,
    IOptions<AiImageProvidersOptions> options,
    ILogger<GeminiImageGenerationService> logger) : IAiImageGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Chặn ảnh tham chiếu quá lớn — inline base64 phình ~4/3 và Gemini giới hạn kích thước request.</summary>
    private const int MaxReferenceImageBytes = 7 * 1024 * 1024;

    public bool IsAvailable(string? provider = null)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(ResolveProviderConfig(provider).ApiKey);
        }
        catch (AiProviderConfigException)
        {
            return false;
        }
    }

    public async Task<AiImageGenerationResult> GenerateAsync(
        AiImageGenerationRequest request, CancellationToken ct = default)
    {
        var providerKey = request.Provider ?? options.Value.DefaultProvider;
        var config = ResolveProviderConfig(providerKey);
        var model = request.Model ?? config.DefaultImageModel;

        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new AiProviderUnavailableException(
                $"Image provider '{providerKey}' has no ApiKey configured. " +
                $"Set via user-secrets: AiImageProviders:Providers:{providerKey}:ApiKey.");

        if (!string.Equals(config.Api, "gemini-generatecontent", StringComparison.OrdinalIgnoreCase))
            throw new AiProviderConfigException(
                $"Image provider '{providerKey}' API '{config.Api}' is not supported.");

        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new AiImageGenerationException("Image prompt is empty.");

        if (string.IsNullOrWhiteSpace(model))
            throw new AiProviderConfigException($"Image provider '{providerKey}' has no image model configured.");

        var modalities = config.ResponseModalities is { Count: > 0 }
            ? config.ResponseModalities
            : new List<string> { "IMAGE" };

        var parts = new List<object>();

        // Ảnh tham chiếu đặt TRƯỚC prompt — Gemini bám theo reference chắc hơn khi ảnh đi trước
        // chỉ dẫn (đúng thứ tự trong ví dụ image-editing của Google).
        foreach (var reference in request.ReferenceImages)
        {
            if (reference.Bytes.Length == 0) continue;

            if (reference.Bytes.Length > MaxReferenceImageBytes)
            {
                logger.LogWarning(
                    "Skipping reference image '{Label}' ({Bytes} bytes) — exceeds {Max} byte limit",
                    reference.Label, reference.Bytes.Length, MaxReferenceImageBytes);
                continue;
            }

            parts.Add(new
            {
                inline_data = new
                {
                    mime_type = string.IsNullOrWhiteSpace(reference.MimeType) ? "image/png" : reference.MimeType,
                    data = Convert.ToBase64String(reference.Bytes)
                }
            });
        }

        var referenceCount = parts.Count;
        parts.Add(new { text = request.Prompt });

        var payload = new
        {
            contents = new[] { new { parts } },
            // temperature cao để cùng prompt vẫn ra khung khác nhau; biến thiên chính vẫn đến từ prompt.
            generationConfig = new { responseModalities = modalities, temperature = 1.0 }
        };

        var url = BuildGenerateContentUrl(config, model);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Add("x-goog-api-key", config.ApiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        logger.LogInformation(
            "Gemini image request → provider={Provider}, model={Model}, referenceImages={RefCount}, promptChars={PromptChars}",
            providerKey, model, referenceCount, request.Prompt.Length);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini image HTTP call failed for provider {Provider}", providerKey);
            throw new AiImageGenerationException("Image provider request failed. Check BaseUrl and network connectivity.");
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = ExtractError(responseBody);
            logger.LogWarning(
                "Gemini image provider {Provider} returned HTTP {StatusCode}: {Error}",
                providerKey, (int)response.StatusCode, error);
            throw new AiImageGenerationException(
                $"Image provider returned HTTP {(int)response.StatusCode}: {error}");
        }

        var inline = ExtractInlineImage(responseBody)
            ?? throw new AiImageGenerationException(
                "Image provider response contained no inline image data. " +
                "Verify the model supports image output (e.g. gemini-2.5-flash-image) and responseModalities.");

        return new AiImageGenerationResult
        {
            ImageBytes = inline.Bytes,
            MimeType = string.IsNullOrWhiteSpace(inline.Mime) ? "image/png" : inline.Mime!,
            Provider = providerKey,
            Model = model
        };
    }

    private AiImageProviderConfig ResolveProviderConfig(string? providerKey)
    {
        var key = string.IsNullOrWhiteSpace(providerKey) ? options.Value.DefaultProvider : providerKey.Trim();

        if (!options.Value.Providers.TryGetValue(key, out var config))
            throw new AiProviderConfigException(
                $"Image provider '{key}' not found in AiImageProviders:Providers configuration.");

        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            throw new AiProviderConfigException($"Image provider '{key}' BaseUrl is not configured.");

        return config;
    }

    private static string BuildGenerateContentUrl(AiImageProviderConfig config, string model)
    {
        var baseUrl = config.BaseUrl.TrimEnd('/');
        var path = config.GenerateContentPath.StartsWith('/')
            ? config.GenerateContentPath
            : $"/{config.GenerateContentPath}";
        return $"{baseUrl}{path.Replace("{model}", model)}";
    }

    /// <summary>First inline image across candidates[].content.parts[].inlineData (camel or snake case).</summary>
    private static (byte[] Bytes, string? Mime)? ExtractInlineImage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("content", out var content)
                    || !content.TryGetProperty("parts", out var parts)
                    || parts.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var part in parts.EnumerateArray())
                {
                    if (!part.TryGetProperty("inlineData", out var inline)
                        && !part.TryGetProperty("inline_data", out inline))
                        continue;

                    var data = inline.TryGetProperty("data", out var d) ? d.GetString() : null;
                    if (string.IsNullOrWhiteSpace(data))
                        continue;

                    var mime = inline.TryGetProperty("mimeType", out var mt) ? mt.GetString()
                        : inline.TryGetProperty("mime_type", out var mt2) ? mt2.GetString()
                        : null;

                    return (Convert.FromBase64String(data), mime);
                }
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "unknown error";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                var status = err.TryGetProperty("status", out var s) ? s.GetString() : null;
                var text = string.IsNullOrWhiteSpace(message) ? "unknown error" : message!.Trim();
                if (text.Length > 300) text = text[..300];
                return status is null ? text : $"{text} ({status})";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — avoid leaking raw content.
        }
        return "unknown error";
    }
}
