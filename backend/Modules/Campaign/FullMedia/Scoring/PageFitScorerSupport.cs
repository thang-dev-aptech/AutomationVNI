using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Backend.Shared.Ai;

namespace Backend.Modules.Campaign.FullMedia.Scoring;

/// <summary>
/// Tiện ích dùng chung cho VotingPageFitScorer và LlmLogprobPageFitScorer
/// (URL, Retry-After có trần, redact, ước tính chi phí, phân giải provider).
/// </summary>
internal static class PageFitScorerSupport
{
    public static string BuildUrl(AiProviderConfig config)
    {
        var baseUrl = config.BaseUrl.TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(config.ChatCompletionsPath)
            ? "/chat/completions"
            : config.ChatCompletionsPath.StartsWith('/')
                ? config.ChatCompletionsPath
                : "/" + config.ChatCompletionsPath;
        return baseUrl + path;
    }

    public static AiProviderConfig ResolveProvider(
        AiProvidersOptions ai,
        PageFitOptions pageFit)
    {
        var overrideName = pageFit.Provider?.Trim();
        var name = !string.IsNullOrWhiteSpace(overrideName)
            ? overrideName
            : ai.DefaultProvider;

        if (string.IsNullOrWhiteSpace(name)
            || !ai.Providers.TryGetValue(name, out var config)
            || string.IsNullOrWhiteSpace(config.ApiKey)
            || string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            throw new PageFitPermanentException(
                "provider_not_configured",
                "PageFit AI provider missing (name/ApiKey/BaseUrl)");
        }

        return config;
    }

    public static string ResolveLogprobsModel(AiProviderConfig config, PageFitOptions pageFit)
    {
        var overrideModel = pageFit.Model?.Trim();
        var model = !string.IsNullOrWhiteSpace(overrideModel)
            ? overrideModel
            : FirstNonEmpty(config.DefaultVisionModel, config.DefaultTextModel);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new PageFitPermanentException(
                "provider_not_configured",
                "PageFit model not configured");
        }

        return model;
    }

    public static IReadOnlyList<string> ResolveDistinctVoters(PageFitOptions pageFit)
    {
        var raw = pageFit.Models ?? [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var voters = new List<string>();
        foreach (var m in raw)
        {
            var name = (m ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new PageFitPermanentException(
                    "voters_insufficient",
                    "PageFit:Models contains an empty model name");
            }

            if (!seen.Add(name))
            {
                throw new PageFitPermanentException(
                    "voters_insufficient",
                    "PageFit:Models contains duplicate model names");
            }

            voters.Add(name);
        }

        var min = pageFit.MinVoters > 0 ? pageFit.MinVoters : 3;
        if (voters.Count < min)
        {
            throw new PageFitPermanentException(
                "voters_insufficient",
                $"PageFit:Models has {voters.Count} distinct model(s); MinVoters={min}");
        }

        return voters;
    }

    public static TimeSpan CapRetryAfter(TimeSpan delay, PageFitOptions opts)
    {
        var maxSec = opts.MaxRetryAfterSeconds;
        if (maxSec <= 0) maxSec = 60;
        var max = TimeSpan.FromSeconds(maxSec);
        return delay > max ? max : delay;
    }

    public static TimeSpan? ParseRetryAfter(HttpResponseMessage response, PageFitOptions opts)
    {
        TimeSpan? raw = null;
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            raw = delta;
        else if (response.Headers.RetryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) raw = wait;
        }
        else if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var text = values.FirstOrDefault();
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                && seconds > 0)
                raw = TimeSpan.FromSeconds(seconds);
            else if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                         DateTimeStyles.AssumeUniversal, out var when))
            {
                var wait = when - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) raw = wait;
            }
        }

        return raw is null ? null : CapRetryAfter(raw.Value, opts);
    }

    public static decimal EstimateCost(int promptTokens, int completionTokens, PageFitOptions opts)
    {
        if (promptTokens <= 0 && completionTokens <= 0) return 0m;
        return (promptTokens * opts.InputPricePerMillionUsd
                + completionTokens * opts.OutputPricePerMillionUsd) / 1_000_000m;
    }

    public static string Redact(string? text, string? apiKey = null)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var result = text
            .Replace("Authorization", "[redacted]", StringComparison.OrdinalIgnoreCase)
            .Replace("Bearer ", "Bearer [redacted]", StringComparison.OrdinalIgnoreCase)
            .Replace("api_key", "[redacted]", StringComparison.OrdinalIgnoreCase)
            .Replace("apiKey", "[redacted]", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(apiKey))
            result = result.Replace(apiKey, "[redacted]", StringComparison.Ordinal);
        return result;
    }

    public static string BuildUserMessage(string imageStateText, string profileText)
        => "Image description:\n" + (imageStateText ?? "")
           + "\n\nPage profile:\n" + (profileText ?? "")
           + "\n\nDoes the image fit this page? Answer YES or NO.";

    /// <summary>
    /// Chuẩn hoá đáp án bỏ phiếu: chỉ chấp nhận đúng YES hoặc NO sau khi trim
    /// và bỏ dấu câu/ngoặc ở hai đầu. Mọi thứ khác (kể cả "YES because...") là null.
    /// </summary>
    public static string? NormalizeVoteAnswer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        // Strip wrapping / trailing punctuation and brackets (fail closed for explanations).
        while (s.Length > 0 && IsStrippableEdge(s[^1]))
            s = s[..^1].TrimEnd();
        while (s.Length > 0 && IsStrippableEdge(s[0]))
            s = s[1..].TrimStart();
        if (s.Length == 0) return null;
        var upper = s.ToUpperInvariant();
        if (upper is "YES" or "NO") return upper;
        return null;
    }

    public static string ExtractMessageContent(System.Text.Json.JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
            return string.Empty;

        if (content.ValueKind == System.Text.Json.JsonValueKind.String)
            return content.GetString() ?? string.Empty;

        if (content.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == System.Text.Json.JsonValueKind.String)
                    sb.Append(part.GetString());
                else if (part.ValueKind == System.Text.Json.JsonValueKind.Object
                         && part.TryGetProperty("text", out var text)
                         && text.ValueKind == System.Text.Json.JsonValueKind.String)
                    sb.Append(text.GetString());
            }

            return sb.ToString();
        }

        return string.Empty;
    }

    private static bool IsStrippableEdge(char c)
        => c is '.' or '!' or ',' or ';' or ':' or '"' or '\''
            or '(' or ')' or '[' or ']' or '{' or '}';

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
