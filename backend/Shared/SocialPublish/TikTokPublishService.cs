using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Backend.Modules.Post.Enums;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Storage;
using Microsoft.Extensions.Options;

namespace Backend.Shared.SocialPublish;

/// <summary>
/// TikTok Content Posting API v2. Khác Facebook/Threads ở chỗ auth luôn qua header
/// "Authorization: Bearer {token}" (không phải form field/query param), và MỌI response —
/// kể cả HTTP 200 — vẫn bọc trong {data:{...}, error:{code,message}}, phải tự kiểm error.code
/// != "ok" mới biết thật sự thành công.
/// </summary>
public class TikTokPublishService(
    HttpClient httpClient,
    IFileStorageService fileStorage,
    IOptions<SocialPublishOptions> options,
    ILogger<TikTokPublishService> logger)
{
    public async Task<SocialPublishResult> PublishAsync(
        SocialPublishRequest request, CancellationToken ct = default)
    {
        if (request.Platform != SocialPlatform.TikTok)
            return SocialPublishResult.Failed("TIKTOK_UNSUPPORTED_PLATFORM", "Only TikTok accounts are supported.");

        if (string.IsNullOrWhiteSpace(request.AccessToken))
            return SocialPublishResult.Failed("TIKTOK_TOKEN_MISSING", "TikTok access token is required.");

        var mediaItems = ResolveMediaItems(request);

        // Khác Facebook (fallback đăng text-only khi thiếu ảnh) — TikTok không có post chỉ chữ.
        if (mediaItems.Count == 0)
            return SocialPublishResult.Failed(
                "TIKTOK_UNSUPPORTED_FORMAT",
                "TikTok không hỗ trợ bài chỉ có chữ — cần ít nhất 1 ảnh hoặc 1 video.");

        // Route theo mimetype giống FacebookPagePublishService — đúng đúng 1 media video thì
        // đây là bài Reels (ReelsRender đã xoá hết ảnh khung hình, chỉ giữ video làm Cover).
        var videoItem = mediaItems.Count == 1
            && mediaItems[0].MimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
                ? mediaItems[0]
                : null;

        if (request.TikTokPostMode == TikTokPostMode.InboxDraft)
        {
            // Inbox Draft (MEDIA_UPLOAD) chỉ hỗ trợ video ở v1 — TikTok cũng có draft cho ảnh
            // nhưng endpoint chưa xác nhận rõ. Chọn InboxDraft mà media là ảnh → fallback về
            // DirectPost thay vì lỗi cứng, log lại để biết đã fallback.
            if (videoItem is not null)
                return await PublishVideoToInboxAsync(request, videoItem, ct);

            logger.LogWarning(
                "Post {PostId} chọn TikTok Inbox Draft nhưng media không phải video đơn — fallback DirectPost",
                request.PostId);
        }

        return videoItem is not null
            ? await PublishVideoAsync(request, videoItem, ct)
            : await PublishPhotoPostAsync(request, mediaItems, ct);
    }

    /// <summary>TikTok Content Posting API không có endpoint bình luận công khai.</summary>
    public Task<SocialPublishResult> CommentAsync(
        SocialCommentPublishRequest request, CancellationToken ct = default)
        => Task.FromResult(SocialPublishResult.Failed(
            "TIKTOK_COMMENT_UNSUPPORTED", "TikTok Content Posting API không hỗ trợ đăng bình luận qua API."));

    /// <summary>Cùng logic ResolveMediaItems của FacebookPagePublishService — private ở đó nên viết lại ở đây.</summary>
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
    /// Init (FILE_UPLOAD) → upload cả file trong 1 PUT → poll status. Giống cách
    /// FacebookPagePublishService.PublishReelAsync upload nguyên file trong 1 lần (offset=0,
    /// file_size=toàn bộ) thay vì chia chunk thật — MVP không cần logic multi-chunk phức tạp,
    /// TikTok cho phép total_chunk_count=1 khi gửi nguyên file.
    /// </summary>
    private async Task<SocialPublishResult> PublishVideoAsync(
        SocialPublishRequest request, SocialPublishMediaItem item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.StorageKey))
            return SocialPublishResult.Failed("TIKTOK_MEDIA_MISSING", "Video TikTok cần file lưu trong storage nội bộ.");
        if (!await fileStorage.ExistsAsync(item.StorageKey, ct))
            return SocialPublishResult.Failed("TIKTOK_MEDIA_MISSING", $"Không tìm thấy file video: {item.StorageKey}");

        var tk = options.Value.TikTok;

        long videoSize;
        try
        {
            await using var probeStream = await fileStorage.OpenReadAsync(item.StorageKey, ct);
            videoSize = probeStream.Length;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TikTok video probe lỗi cho post {PostId}", request.PostId);
            return SocialPublishResult.Failed("TIKTOK_MEDIA_MISSING", "Không đọc được file video.");
        }

        string publishId;
        string uploadUrl;
        try
        {
            var initBody = JsonSerializer.Serialize(new
            {
                post_info = new
                {
                    title = TrimCaption(request.Caption, tk),
                    privacy_level = tk.DefaultPrivacyLevel,
                    disable_duet = false,
                    disable_comment = false,
                    disable_stitch = false
                },
                source_info = new
                {
                    source = "FILE_UPLOAD",
                    video_size = videoSize,
                    chunk_size = videoSize,
                    total_chunk_count = 1
                }
            });

            var initUrl = $"{tk.ApiBaseUrl.TrimEnd('/')}/{tk.ApiVersion}/post/publish/video/init/";
            using var initRequest = BuildJsonRequest(initUrl, initBody, request.AccessToken!);
            var initResponse = await httpClient.SendAsync(initRequest, ct);
            var initRawBody = await initResponse.Content.ReadAsStringAsync(ct);

            if (!initResponse.IsSuccessStatusCode)
                return MapTikTokHttpError(initResponse, initRawBody);

            using var initDoc = JsonDocument.Parse(initRawBody);
            var bodyError = TryGetTikTokBodyError(initDoc.RootElement);
            if (bodyError is not null)
                return bodyError;

            var data = initDoc.RootElement.GetProperty("data");
            publishId = data.TryGetProperty("publish_id", out var pidEl) ? pidEl.GetString() ?? "" : "";
            uploadUrl = data.TryGetProperty("upload_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(publishId) || string.IsNullOrWhiteSpace(uploadUrl))
                return SocialPublishResult.Failed("TIKTOK_INVALID_RESPONSE", "TikTok không trả publish_id/upload_url.", initRawBody);
        }
        catch (TaskCanceledException)
        {
            return SocialPublishResult.Failed("TIKTOK_TIMEOUT", "TikTok API request timed out (init video).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TikTok video init lỗi cho post {PostId}", request.PostId);
            return SocialPublishResult.Failed("TIKTOK_NETWORK_ERROR", "TikTok API request failed (init video).");
        }

        try
        {
            // upload_url do TikTok cấp sẵn có chữ ký (presigned) — không cần header Authorization.
            await using var videoStream = await fileStorage.OpenReadAsync(item.StorageKey, ct);
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = new StreamContent(videoStream)
            };
            uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(item.MimeType) ? "video/mp4" : item.MimeType);
            uploadRequest.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, videoSize - 1, videoSize);

            var uploadResponse = await httpClient.SendAsync(uploadRequest, ct);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var uploadBody = await uploadResponse.Content.ReadAsStringAsync(ct);
                return MapTikTokHttpError(uploadResponse, uploadBody);
            }
        }
        catch (TaskCanceledException)
        {
            return SocialPublishResult.Failed("TIKTOK_TIMEOUT", "TikTok API request timed out (upload video).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TikTok video upload lỗi cho post {PostId}, publish_id {PublishId}", request.PostId, publishId);
            return SocialPublishResult.Failed("TIKTOK_MEDIA_UPLOAD_FAILED", "TikTok API request failed (upload video).");
        }

        return await PollPublishStatusAsync(request, publishId, tk, ct);
    }

    /// <summary>
    /// Inbox Draft (MEDIA_UPLOAD) — gửi video vào Inbox app TikTok thật của người dùng thay vì
    /// đăng thẳng. Khác PublishVideoAsync ở 2 chỗ: (1) endpoint riêng
    /// /v2/post/publish/inbox/video/init/, (2) body KHÔNG có post_info (caption/privacy) — TikTok
    /// từ chối request nếu gửi kèm, vì caption/quyền riêng tư/nhạc do người dùng tự chọn trong app.
    /// Upload byte và cách poll status giữ nguyên hệt PublishVideoAsync, chỉ khác trạng thái thành
    /// công là SEND_TO_USER_INBOX thay vì PUBLISH_COMPLETE.
    /// </summary>
    private async Task<SocialPublishResult> PublishVideoToInboxAsync(
        SocialPublishRequest request, SocialPublishMediaItem item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.StorageKey))
            return SocialPublishResult.Failed("TIKTOK_MEDIA_MISSING", "Video TikTok cần file lưu trong storage nội bộ.");
        if (!await fileStorage.ExistsAsync(item.StorageKey, ct))
            return SocialPublishResult.Failed("TIKTOK_MEDIA_MISSING", $"Không tìm thấy file video: {item.StorageKey}");

        var tk = options.Value.TikTok;

        long videoSize;
        try
        {
            await using var probeStream = await fileStorage.OpenReadAsync(item.StorageKey, ct);
            videoSize = probeStream.Length;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TikTok inbox video probe lỗi cho post {PostId}", request.PostId);
            return SocialPublishResult.Failed("TIKTOK_MEDIA_MISSING", "Không đọc được file video.");
        }

        string publishId;
        string uploadUrl;
        try
        {
            var initBody = JsonSerializer.Serialize(new
            {
                source_info = new
                {
                    source = "FILE_UPLOAD",
                    video_size = videoSize,
                    chunk_size = videoSize,
                    total_chunk_count = 1
                }
            });

            var initUrl = $"{tk.ApiBaseUrl.TrimEnd('/')}/{tk.ApiVersion}/post/publish/inbox/video/init/";
            using var initRequest = BuildJsonRequest(initUrl, initBody, request.AccessToken!);
            var initResponse = await httpClient.SendAsync(initRequest, ct);
            var initRawBody = await initResponse.Content.ReadAsStringAsync(ct);

            if (!initResponse.IsSuccessStatusCode)
                return MapTikTokHttpError(initResponse, initRawBody);

            using var initDoc = JsonDocument.Parse(initRawBody);
            var bodyError = TryGetTikTokBodyError(initDoc.RootElement);
            if (bodyError is not null)
                return bodyError;

            var data = initDoc.RootElement.GetProperty("data");
            publishId = data.TryGetProperty("publish_id", out var pidEl) ? pidEl.GetString() ?? "" : "";
            uploadUrl = data.TryGetProperty("upload_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(publishId) || string.IsNullOrWhiteSpace(uploadUrl))
                return SocialPublishResult.Failed("TIKTOK_INVALID_RESPONSE", "TikTok không trả publish_id/upload_url.", initRawBody);
        }
        catch (TaskCanceledException)
        {
            return SocialPublishResult.Failed("TIKTOK_TIMEOUT", "TikTok API request timed out (init inbox video).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TikTok inbox video init lỗi cho post {PostId}", request.PostId);
            return SocialPublishResult.Failed("TIKTOK_NETWORK_ERROR", "TikTok API request failed (init inbox video).");
        }

        try
        {
            await using var videoStream = await fileStorage.OpenReadAsync(item.StorageKey, ct);
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = new StreamContent(videoStream)
            };
            uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(item.MimeType) ? "video/mp4" : item.MimeType);
            uploadRequest.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, videoSize - 1, videoSize);

            var uploadResponse = await httpClient.SendAsync(uploadRequest, ct);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var uploadBody = await uploadResponse.Content.ReadAsStringAsync(ct);
                return MapTikTokHttpError(uploadResponse, uploadBody);
            }
        }
        catch (TaskCanceledException)
        {
            return SocialPublishResult.Failed("TIKTOK_TIMEOUT", "TikTok API request timed out (upload inbox video).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TikTok inbox video upload lỗi cho post {PostId}, publish_id {PublishId}", request.PostId, publishId);
            return SocialPublishResult.Failed("TIKTOK_MEDIA_UPLOAD_FAILED", "TikTok API request failed (upload inbox video).");
        }

        return await PollPublishStatusAsync(request, publishId, tk, ct, successStatus: "SEND_TO_USER_INBOX");
    }

    /// <summary>
    /// Photo post dùng PULL_FROM_URL — TikTok tự tải ảnh từ URL công khai, không cần upload nhị phân.
    /// Ảnh phải là URL https thật (không localhost) — xem ResolvePublicImageUrl.
    /// </summary>
    private async Task<SocialPublishResult> PublishPhotoPostAsync(
        SocialPublishRequest request, List<SocialPublishMediaItem> items, CancellationToken ct)
    {
        var tk = options.Value.TikTok;
        var photoUrls = items
            .Select(i => ResolvePublicImageUrl(i.PublicUrl, tk))
            .Where(u => u is not null)
            .Select(u => u!)
            .ToList();

        if (photoUrls.Count == 0)
            return SocialPublishResult.Failed(
                "TIKTOK_MEDIA_MISSING",
                "Không có ảnh nào truy cập công khai được — kiểm tra cấu hình SocialPublish:TikTok:PublicBaseUrl.");

        string publishId;
        try
        {
            var initBody = JsonSerializer.Serialize(new
            {
                post_info = new
                {
                    title = TrimCaption(request.Caption, tk),
                    description = TrimCaption(request.Caption, tk),
                    privacy_level = tk.DefaultPrivacyLevel,
                    disable_comment = false,
                    auto_add_music = true
                },
                source_info = new
                {
                    source = "PULL_FROM_URL",
                    photo_cover_index = 0,
                    photo_images = photoUrls
                },
                post_mode = "DIRECT_POST",
                media_type = "PHOTO"
            });

            var initUrl = $"{tk.ApiBaseUrl.TrimEnd('/')}/{tk.ApiVersion}/post/publish/content/init/";
            using var initRequest = BuildJsonRequest(initUrl, initBody, request.AccessToken!);
            var initResponse = await httpClient.SendAsync(initRequest, ct);
            var initRawBody = await initResponse.Content.ReadAsStringAsync(ct);

            if (!initResponse.IsSuccessStatusCode)
                return MapTikTokHttpError(initResponse, initRawBody);

            using var initDoc = JsonDocument.Parse(initRawBody);
            var bodyError = TryGetTikTokBodyError(initDoc.RootElement);
            if (bodyError is not null)
                return bodyError;

            var data = initDoc.RootElement.GetProperty("data");
            publishId = data.TryGetProperty("publish_id", out var pidEl) ? pidEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(publishId))
                return SocialPublishResult.Failed("TIKTOK_INVALID_RESPONSE", "TikTok không trả publish_id.", initRawBody);
        }
        catch (TaskCanceledException)
        {
            return SocialPublishResult.Failed("TIKTOK_TIMEOUT", "TikTok API request timed out (init photo post).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TikTok photo post init lỗi cho post {PostId}", request.PostId);
            return SocialPublishResult.Failed("TIKTOK_NETWORK_ERROR", "TikTok API request failed (init photo post).");
        }

        return await PollPublishStatusAsync(request, publishId, tk, ct);
    }

    /// <summary>
    /// TikTok xử lý bất đồng bộ sau init — phải poll publish_id tới khi PUBLISH_COMPLETE/FAILED.
    /// publish_id không map ra được URL công khai thật (TikTok chỉ gửi post id qua webhook riêng,
    /// chưa triển khai) nên PublishedUrl dùng tạm link hồ sơ — chấp nhận được vì mục tiêu chính
    /// là biết bài đã lên hay chưa, không phải link chia sẻ.
    /// </summary>
    private async Task<SocialPublishResult> PollPublishStatusAsync(
        SocialPublishRequest request, string publishId, TikTokPublishOptions tk, CancellationToken ct,
        string successStatus = "PUBLISH_COMPLETE")
    {
        var statusUrl = $"{tk.ApiBaseUrl.TrimEnd('/')}/{tk.ApiVersion}/post/publish/status/fetch/";
        var pollDeadline = DateTime.UtcNow.AddSeconds(tk.PublishTimeoutSeconds);

        while (DateTime.UtcNow < pollDeadline)
        {
            try
            {
                var body = JsonSerializer.Serialize(new { publish_id = publishId });
                using var statusRequest = BuildJsonRequest(statusUrl, body, request.AccessToken!);
                var statusResponse = await httpClient.SendAsync(statusRequest, ct);
                var statusBody = await statusResponse.Content.ReadAsStringAsync(ct);

                if (statusResponse.IsSuccessStatusCode)
                {
                    using var statusDoc = JsonDocument.Parse(statusBody);
                    if (statusDoc.RootElement.TryGetProperty("data", out var data)
                        && data.ValueKind == JsonValueKind.Object
                        && data.TryGetProperty("status", out var statusEl))
                    {
                        var status = statusEl.GetString();

                        if (status == successStatus)
                        {
                            logger.LogInformation(
                                "TikTok publish reached {Status} for post {PostId}, publish_id {PublishId}",
                                status, request.PostId, publishId);
                            // Inbox Draft: TikTok chưa thật sự đăng gì — người dùng còn phải tự mở app
                            // hoàn tất, nên KHÔNG có URL bài thật để trả (khác PUBLISH_COMPLETE, nơi
                            // bài đã lên và publishId map được vào 1 link hồ sơ tạm).
                            var publishedUrl = successStatus == "PUBLISH_COMPLETE"
                                ? $"https://www.tiktok.com/@i/status/{publishId}"
                                : string.Empty;
                            return SocialPublishResult.Succeeded(publishId, publishedUrl, statusBody);
                        }

                        if (status == "FAILED")
                        {
                            var failReason = data.TryGetProperty("fail_reason", out var fr) ? fr.GetString() : "unknown";
                            return SocialPublishResult.Failed(
                                "TIKTOK_API_ERROR", $"TikTok publish failed: {failReason}", statusBody);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TikTok poll status lỗi cho publish_id {PublishId} — thử lại", publishId);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        return SocialPublishResult.Failed(
            "TIKTOK_TIMEOUT",
            $"TikTok chưa xử lý xong publish sau {tk.PublishTimeoutSeconds}s (publish_id={publishId}).");
    }

    private static HttpRequestMessage BuildJsonRequest(string url, string jsonBody, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static string TrimCaption(string? caption, TikTokPublishOptions tk)
    {
        var text = caption ?? string.Empty;
        return text.Length > tk.MaxCaptionLength ? text[..tk.MaxCaptionLength] : text;
    }

    /// <summary>
    /// Dựng URL ảnh tuyệt đối cho TikTok PULL_FROM_URL (cùng logic ThreadsPublishService.ResolvePublicImageUrl
    /// vì lý do hệt nhau: cả 2 platform đều tự đi tải ảnh, cần URL https công khai thật sự).
    /// </summary>
    private static string? ResolvePublicImageUrl(string? previewUrl, TikTokPublishOptions tk)
    {
        if (SocialPublishUrlHelper.IsPubliclyAccessibleUrl(previewUrl))
            return previewUrl;

        if (string.IsNullOrWhiteSpace(tk.PublicBaseUrl) || string.IsNullOrWhiteSpace(previewUrl))
            return null;

        var baseUrl = tk.PublicBaseUrl.TrimEnd('/');
        var path = previewUrl.Trim();
        if (!path.StartsWith('/')) path = "/" + path;

        var candidate = baseUrl + path;
        return SocialPublishUrlHelper.IsPubliclyAccessibleUrl(candidate) ? candidate : null;
    }

    /// <summary>
    /// TikTok luôn trả HTTP 200 dù lỗi nghiệp vụ — lỗi thật nằm trong error.code (!= "ok") bên
    /// trong body. Trả null khi không có lỗi.
    /// </summary>
    private static SocialPublishResult? TryGetTikTokBodyError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var errEl) || errEl.ValueKind != JsonValueKind.Object)
            return null;

        var code = errEl.TryGetProperty("code", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(code) || string.Equals(code, "ok", StringComparison.OrdinalIgnoreCase))
            return null;

        var message = errEl.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
        return SocialPublishResult.Failed(MapTikTokErrorCode(code), $"{message} (code {code})", root.GetRawText());
    }

    /// <summary>Lỗi tầng HTTP (4xx/5xx) — body vẫn theo hình dạng {error:{code,message}}.</summary>
    private static SocialPublishResult MapTikTokHttpError(HttpResponseMessage response, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.Object)
            {
                var code = errEl.TryGetProperty("code", out var c) ? c.GetString() : null;
                var message = errEl.TryGetProperty("message", out var m) ? m.GetString() : null;
                var text = string.IsNullOrWhiteSpace(message) ? $"TikTok API returned HTTP {(int)response.StatusCode}" : message!;
                return SocialPublishResult.Failed(
                    MapTikTokErrorCode(code), code is null ? text : $"{text} (code {code})", body);
            }
        }
        catch (JsonException)
        {
            // Non-JSON body — rơi xuống nhánh transient/generic bên dưới.
        }

        if ((int)response.StatusCode >= 500)
            return SocialPublishResult.Failed(
                "TIKTOK_TRANSIENT", $"TikTok API returned HTTP {(int)response.StatusCode}", body);

        return SocialPublishResult.Failed(
            "TIKTOK_API_ERROR", $"TikTok API returned HTTP {(int)response.StatusCode}", body);
    }

    /// <summary>Map error.code string của TikTok sang mã lỗi nội bộ TIKTOK_* — theo convention FB_*/THREADS_*.</summary>
    private static string MapTikTokErrorCode(string? tikTokCode) => tikTokCode switch
    {
        "access_token_invalid" => "TIKTOK_TOKEN_INVALID",
        "scope_not_authorized" or "scope_permission_missing" or "unaudited_client_can_only_post_to_private_accounts"
            => "TIKTOK_PERMISSION_DENIED",
        "rate_limit_exceeded" or "spam_risk_too_many_posts" or "spam_risk_user_banned_from_posting"
            or "reached_active_user_cap" => "TIKTOK_RATE_LIMIT",
        "invalid_file_upload" or "video_pull_failed" or "picture_size_check_failed" or "url_ownership_unverified"
            => "TIKTOK_MEDIA_UPLOAD_FAILED",
        "internal_error" => "TIKTOK_TRANSIENT",
        _ => "TIKTOK_API_ERROR"
    };
}
