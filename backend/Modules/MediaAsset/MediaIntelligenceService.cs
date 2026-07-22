using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Shared.Ai;
using Backend.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.MediaAsset;

public class MediaAnalysisResult
{
    public List<string> Keywords { get; set; } = [];
    public string AltText { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

public class MediaRecommendationRequest
{
    public string Query { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public int Limit { get; set; } = 12;
}

public class MediaRecommendationResponse
{
    public List<string> QueryKeywords { get; set; } = [];
    public List<MediaRecommendationItem> Items { get; set; } = [];
}

public class MediaRecommendationItem
{
    public MediaAssetResponse Media { get; set; } = new();
    public double Score { get; set; }
    public List<string> MatchedKeywords { get; set; } = [];
}

public class BulkMediaAnalysisResult
{
    public int Total { get; set; }
    public int Analyzed { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = [];
}

/// <summary>Kết quả nhánh 2 (MediaMatch): 2–3 ảnh phù hợp nhất + nguồn chọn (ai/lexical).</summary>
public class MediaMatchResult
{
    public List<Guid> MediaIds { get; set; } = [];
    public string Source { get; set; } = "none";   // ai | lexical | none
    public int CandidateCount { get; set; }
}

/// <summary>
/// Dùng model GPT (OpenAI-compatible chat completions, ảnh gửi dạng data URL)
/// để phân tích ảnh thành 5-7 keyword + alt/description, và xếp hạng media
/// phù hợp với ý tưởng bài viết dựa trên keyword đã gắn.
/// </summary>
public class MediaIntelligenceService(
    HttpClient httpClient,
    AppDbContext db,
    IFileStorageService storage,
    IOptions<AiProvidersOptions> options,
    ILogger<MediaIntelligenceService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<MediaAssetModel> AnalyzeAndSaveAsync(Guid mediaId, CancellationToken ct = default)
    {
        var media = await db.MediaAssets.FirstOrDefaultAsync(x => x.Id == mediaId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy media");
        if (!media.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("AI tagging hiện chỉ hỗ trợ file ảnh");
        if (string.IsNullOrWhiteSpace(media.StoragePath) || !await storage.ExistsAsync(media.StoragePath, ct))
            throw new ArgumentException("File ảnh không tồn tại trên storage");

        await using var stream = await storage.OpenReadAsync(media.StoragePath, ct);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        var result = await AnalyzeImageAsync(memory.ToArray(), media.MimeType, ct);

        media.AltText = result.AltText;
        media.Description = result.Description;
        media.Tags = BuildTagsJson(media.Tags, result);
        media.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return media;
    }

    /// <summary>
    /// Gắn nhãn lần lượt cho toàn bộ ảnh chưa có keyword. Chạy tuần tự để tránh dùng
    /// DbContext đồng thời và tránh dồn quá nhiều request vào provider GPT.
    /// </summary>
    public async Task<BulkMediaAnalysisResult> AnalyzeAllAsync(
        bool force = false,
        CancellationToken ct = default)
    {
        var candidates = await db.MediaAssets
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.MimeType.StartsWith("image/"))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.OriginalFileName, x.FileName, x.Tags })
            .ToListAsync(ct);

        var result = new BulkMediaAnalysisResult { Total = candidates.Count };
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!force && ParseKeywords(candidate.Tags).Count > 0)
            {
                result.Skipped++;
                continue;
            }

            try
            {
                await AnalyzeAndSaveAsync(candidate.Id, ct);
                result.Analyzed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Failed++;
                if (result.Errors.Count < 20)
                    result.Errors.Add($"{candidate.OriginalFileName ?? candidate.FileName}: {ex.Message}");
                logger.LogWarning(ex, "Bulk AI tagging failed for media {MediaId}", candidate.Id);
            }
        }

        return result;
    }

    public async Task<MediaAnalysisResult> AnalyzeImageAsync(
        byte[] imageBytes,
        string mimeType,
        CancellationToken ct = default)
    {
        var (providerKey, config, model) = ResolveConfig();
        const string systemPrompt = """
            Bạn phân tích ảnh cho kho media marketing mạng xã hội Việt Nam.
            CHỈ trả về JSON hợp lệ, không markdown:
            {
              "keywords": ["5 đến 7 keyword tiếng Việt ngắn, cụ thể"],
              "altText": "mô tả ảnh tự nhiên, tối đa 140 ký tự",
              "description": "1-2 câu gồm chủ thể, sản phẩm, màu sắc, bối cảnh, phong cách, dịp phù hợp"
            }
            Keyword phải hữu ích để tìm ảnh cho bài viết; tránh từ quá chung như "ảnh", "đẹp", "sản phẩm".
            """;

        var dataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Phân tích ảnh này và trả về JSON đúng schema." },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            },
            max_tokens = 500,
            temperature = 0.1
        };

        var content = await CallChatCompletionsAsync(config, payload, ct);
        var parsed = JsonSerializer.Deserialize<MediaAnalysisPayload>(StripJsonFence(content), JsonOptions)
            ?? throw new InvalidOperationException("AI không trả metadata ảnh hợp lệ");
        var keywords = NormalizeKeywords(parsed.Keywords);
        if (keywords.Count < 3)
            throw new InvalidOperationException("AI trả thiếu keyword cho ảnh");

        return new MediaAnalysisResult
        {
            Keywords = keywords,
            AltText = Limit(parsed.AltText, 140),
            Description = Limit(parsed.Description, 600),
            Provider = providerKey,
            Model = model
        };
    }

    public async Task<MediaRecommendationResponse> RecommendAsync(
        MediaRecommendationRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Cần nhập ý tưởng hoặc nội dung để gợi ý media");

        var queryKeywords = await ExtractQueryKeywordsAsync(request.Query, ct);
        var queryTokens = Tokenize($"{request.Query} {string.Join(' ', queryKeywords)}");

        var query = db.MediaAssets.AsNoTracking().Where(x =>
            !x.IsDeleted && x.MimeType.StartsWith("image/"));
        if (request.CategoryId.HasValue)
            query = query.Where(x => x.CategoryId == request.CategoryId.Value);
        var candidates = await query.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);

        var ranked = candidates
            .Select(media =>
            {
                var mediaKeywords = ParseKeywords(media.Tags);
                var mediaTokens = Tokenize(
                    $"{string.Join(' ', mediaKeywords)} {media.AltText} {media.Description} {media.OriginalFileName}");
                var matched = queryKeywords
                    .Where(keyword => mediaTokens.Overlaps(Tokenize(keyword)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var overlap = queryTokens.Count == 0
                    ? 0
                    : queryTokens.Intersect(mediaTokens).Count() / (double)queryTokens.Count;
                var keywordScore = queryKeywords.Count == 0
                    ? 0
                    : matched.Count / (double)queryKeywords.Count;
                var analyzedBoost = mediaKeywords.Count >= 5 ? 0.08 : 0;
                return new MediaRecommendationItem
                {
                    Media = MediaAssetRepository.ToResponse(media),
                    Score = Math.Round(Math.Min(1, keywordScore * 0.7 + overlap * 0.22 + analyzedBoost), 4),
                    MatchedKeywords = matched
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Media.CreatedAt)
            .Take(Math.Clamp(request.Limit, 1, 30))
            .ToList();

        return new MediaRecommendationResponse { QueryKeywords = queryKeywords, Items = ranked };
    }

    /// <summary>
    /// Nhánh 2 — tìm 2–3 ảnh kho phù hợp với nội dung bài + loại bài (Post.CategoryId).
    /// Tối ưu token: lexical (0 token) thu kho về top-K candidate, rồi CHỈ 1 call AI chọn ảnh cuối.
    /// AI lỗi/timeout → fallback top lexical. Kho rỗng / không khớp → trả rỗng (không ném).
    /// </summary>
    public async Task<MediaMatchResult> MatchForPostAsync(
        string content,
        Guid? postCategoryId,
        int take = 3,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 5);
        var contentTokens = Tokenize(content);

        var candidates = await db.MediaAssets.AsNoTracking()
            .Where(x => !x.IsDeleted && x.MimeType.StartsWith("image/"))
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        // Lọc loại bài: CategoryIds chứa loại-bài-của-post HOẶC rỗng (dùng chung mọi loại).
        if (postCategoryId is Guid cat && cat != Guid.Empty)
        {
            var needle = cat.ToString();
            candidates = candidates
                .Where(x => string.IsNullOrWhiteSpace(x.CategoryIds) || x.CategoryIds.Contains(needle))
                .ToList();
        }

        // Chấm điểm lexical (tái dùng ý tưởng RecommendAsync) → top 20.
        var scored = candidates
            .Select(m =>
            {
                var kws = ParseKeywords(m.Tags);
                var mediaTokens = Tokenize(
                    $"{string.Join(' ', kws)} {m.AltText} {m.Description} {m.OriginalFileName}");
                var overlap = contentTokens.Count == 0
                    ? 0
                    : contentTokens.Intersect(mediaTokens).Count() / (double)contentTokens.Count;
                var analyzedBoost = kws.Count >= 5 ? 0.08 : 0;
                return new ScoredMedia(m, kws, overlap + analyzedBoost);
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Media.CreatedAt)
            .Take(20)
            .ToList();

        if (scored.Count == 0)
            return new MediaMatchResult { Source = "none", CandidateCount = 0 };

        // 1 call AI chọn ảnh cuối; lỗi → fallback lexical.
        try
        {
            var picked = await PickBestMediaAsync(content, scored, take, ct);
            if (picked.Count > 0)
                return new MediaMatchResult { MediaIds = picked, Source = "ai", CandidateCount = scored.Count };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MediaMatch: AI pick lỗi, fallback lexical top {Take}", take);
        }

        // Fallback lexical: chỉ lấy ảnh có điểm > 0 (thật sự khớp), tránh gắn ảnh ngẫu nhiên khi AI offline.
        var lexicalPicks = scored.Where(x => x.Score > 0).Take(take).Select(x => x.Media.Id).ToList();
        return new MediaMatchResult
        {
            MediaIds = lexicalPicks,
            Source = lexicalPicks.Count > 0 ? "lexical" : "none",
            CandidateCount = scored.Count
        };
    }

    /// <summary>
    /// 1 call AI: gửi nội dung bài (cắt ~400 ký tự) + candidate NÉN (index thay GUID, keyword top-5,
    /// description cắt ~80 ký tự) → nhận mảng index. Map index → MediaAssetId theo thứ tự AI trả.
    /// </summary>
    private async Task<List<Guid>> PickBestMediaAsync(
        string content, List<ScoredMedia> scored, int take, CancellationToken ct)
    {
        using var quickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        quickCts.CancelAfter(TimeSpan.FromSeconds(15));
        var token = quickCts.Token;

        var (_, config, model) = ResolveConfig();

        // index 1-based → media (nén payload để giảm token).
        var indexed = scored
            .Select((s, i) => new { i = i + 1, s.Media, s.Keywords })
            .ToList();
        var candidatesPayload = indexed
            .Select(x => new
            {
                x.i,
                kw = x.Keywords.Take(5).ToList(),
                d = Limit(x.Media.Description ?? x.Media.AltText, 80)
            })
            .ToList();

        var systemPrompt =
            $"Bạn chọn {take} ảnh phù hợp NHẤT với nội dung bài đăng mạng xã hội Việt Nam, dựa trên keyword và mô tả ảnh. " +
            "CHỈ trả JSON: {\"picked\":[số i của ảnh, tối đa " + take + ", theo độ phù hợp giảm dần]}. " +
            "Chỉ chọn ảnh thật sự liên quan; nếu không ảnh nào hợp thì trả {\"picked\":[]}.";
        var userPrompt =
            $"NỘI DUNG BÀI:\n{Limit(content, 400)}\n\nDANH SÁCH ẢNH (i, kw=keyword, d=mô tả):\n" +
            JsonSerializer.Serialize(candidatesPayload, JsonOptions);

        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 120,
            temperature = 0
        };

        var responseText = await CallChatCompletionsAsync(config, payload, token);
        var parsed = JsonSerializer.Deserialize<PickPayload>(StripJsonFence(responseText), JsonOptions);
        var byIndex = indexed.ToDictionary(x => x.i, x => x.Media.Id);

        var result = new List<Guid>();
        foreach (var idx in parsed?.Picked ?? [])
        {
            if (byIndex.TryGetValue(idx, out var id) && !result.Contains(id))
                result.Add(id);
            if (result.Count >= take) break;
        }
        return result;
    }

    private sealed record ScoredMedia(MediaAssetModel Media, List<string> Keywords, double Score);

    private sealed class PickPayload
    {
        public List<int>? Picked { get; set; }
    }

    public async Task<List<string>> ExtractQueryKeywordsAsync(string query, CancellationToken ct = default)
    {
        try
        {
            // Recommend phải phản hồi nhanh — nếu provider chậm/chết thì cắt sớm và fallback lexical.
            using var quickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            quickCts.CancelAfter(TimeSpan.FromSeconds(12));
            ct = quickCts.Token;

            var (_, config, model) = ResolveConfig();
            var payload = new
            {
                model,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Bạn trích keyword tìm ảnh cho bài đăng mạng xã hội Việt Nam. " +
                                  "CHỈ trả JSON: {\"keywords\":[\"...\"]} với đúng 5-7 keyword tiếng Việt ngắn."
                    },
                    new
                    {
                        role = "user",
                        content = $"Trích keyword (sản phẩm/chủ thể, màu sắc, bối cảnh, phong cách, dịp) từ nội dung: {query.Trim()}"
                    }
                },
                max_tokens = 200,
                temperature = 0.1
            };

            var content = await CallChatCompletionsAsync(config, payload, ct);
            var parsed = JsonSerializer.Deserialize<KeywordPayload>(StripJsonFence(content), JsonOptions);
            var keywords = NormalizeKeywords(parsed?.Keywords);
            if (keywords.Count >= 3) return keywords;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI query keyword extraction failed; using lexical fallback");
        }

        return Tokenize(query).Where(x => x.Length >= 3).Take(7).ToList();
    }

    public static List<string> ParseKeywords(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return [];
        try
        {
            using var doc = JsonDocument.Parse(tags);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("keywords", out var keywords)
                && keywords.ValueKind == JsonValueKind.Array)
                return NormalizeKeywords(keywords.EnumerateArray().Select(x => x.GetString()));
            return [];
        }
        catch
        {
            // Tags cũ dạng chuỗi thường "a, b, c"
            return tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).Take(12).ToList();
        }
    }

    private (string Provider, AiProviderConfig Config, string Model) ResolveConfig()
    {
        var provider = options.Value.DefaultProvider;
        if (!options.Value.Providers.TryGetValue(provider, out var config))
            throw new InvalidOperationException($"Không tìm thấy AI provider '{provider}'");
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("Chưa cấu hình API key để AI phân tích ảnh");
        var model = string.IsNullOrWhiteSpace(config.DefaultVisionModel)
            ? config.DefaultTextModel
            : config.DefaultVisionModel;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Chưa cấu hình model GPT cho phân tích ảnh");
        return (provider, config, model);
    }

    private async Task<string> CallChatCompletionsAsync(
        AiProviderConfig config,
        object payload,
        CancellationToken ct)
    {
        var path = config.ChatCompletionsPath.StartsWith('/')
            ? config.ChatCompletionsPath
            : $"/{config.ChatCompletionsPath}";
        var url = $"{config.BaseUrl.TrimEnd('/')}{path}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Media AI chat completions returned HTTP {Status}: {Body}",
                (int)response.StatusCode, body.Length <= 400 ? body : body[..400]);
            throw new InvalidOperationException($"AI phân tích media lỗi HTTP {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("AI không trả nội dung phân tích");
        return content;
    }

    private static string BuildTagsJson(string? originalTags, MediaAnalysisResult result)
        => JsonSerializer.Serialize(new
        {
            keywords = result.Keywords,
            aiAnalysis = new
            {
                provider = result.Provider,
                model = result.Model,
                analyzedAt = DateTime.UtcNow
            },
            originalTags = string.IsNullOrWhiteSpace(originalTags) ? null : originalTags
        }, JsonOptions);

    private static List<string> NormalizeKeywords(IEnumerable<string?>? values)
        => (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Regex.Replace(x!.Trim().ToLowerInvariant(), @"\s+", " "))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(7)
            .ToList();

    private static HashSet<string> Tokenize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        var normalized = input.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var withoutMarks = string.Concat(normalized.Where(c =>
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
        return Regex.Split(withoutMarks, @"[^\p{L}\p{N}]+")
            .Where(x => x.Length >= 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string StripJsonFence(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("```"))
        {
            text = Regex.Replace(text, @"^```(?:json)?\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s*```$", "");
        }
        return text.Trim();
    }

    private static string Limit(string? value, int max)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }

    private sealed class MediaAnalysisPayload
    {
        public List<string>? Keywords { get; set; }
        public string? AltText { get; set; }
        public string? Description { get; set; }
    }

    private sealed class KeywordPayload
    {
        public List<string>? Keywords { get; set; }
    }
}
