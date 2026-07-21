using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared.Storage;
using Microsoft.Extensions.Options;

namespace Backend.Shared.SocialPublish;

public partial class FacebookPagePublishService(
    HttpClient httpClient,
    IFileStorageService fileStorage,
    IOptions<SocialPublishOptions> options,
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

        if (mediaItems.Count > 1)
            return await PublishMultiPhotoFeedAsync(request, mediaItems, ct);

        if (mediaItems.Count == 1)
        {
            var item = mediaItems[0];
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
