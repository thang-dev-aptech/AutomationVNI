using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Storage;
using Microsoft.Extensions.Options;

namespace Backend.Shared.SocialPublish;

public partial class FacebookPagePublishService(
    HttpClient httpClient,
    IHttpClientFactory httpClientFactory,
    IFileStorageService fileStorage,
    IOptions<SocialPublishOptions> options,
    IOptions<ReelsOptions> reelsOptions,
    ILogger<FacebookPagePublishService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<SocialPublishResult> PublishAsync(
        SocialPublishRequest request, CancellationToken ct = default)
    {
        if (request.Platform != SocialPlatform.Facebook)
            return SocialPublishResult.Failed("FB_UNSUPPORTED_PLATFORM", "Only Facebook pages are supported.");

        if (string.IsNullOrWhiteSpace(request.PageExternalId))
            return SocialPublishResult.Failed("FB_PAGE_ID_MISSING", "Facebook Page ID (ExternalPageId) is required.");

        if (string.IsNullOrWhiteSpace(request.AccessToken))
            return SocialPublishResult.Failed("FB_TOKEN_MISSING", "Facebook page access token is required.");

        var mediaItems = ResolveMediaItems(request);

        // Chốt chặn cuối: ảnh placeholder (PNG 1x1, ~70 byte) sinh ra khi provider ảnh lỗi.
        // Thà không đăng còn hơn đăng lên Page một ô ảnh rỗng.
        foreach (var item in mediaItems.Where(x => !string.IsNullOrWhiteSpace(x.StorageKey)))
        {
            var placeholderKey = await IsPlaceholderImageAsync(item.StorageKey!, ct);
            if (placeholderKey)
            {
                logger.LogError(
                    "Chặn đăng post {PostId}: ảnh {StorageKey} là placeholder hỏng, không phải ảnh thật",
                    request.PostId, item.StorageKey);
                return SocialPublishResult.Failed(
                    "FB_MEDIA_PLACEHOLDER",
                    "Bài đang dùng ảnh placeholder do sinh ảnh AI thất bại. Hãy bấm Tạo lại ảnh trước khi đăng.");
            }
        }

        if (mediaItems.Count > 1)
            return await PublishMultiPhotoFeedAsync(request, mediaItems, ct);

        if (mediaItems.Count == 1)
        {
            var item = mediaItems[0];

            // Reels: post luôn có đúng 1 media item là video (ReelsRender xoá hết ảnh khung hình,
            // chỉ giữ video làm Cover — xem GenerationJobPipelineService.ProcessReelsRenderAsync).
            // Sniff theo MimeType giống tiền lệ lọc "image/" ở GenerationJobPipelineService.
            if (item.MimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true)
                return await PublishReelAsync(request, item, ct);

            return !string.IsNullOrWhiteSpace(item.StorageKey)
                ? await PublishPhotoMultipartAsync(request, item, ct)
                : await PublishPhotoByUrlAsync(request, item, ct);
        }

        // Không có ảnh reachable → đăng text-only (log cảnh báo).
        logger.LogWarning(
            "Facebook publish for post {PostId} has no uploadable media (storage/public url). Falling back to text feed post.",
            request.PostId);
        return await PublishFeedTextAsync(request, ct);
    }

    /// <summary>
    /// Ảnh placeholder do MockImageGenerator sinh khi provider ảnh lỗi: PNG 1x1, khoảng 70 byte.
    /// Nhận diện bằng dung lượng — ảnh banner thật luôn hàng trăm KB.
    /// </summary>
    private async Task<bool> IsPlaceholderImageAsync(string storageKey, CancellationToken ct)
    {
        const int minRealImageBytes = 2048;
        try
        {
            if (!await fileStorage.ExistsAsync(storageKey, ct)) return false;
            await using var stream = await fileStorage.OpenReadAsync(storageKey, ct);
            if (stream.CanSeek) return stream.Length < minRealImageBytes;

            var buffer = new byte[minRealImageBytes];
            var read = await stream.ReadAsync(buffer, ct);
            return read < minRealImageBytes;
        }
        catch (Exception ex)
        {
            // Không chặn đăng chỉ vì không đọc được file — để các bước sau báo lỗi cụ thể hơn.
            logger.LogWarning(ex, "Không kiểm tra được kích thước ảnh {StorageKey}", storageKey);
            return false;
        }
    }

    /// <summary>
    /// Danh sách ảnh đăng được (có storage key hoặc URL public). Ưu tiên MediaItems;
    /// fallback các field Media* đơn lẻ để tương thích flow cũ.
    /// </summary>
    private static List<SocialPublishMediaItem> ResolveMediaItems(SocialPublishRequest request)
    {
        var items = request.MediaItems
            .Where(x => !string.IsNullOrWhiteSpace(x.StorageKey)
                || SocialPublishUrlHelper.IsPubliclyAccessibleUrl(x.PublicUrl))
            .ToList();
        if (items.Count > 0) return items;

        if (!string.IsNullOrWhiteSpace(request.MediaStorageKey)
            || SocialPublishUrlHelper.IsPubliclyAccessibleUrl(request.MediaPreviewUrl))
        {
            return
            [
                new SocialPublishMediaItem
                {
                    PublicUrl = request.MediaPreviewUrl,
                    StorageKey = request.MediaStorageKey,
                    FileName = request.MediaFileName,
                    MimeType = request.MediaMimeType
                }
            ];
        }

        return [];
    }

    /// <summary>
    /// Đăng nhiều ảnh trong 1 bài: upload từng ảnh dạng unpublished lấy media_fbid,
    /// sau đó tạo bài /feed với attached_media.
    /// </summary>
    private async Task<SocialPublishResult> PublishMultiPhotoFeedAsync(
        SocialPublishRequest request, List<SocialPublishMediaItem> items, CancellationToken ct)
    {
        var photoIds = new List<string>();
        for (var index = 0; index < items.Count; index++)
        {
            var (photoId, error) = await UploadUnpublishedPhotoAsync(request, items[index], index, ct);
            if (error is not null)
            {
                // Ảnh đầu là cover — lỗi thì fail cả bài. Ảnh phụ lỗi thì bỏ qua, vẫn đăng phần còn lại.
                if (index == 0) return error;
                logger.LogWarning(
                    "Facebook multi-photo: skip photo {Index} for post {PostId} ({Code}: {Message})",
                    index, request.PostId, error.ErrorCode, error.ErrorMessage);
                continue;
            }
            photoIds.Add(photoId!);
        }

        if (photoIds.Count == 0)
            return SocialPublishResult.Failed(
                "FB_MEDIA_UPLOAD_FAILED", "No photo could be uploaded for the multi-photo post.");

        var fb = options.Value.Facebook;
        var url = BuildGraphUrl(fb, request.PageExternalId, "feed");
        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken!,
            ["message"] = request.Caption ?? string.Empty
        };
        for (var i = 0; i < photoIds.Count; i++)
            form[$"attached_media[{i}]"] = $"{{\"media_fbid\":\"{photoIds[i]}\"}}";

        logger.LogInformation(
            "Facebook multi-photo feed post for {PostId} with {Count} photos",
            request.PostId, photoIds.Count);

        return await SendGraphAsync(url, new FormUrlEncodedContent(form), request.PostId, ct);
    }

    private async Task<(string? PhotoId, SocialPublishResult? Error)> UploadUnpublishedPhotoAsync(
        SocialPublishRequest request, SocialPublishMediaItem item, int index, CancellationToken ct)
    {
        var fb = options.Value.Facebook;
        var url = BuildGraphUrl(fb, request.PageExternalId, "photos");

        HttpContent content;
        Stream? fileStream = null;
        if (!string.IsNullOrWhiteSpace(item.StorageKey))
        {
            var storageKey = item.StorageKey.Trim();
            if (!await fileStorage.ExistsAsync(storageKey, ct))
            {
                return (null, SocialPublishResult.Failed(
                    "FB_MEDIA_MISSING", $"Media file not found in storage: {storageKey}"));
            }

            var fileName = string.IsNullOrWhiteSpace(item.FileName)
                ? Path.GetFileName(storageKey)
                : item.FileName.Trim();
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"photo-{index}.jpg";

            var mime = string.IsNullOrWhiteSpace(item.MimeType)
                ? GuessMimeType(fileName)
                : item.MimeType.Trim();

            fileStream = await fileStorage.OpenReadAsync(storageKey, ct);
            var multipart = new MultipartFormDataContent
            {
                { new StringContent(request.AccessToken!), "access_token" },
                { new StringContent("false"), "published" }
            };
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(mime);
            multipart.Add(streamContent, "source", fileName);
            content = multipart;
        }
        else
        {
            content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["access_token"] = request.AccessToken!,
                ["url"] = item.PublicUrl!,
                ["published"] = "false"
            });
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(httpRequest, ct);
            }
            catch (TaskCanceledException)
            {
                return (null, SocialPublishResult.Failed("FB_TIMEOUT", "Facebook API request timed out."));
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "Facebook unpublished photo upload HTTP error for post {PostId}", request.PostId);
                return (null, SocialPublishResult.Failed("FB_NETWORK_ERROR", "Facebook API request failed."));
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var sanitized = SanitizeFacebookResponse(body);
            if (!response.IsSuccessStatusCode)
                return (null, MapFacebookError(response, sanitized));

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(id.GetString()))
            {
                return (id.GetString(), null);
            }

            return (null, SocialPublishResult.Failed(
                "FB_INVALID_RESPONSE", "Facebook returned no photo id.", sanitized));
        }
        finally
        {
            if (fileStream is not null) await fileStream.DisposeAsync();
        }
    }

    private async Task<SocialPublishResult> PublishPhotoMultipartAsync(
        SocialPublishRequest request, SocialPublishMediaItem item, CancellationToken ct)
    {
        var storageKey = item.StorageKey!.Trim();
        if (!await fileStorage.ExistsAsync(storageKey, ct))
        {
            return SocialPublishResult.Failed(
                "FB_MEDIA_MISSING",
                $"Media file not found in storage: {storageKey}");
        }

        var fb = options.Value.Facebook;
        var url = BuildGraphUrl(fb, request.PageExternalId, "photos");
        var fileName = string.IsNullOrWhiteSpace(item.FileName)
            ? Path.GetFileName(storageKey)
            : item.FileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "photo.jpg";

        var mime = string.IsNullOrWhiteSpace(item.MimeType)
            ? GuessMimeType(fileName)
            : item.MimeType.Trim();

        await using var fileStream = await fileStorage.OpenReadAsync(storageKey, ct);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(request.AccessToken!), "access_token");
        if (!string.IsNullOrWhiteSpace(request.Caption))
            content.Add(new StringContent(request.Caption), "caption");

        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(mime);
        content.Add(streamContent, "source", fileName);

        logger.LogInformation(
            "Facebook multipart photo upload for post {PostId}, file={FileName}, mime={Mime}",
            request.PostId, fileName, mime);

        return await SendGraphAsync(url, content, request.PostId, ct);
    }

    private async Task<SocialPublishResult> PublishPhotoByUrlAsync(
        SocialPublishRequest request, SocialPublishMediaItem item, CancellationToken ct)
    {
        var fb = options.Value.Facebook;
        var url = BuildGraphUrl(fb, request.PageExternalId, "photos");
        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken!,
            ["url"] = item.PublicUrl!
        };
        if (!string.IsNullOrWhiteSpace(request.Caption))
            form["caption"] = request.Caption;

        return await SendGraphAsync(url, new FormUrlEncodedContent(form), request.PostId, ct);
    }

    private async Task<SocialPublishResult> PublishFeedTextAsync(
        SocialPublishRequest request, CancellationToken ct)
    {
        var fb = options.Value.Facebook;
        var url = BuildGraphUrl(fb, request.PageExternalId, "feed");
        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken!,
            ["message"] = request.Caption ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(request.Link))
            form["link"] = request.Link;

        // Nền màu: Facebook chỉ chấp nhận khi bài không có ảnh và message ≤130 ký tự.
        // Vượt ngưỡng thì API vẫn trả 200 nhưng đăng ra bài chữ thường — hỏng trong im lặng,
        // nên chặn ở đây và ghi log để còn biết đường sửa.
        if (!string.IsNullOrWhiteSpace(request.TextFormatPresetId))
        {
            var message = form["message"] ?? string.Empty;
            if (message.Length <= 130)
            {
                form["text_format_preset_id"] = request.TextFormatPresetId;
            }
            else
            {
                logger.LogWarning(
                    "Post {PostId}: bài dài {Len} ký tự (>130) nên bỏ nền màu, đăng dạng chữ thường",
                    request.PostId, message.Length);
            }
        }

        return await SendGraphAsync(url, new FormUrlEncodedContent(form), request.PostId, ct);
    }

    /// <summary>
    /// Đăng Facebook Reels — quy trình 3 bước riêng của Meta Video API (khác hẳn /photos, /feed):
    /// 1) start (lấy video_id + upload_url) → 2) upload byte video lên rupload.facebook.com (host
    /// riêng, không phải graph.facebook.com) → 3) poll trạng thái xử lý → 4) finish (publish thật).
    /// Reels KHÔNG nhận ảnh tĩnh — bug hiển thị "khung hình đứng yên" nếu cố gửi ảnh vào endpoint
    /// này, nên bài Reels luôn phải có đúng 1 media item là video/mp4 (đảm bảo ở tầng pipeline).
    /// </summary>
    private async Task<SocialPublishResult> PublishReelAsync(
        SocialPublishRequest request, SocialPublishMediaItem item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.StorageKey))
            return SocialPublishResult.Failed("FB_MEDIA_MISSING", "Reels cần file video lưu trong storage nội bộ.");
        if (!await fileStorage.ExistsAsync(item.StorageKey, ct))
            return SocialPublishResult.Failed("FB_MEDIA_MISSING", $"Không tìm thấy file video: {item.StorageKey}");

        var fb = options.Value.Facebook;
        var reels = reelsOptions.Value;

        // Bước 1: start
        var startUrl = BuildGraphUrl(fb, request.PageExternalId, "video_reels");
        var startForm = new Dictionary<string, string>
        {
            ["upload_phase"] = "start",
            ["access_token"] = request.AccessToken!
        };

        string videoId;
        string uploadUrl;
        try
        {
            using var startRequest = new HttpRequestMessage(HttpMethod.Post, startUrl)
            {
                Content = new FormUrlEncodedContent(startForm)
            };
            var startResponse = await httpClient.SendAsync(startRequest, ct);
            var startBody = await startResponse.Content.ReadAsStringAsync(ct);
            var startSanitized = SanitizeFacebookResponse(startBody);
            if (!startResponse.IsSuccessStatusCode)
                return MapFacebookError(startResponse, startSanitized);

            using var startDoc = JsonDocument.Parse(startBody);
            videoId = startDoc.RootElement.TryGetProperty("video_id", out var vidEl) ? vidEl.GetString() ?? "" : "";
            uploadUrl = startDoc.RootElement.TryGetProperty("upload_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(videoId))
                return SocialPublishResult.Failed("FB_INVALID_RESPONSE", "Facebook không trả video_id.", startSanitized);
        }
        catch (TaskCanceledException)
        {
            return SocialPublishResult.Failed("FB_TIMEOUT", "Facebook API request timed out (start reels).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Facebook Reels start lỗi cho post {PostId}", request.PostId);
            return SocialPublishResult.Failed("FB_NETWORK_ERROR", "Facebook API request failed (start reels).");
        }

        // Bước 2: upload byte video — host riêng (rupload.facebook.com), named client tách biệt
        // khỏi client typed đang trỏ graph.facebook.com.
        try
        {
            await using var videoStream = await fileStorage.OpenReadAsync(item.StorageKey, ct);
            var ruploadClient = httpClientFactory.CreateClient("FacebookRupload");
            var effectiveUploadUrl = string.IsNullOrWhiteSpace(uploadUrl)
                ? $"{reels.RuploadBaseUrl.TrimEnd('/')}/video-upload/{fb.GraphVersion}/{videoId}"
                : uploadUrl;

            using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, effectiveUploadUrl);
            uploadRequest.Headers.TryAddWithoutValidation("Authorization", $"OAuth {request.AccessToken}");
            uploadRequest.Headers.TryAddWithoutValidation("offset", "0");
            uploadRequest.Headers.TryAddWithoutValidation("file_size", videoStream.Length.ToString());
            uploadRequest.Content = new StreamContent(videoStream);
            uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var uploadResponse = await ruploadClient.SendAsync(uploadRequest, ct);
            var uploadBody = await uploadResponse.Content.ReadAsStringAsync(ct);
            if (!uploadResponse.IsSuccessStatusCode)
                return MapFacebookError(uploadResponse, SanitizeFacebookResponse(uploadBody));
        }
        catch (TaskCanceledException)
        {
            return SocialPublishResult.Failed("FB_TIMEOUT", "Facebook API request timed out (upload reels video).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Facebook Reels upload lỗi cho post {PostId}, video_id {VideoId}", request.PostId, videoId);
            return SocialPublishResult.Failed("FB_NETWORK_ERROR", "Facebook API request failed (upload reels video).");
        }

        // Bước 3: poll trạng thái xử lý — video còn "processing"/"uploading" thì finish sẽ lỗi.
        // Chạy an toàn ở đây vì publish luôn nằm trong BackgroundService, không chờ HTTP request người dùng.
        var pollDeadline = DateTime.UtcNow.AddSeconds(reels.TimeoutSeconds);
        while (DateTime.UtcNow < pollDeadline)
        {
            try
            {
                var statusUrl = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(videoId)}" +
                    $"?fields=status&access_token={Uri.EscapeDataString(request.AccessToken!)}";
                var statusResponse = await httpClient.GetAsync(statusUrl, ct);
                var statusBody = await statusResponse.Content.ReadAsStringAsync(ct);
                if (statusResponse.IsSuccessStatusCode)
                {
                    using var statusDoc = JsonDocument.Parse(statusBody);
                    var videoStatus = statusDoc.RootElement.TryGetProperty("status", out var statusEl)
                        && statusEl.TryGetProperty("video_status", out var vsEl)
                        ? vsEl.GetString()
                        : null;
                    if (videoStatus is not ("processing" or "uploading"))
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Facebook Reels poll status lỗi cho video_id {VideoId} — thử lại", videoId);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        // Bước 4: finish
        var finishUrl = BuildGraphUrl(fb, request.PageExternalId, "video_reels");
        var finishForm = new Dictionary<string, string>
        {
            ["upload_phase"] = "finish",
            ["video_id"] = videoId,
            ["video_state"] = "PUBLISHED",
            ["access_token"] = request.AccessToken!,
            ["description"] = request.Caption ?? string.Empty
        };

        try
        {
            using var finishRequest = new HttpRequestMessage(HttpMethod.Post, finishUrl)
            {
                Content = new FormUrlEncodedContent(finishForm)
            };
            var finishResponse = await httpClient.SendAsync(finishRequest, ct);
            var finishBody = await finishResponse.Content.ReadAsStringAsync(ct);
            var finishSanitized = SanitizeFacebookResponse(finishBody);
            if (!finishResponse.IsSuccessStatusCode)
                return MapFacebookError(finishResponse, finishSanitized);

            logger.LogInformation(
                "Facebook Reels publish succeeded for post {PostId}, video_id {VideoId}", request.PostId, videoId);

            // Reels không trả post_id ở finish — dùng video_id làm external id, link xem trên Facebook
            // theo dạng /reel/{video_id} (chuẩn URL Reels công khai của Meta).
            return SocialPublishResult.Succeeded(
                videoId, $"https://www.facebook.com/reel/{videoId}", finishSanitized);
        }
        catch (TaskCanceledException)
        {
            return SocialPublishResult.Failed("FB_TIMEOUT", "Facebook API request timed out (finish reels).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Facebook Reels finish lỗi cho post {PostId}, video_id {VideoId}", request.PostId, videoId);
            return SocialPublishResult.Failed("FB_NETWORK_ERROR", "Facebook API request failed (finish reels).");
        }
    }

    /// <summary>
    /// Đăng bình luận vào bài của chính Page. Graph API dùng chung endpoint /{id}/comments cho
    /// cả bài viết lẫn bình luận, nên chỉ cần truyền ID bài.
    /// </summary>
    public async Task<SocialPublishResult> CommentAsync(
        SocialCommentPublishRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalPostId))
            return SocialPublishResult.Failed("FB_POST_ID_MISSING", "Thiếu ID bài để bình luận");
        if (string.IsNullOrWhiteSpace(request.AccessToken))
            return SocialPublishResult.Failed("FB_TOKEN_MISSING", "Thiếu access token");
        if (string.IsNullOrWhiteSpace(request.Message))
            return SocialPublishResult.Failed("FB_EMPTY_COMMENT", "Nội dung bình luận trống");

        var fb = options.Value.Facebook;
        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(request.ExternalPostId)}/comments";
        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken!,
            ["message"] = request.Message.Trim(),
        };
        return await SendGraphAsync(url, new FormUrlEncodedContent(form), request.PostId, ct);
    }

    private async Task<SocialPublishResult> SendGraphAsync(
        string url, HttpContent content, Guid postId, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, ct);
        }
        catch (TaskCanceledException)
        {
            return SocialPublishResult.Failed("FB_TIMEOUT", "Facebook API request timed out.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Facebook publish HTTP error for post {PostId}", postId);
            return SocialPublishResult.Failed("FB_NETWORK_ERROR", "Facebook API request failed.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var sanitized = SanitizeFacebookResponse(body);

        if (!response.IsSuccessStatusCode)
            return MapFacebookError(response, sanitized);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var externalId = ExtractPublishedId(doc.RootElement);
            if (string.IsNullOrWhiteSpace(externalId))
                return SocialPublishResult.Failed("FB_INVALID_RESPONSE", "Facebook returned no post id.", sanitized);

            var publishedUrl = $"https://www.facebook.com/{externalId}";
            logger.LogInformation(
                "Facebook publish succeeded for post {PostId}, externalId {ExternalId}",
                postId, externalId);

            return SocialPublishResult.Succeeded(externalId, publishedUrl, sanitized);
        }
        catch (Exception)
        {
            return SocialPublishResult.Failed("FB_INVALID_RESPONSE", "Facebook response format is invalid.", sanitized);
        }
    }

    private static string BuildGraphUrl(FacebookPublishOptions fb, string pageId, string endpoint)
        => $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{pageId.Trim()}/{endpoint}";

    /// <summary>
    /// /photos thường trả id (photo) + post_id (bài trên tường). Ưu tiên post_id để link đúng feed.
    /// </summary>
    private static string? ExtractPublishedId(JsonElement root)
    {
        if (root.TryGetProperty("post_id", out var postId) && postId.ValueKind == JsonValueKind.String)
        {
            var v = postId.GetString();
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }

        if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            return id.GetString();

        return null;
    }

    private static string GuessMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
    }

    private static SocialPublishResult MapFacebookError(HttpResponseMessage response, string sanitized)
    {
        try
        {
            var error = JsonSerializer.Deserialize<FacebookErrorResponse>(sanitized, JsonOptions)?.Error;
            var code = error?.Code ?? (int)response.StatusCode;
            var message = error?.Message ?? $"Facebook API returned HTTP {(int)response.StatusCode}";

            if (code is 190 or 102 or 463 or 467)
                return SocialPublishResult.Failed("FB_TOKEN_INVALID", message, sanitized);

            if (code is 10 or 200 or 294)
                return SocialPublishResult.Failed("FB_PERMISSION_DENIED", message, sanitized);

            if ((int)response.StatusCode >= 500 || code is 1 or 2)
                return SocialPublishResult.Failed("FB_TRANSIENT", message, sanitized);

            return SocialPublishResult.Failed("FB_API_ERROR", message, sanitized);
        }
        catch
        {
            return SocialPublishResult.Failed(
                "FB_API_ERROR",
                $"Facebook API returned HTTP {(int)response.StatusCode}",
                sanitized);
        }
    }

    private static string SanitizeFacebookResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return body;
        return AccessTokenPattern().Replace(body, "\"access_token\":\"[redacted]\"");
    }

    [GeneratedRegex(@"""access_token""\s*:\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenPattern();

    private sealed class FacebookErrorResponse
    {
        public FacebookError? Error { get; set; }
    }

    private sealed class FacebookError
    {
        public string? Message { get; set; }
        public int Code { get; set; }
        public string? Type { get; set; }
    }
}

public static class SocialPublishUrlHelper
{
    public static bool IsPubliclyAccessibleUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != "https") return false;

        var host = uri.Host.ToLowerInvariant();
        return host is not "localhost" and not "127.0.0.1" and not "::1";
    }
}
