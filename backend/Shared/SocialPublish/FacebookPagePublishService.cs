using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Modules.SocialChannel.Enums;
using Microsoft.Extensions.Options;

namespace Backend.Shared.SocialPublish;

public partial class FacebookPagePublishService(
    HttpClient httpClient,
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

        var fb = options.Value.Facebook;
        var usePhoto = SocialPublishUrlHelper.IsPubliclyAccessibleUrl(request.MediaPreviewUrl);
        var endpoint = usePhoto ? "photos" : "feed";

        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken
        };

        if (usePhoto)
        {
            form["url"] = request.MediaPreviewUrl!;
            if (!string.IsNullOrWhiteSpace(request.Caption))
                form["caption"] = request.Caption;
        }
        else
        {
            form["message"] = request.Caption;
            if (!string.IsNullOrWhiteSpace(request.Link))
                form["link"] = request.Link;
        }

        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{request.PageExternalId.Trim()}/{endpoint}";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };

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
            logger.LogWarning(ex, "Facebook publish HTTP error for post {PostId}", request.PostId);
            return SocialPublishResult.Failed("FB_NETWORK_ERROR", "Facebook API request failed.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var sanitized = SanitizeFacebookResponse(body);

        if (!response.IsSuccessStatusCode)
            return MapFacebookError(response, sanitized);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var externalId = doc.RootElement.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(externalId))
                return SocialPublishResult.Failed("FB_INVALID_RESPONSE", "Facebook returned no post id.", sanitized);

            var publishedUrl = $"https://www.facebook.com/{externalId}";
            logger.LogInformation(
                "Facebook publish succeeded for post {PostId}, externalId {ExternalId}",
                request.PostId, externalId);

            return SocialPublishResult.Succeeded(externalId, publishedUrl, sanitized);
        }
        catch (Exception)
        {
            return SocialPublishResult.Failed("FB_INVALID_RESPONSE", "Facebook response format is invalid.", sanitized);
        }
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
