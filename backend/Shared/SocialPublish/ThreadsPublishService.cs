using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Modules.SocialChannel.Enums;
using Microsoft.Extensions.Options;

namespace Backend.Shared.SocialPublish;

/// <summary>
/// Đăng bài lên Threads. Khác Facebook ở hai điểm cốt lõi:
/// 1. Luồng 2 bước — tạo media container rồi mới publish container đó, thay vì một POST duy nhất.
/// 2. Ảnh phải là URL công khai (image_url); Threads tự đi tải về, không nhận multipart upload.
/// </summary>
public partial class ThreadsPublishService(
    HttpClient httpClient,
    IOptions<SocialPublishOptions> options,
    ILogger<ThreadsPublishService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<SocialPublishResult> PublishAsync(
        SocialPublishRequest request, CancellationToken ct = default)
    {
        if (request.Platform != SocialPlatform.Threads)
            return SocialPublishResult.Failed("THREADS_UNSUPPORTED_PLATFORM", "Only Threads channels are supported.");

        if (string.IsNullOrWhiteSpace(request.PageExternalId))
            return SocialPublishResult.Failed("THREADS_USER_ID_MISSING", "Threads user id (ExternalPageId) is required.");

        if (string.IsNullOrWhiteSpace(request.AccessToken))
            return SocialPublishResult.Failed("THREADS_TOKEN_MISSING", "Threads access token is required.");

        var th = options.Value.Threads;
        var (text, truncated) = NormalizeText(request.Caption, th.MaxTextLength);

        // Threads không cho đăng bài rỗng hoàn toàn.
        if (string.IsNullOrWhiteSpace(text))
            return SocialPublishResult.Failed("THREADS_EMPTY_TEXT", "Threads post text is empty.");

        if (truncated)
        {
            logger.LogWarning(
                "Threads caption for post {PostId} exceeded {Max} chars and was truncated",
                request.PostId, th.MaxTextLength);
        }

        var imageUrls = ResolvePublicImageUrls(request, th);
        var isImagePost = imageUrls.Count > 0;

        if (!isImagePost && HasMediaButNoPublicUrl(request))
        {
            logger.LogWarning(
                "Threads publish for post {PostId} has media but no publicly reachable URL "
                + "(SocialPublish:Threads:PublicBaseUrl chưa cấu hình?). Falling back to text-only.",
                request.PostId);
        }

        // Bước 1 — tạo container. Từ 2 ảnh trở lên dùng carousel (children containers).
        var containerResult = imageUrls.Count > 1
            ? await CreateCarouselContainerAsync(request, th, text, imageUrls, ct)
            : await CreateContainerAsync(request, th, text, imageUrls.FirstOrDefault(), ct);
        if (!containerResult.Success)
            return containerResult;

        var creationId = containerResult.PublishedExternalId!;

        // Bước 2 — Meta cần thời gian tải ảnh về trước khi container publish được.
        // Bài text sẵn sàng ngay nên không chờ.
        if (isImagePost && th.MediaProcessingDelaySeconds > 0)
        {
            logger.LogInformation(
                "Threads container {CreationId} for post {PostId} created, waiting {Seconds}s for media processing",
                creationId, request.PostId, th.MediaProcessingDelaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(th.MediaProcessingDelaySeconds), ct);
        }

        var publishResult = await PublishContainerAsync(request, th, creationId, ct);
        if (!publishResult.Success)
            return publishResult;

        // Permalink là best-effort — publish đã thành công rồi, không để lỗi ở đây làm hỏng kết quả.
        var mediaId = publishResult.PublishedExternalId!;
        var permalink = await TryFetchPermalinkAsync(th, mediaId, request.AccessToken!, ct)
            ?? $"https://www.threads.net/@me/post/{mediaId}";

        logger.LogInformation(
            "Threads publish succeeded for post {PostId}, mediaId {MediaId}",
            request.PostId, mediaId);

        return SocialPublishResult.Succeeded(
            mediaId, permalink, publishResult.RawResponseSanitized);
    }

    private async Task<SocialPublishResult> CreateContainerAsync(
        SocialPublishRequest request, ThreadsPublishOptions th,
        string text, string? imageUrl, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken!,
            ["media_type"] = imageUrl is null ? "TEXT" : "IMAGE",
            ["text"] = text
        };

        if (imageUrl is not null)
            form["image_url"] = imageUrl;

        var url = BuildUrl(th, request.PageExternalId, "threads");
        var result = await SendAsync(url, new FormUrlEncodedContent(form), request.PostId, "create-container", ct);

        if (result.Success && string.IsNullOrWhiteSpace(result.PublishedExternalId))
            return SocialPublishResult.Failed("THREADS_INVALID_RESPONSE", "Threads returned no container id.", result.RawResponseSanitized);

        return result;
    }

    /// <summary>
    /// Bài nhiều ảnh trên Threads = carousel: tạo container con cho từng ảnh
    /// (is_carousel_item=true), rồi tạo container cha media_type=CAROUSEL với children.
    /// Threads giới hạn tối đa 20 ảnh/carousel.
    /// </summary>
    private async Task<SocialPublishResult> CreateCarouselContainerAsync(
        SocialPublishRequest request, ThreadsPublishOptions th,
        string text, List<string> imageUrls, CancellationToken ct)
    {
        var url = BuildUrl(th, request.PageExternalId, "threads");
        var childIds = new List<string>();

        foreach (var imageUrl in imageUrls.Take(20))
        {
            var form = new Dictionary<string, string>
            {
                ["access_token"] = request.AccessToken!,
                ["media_type"] = "IMAGE",
                ["image_url"] = imageUrl,
                ["is_carousel_item"] = "true"
            };

            var childResult = await SendAsync(
                url, new FormUrlEncodedContent(form), request.PostId, "create-carousel-item", ct);

            if (!childResult.Success || string.IsNullOrWhiteSpace(childResult.PublishedExternalId))
            {
                // Ảnh đầu (cover) lỗi → fail cả bài; ảnh phụ lỗi → bỏ qua, đăng phần còn lại.
                if (childIds.Count == 0)
                {
                    return childResult.Success
                        ? SocialPublishResult.Failed(
                            "THREADS_INVALID_RESPONSE", "Threads returned no carousel item id.",
                            childResult.RawResponseSanitized)
                        : childResult;
                }

                logger.LogWarning(
                    "Threads carousel: skip image for post {PostId} ({Code}: {Message})",
                    request.PostId, childResult.ErrorCode, childResult.ErrorMessage);
                continue;
            }

            childIds.Add(childResult.PublishedExternalId);
        }

        // Carousel cần tối thiểu 2 children — còn 1 ảnh thì rơi về bài IMAGE thường.
        if (childIds.Count == 1)
            return await CreateContainerAsync(request, th, text, imageUrls[0], ct);

        var parentForm = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken!,
            ["media_type"] = "CAROUSEL",
            ["children"] = string.Join(",", childIds),
            ["text"] = text
        };

        var result = await SendAsync(
            url, new FormUrlEncodedContent(parentForm), request.PostId, "create-carousel", ct);

        if (result.Success && string.IsNullOrWhiteSpace(result.PublishedExternalId))
            return SocialPublishResult.Failed(
                "THREADS_INVALID_RESPONSE", "Threads returned no carousel container id.",
                result.RawResponseSanitized);

        return result;
    }

    private async Task<SocialPublishResult> PublishContainerAsync(
        SocialPublishRequest request, ThreadsPublishOptions th,
        string creationId, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken!,
            ["creation_id"] = creationId
        };

        var url = BuildUrl(th, request.PageExternalId, "threads_publish");
        var result = await SendAsync(url, new FormUrlEncodedContent(form), request.PostId, "publish", ct);

        if (result.Success && string.IsNullOrWhiteSpace(result.PublishedExternalId))
            return SocialPublishResult.Failed("THREADS_INVALID_RESPONSE", "Threads returned no media id.", result.RawResponseSanitized);

        return result;
    }

    private async Task<string?> TryFetchPermalinkAsync(
        ThreadsPublishOptions th, string mediaId, string accessToken, CancellationToken ct)
    {
        var url = $"{th.GraphBaseUrl.TrimEnd('/')}/{th.GraphVersion}/{Uri.EscapeDataString(mediaId)}" +
                  $"?fields=permalink&access_token={Uri.EscapeDataString(accessToken)}";

        try
        {
            using var response = await httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("permalink", out var p) ? p.GetString() : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Threads permalink lookup failed for media {MediaId}", mediaId);
            return null;
        }
    }

    private async Task<SocialPublishResult> SendAsync(
        string url, HttpContent content, Guid postId, string step, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return SocialPublishResult.Failed(
                "THREADS_TRANSIENT", $"Threads API timed out at step '{step}'.");
        }
        catch (HttpRequestException ex)
        {
            return SocialPublishResult.Failed(
                "THREADS_TRANSIENT", $"Threads API unreachable at step '{step}': {ex.Message}");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var sanitized = SanitizeResponse(body);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Threads {Step} failed for post {PostId} (HTTP {Status}): {Body}",
                    step, postId, (int)response.StatusCode, sanitized);
                return MapThreadsError(response, sanitized);
            }

            try
            {
                using var doc = JsonDocument.Parse(sanitized);
                var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                return SocialPublishResult.Succeeded(id ?? string.Empty, string.Empty, sanitized);
            }
            catch (JsonException)
            {
                return SocialPublishResult.Failed(
                    "THREADS_INVALID_RESPONSE", "Threads response format is invalid.", sanitized);
            }
        }
    }

    /// <summary>
    /// Dựng danh sách URL ảnh tuyệt đối cho Threads (cover đứng đầu). Ưu tiên MediaItems;
    /// fallback field MediaPreviewUrl đơn lẻ. URL tương đối (/api/mediaasset/...) được
    /// ghép với PublicBaseUrl vì Threads phải tự tải ảnh về từ URL công khai.
    /// </summary>
    private static List<string> ResolvePublicImageUrls(SocialPublishRequest request, ThreadsPublishOptions th)
    {
        var candidates = request.MediaItems.Count > 0
            ? request.MediaItems.Select(x => x.PublicUrl)
            : [request.MediaPreviewUrl];

        return candidates
            .Select(u => ResolvePublicImageUrl(u, th))
            .Where(u => u is not null)
            .Select(u => u!)
            .Distinct()
            .ToList();
    }

    private static string? ResolvePublicImageUrl(string? previewUrl, ThreadsPublishOptions th)
    {
        if (SocialPublishUrlHelper.IsPubliclyAccessibleUrl(previewUrl))
            return previewUrl;

        if (string.IsNullOrWhiteSpace(th.PublicBaseUrl) || string.IsNullOrWhiteSpace(previewUrl))
            return null;

        var baseUrl = th.PublicBaseUrl.TrimEnd('/');
        var path = previewUrl.Trim();
        if (!path.StartsWith('/')) path = "/" + path;

        var candidate = baseUrl + path;
        return SocialPublishUrlHelper.IsPubliclyAccessibleUrl(candidate) ? candidate : null;
    }

    private static bool HasMediaButNoPublicUrl(SocialPublishRequest request)
        => request.MediaItems.Count > 0
            || !string.IsNullOrWhiteSpace(request.MediaStorageKey)
            || !string.IsNullOrWhiteSpace(request.MediaPreviewUrl);

    /// <summary>Cắt caption về giới hạn của Threads, ưu tiên cắt ở ranh giới từ.</summary>
    private static (string Text, bool Truncated) NormalizeText(string? caption, int maxLength)
    {
        var text = (caption ?? string.Empty).Trim();
        if (maxLength <= 0 || text.Length <= maxLength)
            return (text, false);

        var slice = text[..maxLength];
        var lastSpace = slice.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
            slice = slice[..lastSpace];

        return (slice.TrimEnd() + "…", true);
    }

    private static string BuildUrl(ThreadsPublishOptions th, string userId, string endpoint)
        => $"{th.GraphBaseUrl.TrimEnd('/')}/{th.GraphVersion}/{userId.Trim()}/{endpoint}";

    private static SocialPublishResult MapThreadsError(HttpResponseMessage response, string sanitized)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ThreadsErrorResponse>(sanitized, JsonOptions)?.Error;
            var code = error?.Code ?? (int)response.StatusCode;
            var message = error?.Message ?? $"Threads API returned HTTP {(int)response.StatusCode}";

            if (code is 190 or 102 or 463 or 467)
                return SocialPublishResult.Failed("THREADS_TOKEN_INVALID", message, sanitized);

            if (code is 10 or 200 or 294)
                return SocialPublishResult.Failed("THREADS_PERMISSION_DENIED", message, sanitized);

            // 4/17/32/613 = các biến thể vượt hạn mức. Threads chặn cứng 250 bài/24h mỗi profile.
            if (code is 4 or 17 or 32 or 613 || (int)response.StatusCode == 429)
                return SocialPublishResult.Failed("THREADS_RATE_LIMIT", message, sanitized);

            if ((int)response.StatusCode >= 500 || code is 1 or 2)
                return SocialPublishResult.Failed("THREADS_TRANSIENT", message, sanitized);

            return SocialPublishResult.Failed("THREADS_API_ERROR", message, sanitized);
        }
        catch
        {
            return SocialPublishResult.Failed(
                "THREADS_API_ERROR",
                $"Threads API returned HTTP {(int)response.StatusCode}",
                sanitized);
        }
    }

    private static string SanitizeResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return body;
        return AccessTokenPattern().Replace(body, "\"access_token\":\"[redacted]\"");
    }

    [GeneratedRegex(@"""access_token""\s*:\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenPattern();

    private sealed class ThreadsErrorResponse
    {
        public ThreadsError? Error { get; set; }
    }

    private sealed class ThreadsError
    {
        public string? Message { get; set; }
        public int Code { get; set; }
        public string? Type { get; set; }
    }
}
