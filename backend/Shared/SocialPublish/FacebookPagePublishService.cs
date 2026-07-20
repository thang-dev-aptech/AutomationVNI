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

        var hasLocalMedia = !string.IsNullOrWhiteSpace(request.MediaStorageKey);
        var hasPublicUrl = SocialPublishUrlHelper.IsPubliclyAccessibleUrl(request.MediaPreviewUrl);

        if (hasLocalMedia)
            return await PublishPhotoMultipartAsync(request, ct);

        if (hasPublicUrl)
            return await PublishPhotoByUrlAsync(request, ct);

        // Không có ảnh reachable → đăng text-only (log cảnh báo).
        logger.LogWarning(
            "Facebook publish for post {PostId} has no uploadable media (storage/public url). Falling back to text feed post.",
            request.PostId);
        return await PublishFeedTextAsync(request, ct);
    }

    private async Task<SocialPublishResult> PublishPhotoMultipartAsync(
        SocialPublishRequest request, CancellationToken ct)
    {
        var storageKey = request.MediaStorageKey!.Trim();
        if (!await fileStorage.ExistsAsync(storageKey, ct))
        {
            return SocialPublishResult.Failed(
                "FB_MEDIA_MISSING",
                $"Media file not found in storage: {storageKey}");
        }

        var fb = options.Value.Facebook;
        var url = BuildGraphUrl(fb, request.PageExternalId, "photos");
        var fileName = string.IsNullOrWhiteSpace(request.MediaFileName)
            ? Path.GetFileName(storageKey)
            : request.MediaFileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "photo.jpg";

        var mime = string.IsNullOrWhiteSpace(request.MediaMimeType)
            ? GuessMimeType(fileName)
            : request.MediaMimeType.Trim();

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
        SocialPublishRequest request, CancellationToken ct)
    {
        var fb = options.Value.Facebook;
        var url = BuildGraphUrl(fb, request.PageExternalId, "photos");
        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken!,
            ["url"] = request.MediaPreviewUrl!
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
