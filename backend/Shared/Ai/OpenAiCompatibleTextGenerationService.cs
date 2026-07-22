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
        Bạn là copywriter Facebook chuyên nghiệp cho Page bán hàng / thương hiệu Việt Nam.

        Nhiệm vụ: viết caption sẵn sàng đăng feed Facebook — tiếng Việt tự nhiên, thuyết phục, dễ đọc trên mobile.

        CHỈ trả về JSON hợp lệ (không markdown, không giải thích), đúng schema:
        {
          "caption": "toàn bộ nội dung bài đăng (đã có emoji + xuống dòng; CHƯA gồm dòng hashtag cuối)",
          "hashtags": ["#tag1", "#tag2", "#tag3", "#tag4"],
          "cta": "một câu CTA ngắn, có thể kèm emoji",
          "bannerHeadline": "tiêu đề banner ngắn",
          "bannerSubheadline": "dòng phụ banner",
          "bannerCta": "chữ nút banner",
          "imagePrompt": "English prompt tả banner: chủ thể chính, bối cảnh, bố cục, ánh sáng, phong cách. Banner có chữ — mô tả cả vị trí khối chữ, KHÔNG tự bịa hotline/website"
        }

        Cấu trúc caption (bắt buộc):
        1) Hook 1 câu mở đầu + 1–2 emoji phù hợp ngành.
        2) Thân bài 2–4 ý, mỗi ý xuống dòng; dùng bullet (• / ✅ / ✨ / 👗…) để dễ scan.
        3) 4–8 emoji tổng bài — đủ sống động, không spam mỗi từ một icon.
        4) Không nhồi hashtag vào giữa caption; hashtag chỉ trả trong mảng "hashtags".
        5) Không kết caption bằng CTA — CTA trả riêng ở field "cta" (hệ thống sẽ ghép).

        Chất lượng:
        - Gắn đúng ý tưởng (title) + danh mục; cụ thể, không sáo rỗng.
        - Tránh cụm generic: "nâng tầm phong cách", "tự tin theo cách riêng", "mặc đẹp mỗi ngày" nếu không có chi tiết mới.
        - Luôn tự viết CTA + 4–6 hashtag chuyên ngành dù context CTA/hashtag chung chung hoặc thiếu.
        - Độ dài thân bài khoảng 80–160 từ (chưa tính hashtag).
        """;

    private const string ImagePromptSystem = """
        You are a senior creative director at a premium social-media ad agency. From the Vietnamese
        caption, the brand context, and a CREATIVE DIRECTION brief, WRITE ONE rich, specific English
        prompt (240-340 words) for an AI image generator to produce a striking, ORIGINAL 1:1 design in
        the FORMAT given in the brief (a banner, poster, advertising key visual, magazine ad, etc.) —
        vary the genre so consecutive designs never feel like the same template.

        Your #1 goal: REALIZE the given creative direction so the banner looks intentionally DESIGNED,
        not like a default AI template. Follow the brief's composition, typography, visual style, mood
        and hero focus faithfully and boldly — let it drive a distinctive layout.

        AVOID these over-used "AI-looking" layouts:
        - logo top-left + a person on the right + a row of feature chips + a full-width CTA bar at the bottom;
        - a plain horizontal three-stack: logo/title on top, one photo in the middle, contact bar at the bottom.
        Do something clearly different, driven by the brief.

        FILL & FOCUS (important):
        - Fill the whole square richly — either ONE full-bleed scene, or a rich photo scene on one side
          plus a densely-designed content panel on the other. Never a subject floating on a plain, empty
          or studio backdrop; leave no large empty, flat or monotone areas. Do NOT fill with scattered
          decorative patterns, and do NOT collage or stack multiple large overlapping photos.
        - Establish ONE clear FOCAL POINT (per the brief) that pops above everything via scale, contrast,
          lighting or an accent colour — the eye must land there first. Build a strong visual HIERARCHY;
          everything else is clearly secondary. Avoid a flat, evenly-weighted layout where nothing stands out.
        - Colour & harmony (IMPORTANT — must always look tasteful, never garish): colour the MAIN CONTENT
          BLOCK per the brief's "Content block" — vary it across designs, do NOT always default to blue.
          Blue/navy is the anchor; orange and green appear ONLY as small accents (a thin line, an icon,
          the CTA, one keyword) — NEVER as a large solid background fill (a big saturated green or orange
          panel looks cheap). Keep a calm, harmonious, premium palette: one dominant block colour + white/
          neutral space + at most one accent. Avoid clashing or over-saturated colour combinations. Ensure
          high contrast so text stays perfectly legible on whatever block colour is chosen.
        - You MAY add a few tasteful, topic-related supporting graphics or highlight badges (small icons,
          stat pills, benefit tags) that reinforce the focal point — organised, never cluttered.

        Respect the brand (non-negotiable) but express it creatively:
        - Reserve a tasteful, uncluttered spot for the PROVIDED brand logo — placement can be creative
          (not always top-left); keep it exact, do NOT redraw or recolor it.
        - Use the brand colors as the palette.
        - Include the exact Vietnamese headline and subheadline; keep ALL Vietnamese text large, clean and
          perfectly legible with correct diacritics. NEVER sacrifice readability for style (no heavy
          outline/overlap that garbles Vietnamese text). If the brief's "Script accent" asks for a
          handwritten/script touch, apply it to only ONE short phrase (a hook or tagline) and keep it
          clearly legible; otherwise keep all type clean sans-serif.
        - Feature highlights: if feature labels are provided, use EXACTLY them; otherwise derive 3-4
          DISTINCT feature points from the caption. Never duplicate a label or add extras — each as a
          small actual-glyph icon plus its short Vietnamese label.
        - CTA & CONTACT (MANDATORY — this is a Facebook ad; NEVER omit it, whatever the creative layout):
          include BOTH a clearly visible CTA button with a short Vietnamese action label AND a legible
          contact strip that prints the EXACT hotline and website provided, copied verbatim (do not alter,
          abbreviate, translate or invent). Integrate it tastefully into the composition — but it must be
          present and readable in every single design.

        Write as labelled lines: Concept, Composition & layout, Focal point & hierarchy (what pops first),
        Scene & framing (how the frame is filled), Subject/imagery, Typography & headline,
        Brand, colour & emphasis, Feature highlights, CTA & contact (button + exact hotline/website),
        Finishing details (shapes, depth, lighting), Quality constraints (ultra realistic OR clean vector
        as the style fits, 4K, sharp Vietnamese text, no watermark, no gibberish/garbled text on
        props/screens, no distorted hands/faces, readable on mobile).

        Rules:
        - Ground every choice in the caption; realize the CREATIVE DIRECTION distinctly each time.
        - If an AVOID layout/prompt is given, make a clearly different composition from it.
        - Output ONLY the prompt text. No JSON, no markdown, no preamble.
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
        var result = ParseModelJson(stripped, content);
        result.Provider = providerKey;
        result.Model = model;
        return result;
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

    public async Task<string> ComposeImagePromptAsync(
        AiImagePromptRequest request, CancellationToken ct = default)
    {
        var providerKey = request.Provider ?? options.Value.DefaultProvider;
        AiProviderConfig config;
        try
        {
            config = ResolveProviderConfig(providerKey, request.Model);
        }
        catch (AiProviderConfigException)
        {
            return string.Empty;
        }

        var model = request.Model ?? config.DefaultTextModel;
        if (string.IsNullOrWhiteSpace(config.ApiKey)
            || !string.Equals(config.Api, "openai-completions", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = ImagePromptSystem },
                new { role = "user", content = BuildImagePromptUser(request) }
            },
            max_tokens = config.MaxTokens,
            // Nhiệt độ cao để mỗi lần sinh cho một bố cục khác (brand vẫn khóa ở AppendBrandLock).
            temperature = 0.95
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildCompletionsUrl(config));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "AI image-prompt provider {Provider} returned HTTP {StatusCode}",
                    providerKey, (int)response.StatusCode);
                return string.Empty;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return StripMarkdownJson(ExtractMessageContent(body)).Trim();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Không chặn sinh ảnh: caller sẽ fallback prompt tĩnh khi trả rỗng.
            logger.LogWarning(ex, "AI image-prompt composition failed for provider {Provider}", providerKey);
            return string.Empty;
        }
    }

    private static string BuildImagePromptUser(AiImagePromptRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Caption (tiếng Việt) — nguồn ngữ cảnh");
        sb.AppendLine((string.IsNullOrWhiteSpace(r.Caption) ? r.Title : r.Caption)?.Trim() ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(r.CreativeBrief))
        {
            sb.AppendLine();
            sb.AppendLine("## CREATIVE DIRECTION (realize this fully, boldly — it drives the layout)");
            sb.AppendLine(r.CreativeBrief.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("## Brand context");
        AppendLine(sb, "Brand", r.Brand);
        AppendLine(sb, "Category", r.Category);
        AppendLine(sb, "Brand colors", r.BrandColors);
        AppendLine(sb, "Banner headline", r.BannerHeadline);
        AppendLine(sb, "Banner subheadline", r.BannerSubheadline);
        AppendLine(sb, "CTA button label", string.IsNullOrWhiteSpace(r.BannerCta) ? r.Cta : r.BannerCta);
        AppendLine(sb, "Feature labels (use exactly these, no duplicates)", r.FeatureLabels);
        if (r.HasLogoReference)
            sb.AppendLine("Logo: a brand logo image is provided separately — keep it exact, do not redraw.");

        // CTA/contact bắt buộc — đưa giá trị thật để LLM in đúng, không bịa.
        if (!string.IsNullOrWhiteSpace(r.Hotline) || !string.IsNullOrWhiteSpace(r.Website))
        {
            sb.AppendLine();
            sb.AppendLine("## CTA & CONTACT (MANDATORY — must appear clearly in the banner, print verbatim)");
            AppendLine(sb, "Hotline (print exactly)", r.Hotline);
            AppendLine(sb, "Website (print exactly)", r.Website);
        }

        if (!string.IsNullOrWhiteSpace(r.StyleGuide))
        {
            sb.AppendLine();
            sb.AppendLine("## Style guide (tham chiếu phong cách, KHÔNG copy nguyên văn)");
            sb.AppendLine(r.StyleGuide.Trim());
        }

        if (!string.IsNullOrWhiteSpace(r.ImagePromptHint))
        {
            sb.AppendLine();
            sb.AppendLine("## Gợi ý bố cục ban đầu");
            sb.AppendLine(r.ImagePromptHint.Trim());
        }

        if (!string.IsNullOrWhiteSpace(r.AvoidLayout))
        {
            sb.AppendLine();
            sb.AppendLine("## AVOID — bố cục lần trước (hãy làm KHÁC đi)");
            sb.AppendLine(r.AvoidLayout.Trim());
        }

        return sb.ToString().Trim();
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
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(request.PromptOverride))
        {
            sb.AppendLine("## Brief từ template danh mục (đã điền biến)");
            sb.AppendLine(request.PromptOverride.Trim());
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## Brief");
            sb.AppendLine("Viết bài đăng mạng xã hội theo context bên dưới.");
            sb.AppendLine();
        }

        sb.AppendLine("## Context bài viết");
        AppendLine(sb, "Ý tưởng (title)", request.Title);
        AppendLine(sb, "Danh mục", request.Category);
        AppendLine(sb, "Mục tiêu", request.Objective);
        AppendLine(sb, "Thương hiệu", request.BrandContext);
        AppendLine(sb, "Giọng văn", request.Tone);
        AppendLine(sb, "Đối tượng", request.Audience);
        AppendLine(sb, "CTA gợi ý", request.CtaText);
        AppendLine(sb, "Hashtag gợi ý", request.Hashtags);

        sb.AppendLine();
        sb.AppendLine("## Ràng buộc Facebook");
        sb.AppendLine("- Caption có emoji/icon + xuống dòng rõ; sẵn sàng đăng feed.");
        sb.AppendLine("- Tự viết CTA hấp dẫn + 4–6 hashtag đúng ngành (kể cả khi CTA/hashtag gợi ý ở trên còn chung chung).");
        sb.AppendLine("- Không bịa giá/thông số nếu ý tưởng không nêu.");
        sb.AppendLine("- Trả đúng JSON schema ở system prompt.");

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
