using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Backend.Shared.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Campaign.FullMedia.Scoring;

public sealed class LlmLogprobPageFitScorer(
    HttpClient http,
    IOptions<AiProvidersOptions> aiOptions,
    IOptions<PageFitOptions> pageFitOptions,
    TimeProvider timeProvider,
    ILogger<LlmLogprobPageFitScorer> logger) : IPageFitScorer
{
    internal const string SystemPrompt =
        "Reply with exactly one word: YES if the image description fits the page profile, NO if it does not.";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<PageFitResult> ScoreAsync(
        string imageStateText,
        IReadOnlyList<PageFitCandidate> pages,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
            return new PageFitResult(new Dictionary<Guid, double>(), 0m, 0);

        var (config, model) = ResolveProviderAndModel();
        var url = BuildUrl(config);
        var scores = new Dictionary<Guid, double>(pages.Count);
        var totalPrompt = 0;
        var totalCompletion = 0;
        var requestCount = 0;
        var opts = pageFitOptions.Value;

        for (var i = 0; i < pages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0 && opts.MinDelayBetweenRequestsMs > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(opts.MinDelayBetweenRequestsMs), timeProvider, ct);

            var page = pages[i];
            var (score, promptTokens, completionTokens, attempts) =
                await ScoreOnePageAsync(url, config.ApiKey, model, imageStateText, page, ct);
            requestCount += attempts;
            scores[page.ChannelId] = score;
            totalPrompt += promptTokens;
            totalCompletion += completionTokens;
        }

        var cost = EstimateCost(totalPrompt, totalCompletion, opts);
        return new PageFitResult(scores, cost, requestCount);
    }

    private async Task<(double Score, int PromptTokens, int CompletionTokens, int Attempts)> ScoreOnePageAsync(
        string url,
        string apiKey,
        string model,
        string imageStateText,
        PageFitCandidate page,
        CancellationToken ct)
    {
        var opts = pageFitOptions.Value;
        var maxAttempts = Math.Max(0, opts.MaxRetries) + 1;
        Exception? last = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (score, prompt, completion) = await SendOnceAsync(
                    url, apiKey, model, imageStateText, page, ct);
                return (score, prompt, completion, attempt + 1);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // HttpClient timeout
                throw new PageFitTransientException("timeout", "PageFit request timed out");
            }
            catch (PageFitPermanentException)
            {
                throw;
            }
            catch (PageFitTransientException ex) when (ex.ReasonCode == "rate_limited" && attempt + 1 < maxAttempts)
            {
                last = ex;
                var delay = ex.Data["retryAfter"] is TimeSpan ra && ra > TimeSpan.Zero
                    ? ra
                    : TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning("PageFit 429, waiting {Delay} before retry {Attempt}/{Max}",
                    delay, attempt + 1, maxAttempts);
                await Task.Delay(delay, timeProvider, ct);
            }
            catch (PageFitTransientException)
            {
                throw;
            }
        }

        throw last ?? new PageFitTransientException("rate_limited", "PageFit rate limited after retries");
    }

    private async Task<(double Score, int PromptTokens, int CompletionTokens)> SendOnceAsync(
        string url,
        string apiKey,
        string model,
        string imageStateText,
        PageFitCandidate page,
        CancellationToken ct)
    {
        var opts = pageFitOptions.Value;
        var userText = BuildUserMessage(imageStateText, page.ProfileText);
        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userText },
            },
            max_tokens = 1,
            temperature = 0,
            logprobs = true,
            top_logprobs = opts.TopLogprobs > 0 ? opts.TopLogprobs : 5,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new PageFitTransientException("timeout", "PageFit request timed out", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PageFitTransientException("http_error", "PageFit HTTP transport error", ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (status is 401 or 403)
            {
                LogHttpError(status, raw);
                throw new PageFitPermanentException("auth", $"PageFit auth failed HTTP {status}");
            }

            if (status is 400 or 404 or 422)
            {
                LogHttpError(status, raw);
                throw new PageFitPermanentException(
                    "logprobs_unsupported_or_bad_request",
                    $"PageFit provider rejected request HTTP {status}");
            }

            if (status == 429)
            {
                LogHttpError(status, raw);
                var ra = ParseRetryAfter(response);
                var ex = new PageFitTransientException("rate_limited", "PageFit rate limited (429)");
                if (ra is not null) ex.Data["retryAfter"] = ra.Value;
                throw ex;
            }

            if (status >= 500)
            {
                LogHttpError(status, raw);
                throw new PageFitTransientException("provider_5xx", $"PageFit provider error HTTP {status}");
            }

            if (!response.IsSuccessStatusCode)
            {
                LogHttpError(status, raw);
                throw new PageFitTransientException("http_error", $"PageFit unexpected HTTP {status}");
            }

            return ParseSuccess(raw);
        }
    }

    private (double Score, int PromptTokens, int CompletionTokens) ParseSuccess(string raw)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;

        var promptTokens = 0;
        var completionTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ctTok)) completionTokens = ctTok.GetInt32();
        }

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new PageFitPermanentException("logprobs_unsupported", "PageFit response missing choices");
        }

        var choice0 = choices[0];
        if (!choice0.TryGetProperty("logprobs", out var logprobs)
            || logprobs.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new PageFitPermanentException(
                "logprobs_unsupported",
                "PageFit response missing logprobs (fail closed)");
        }

        if (!logprobs.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array
            || content.GetArrayLength() == 0)
        {
            throw new PageFitPermanentException(
                "logprobs_unsupported",
                "PageFit logprobs.content empty (fail closed)");
        }

        var first = content[0];
        if (!first.TryGetProperty("top_logprobs", out var top)
            || top.ValueKind != JsonValueKind.Array
            || top.GetArrayLength() == 0)
        {
            throw new PageFitPermanentException(
                "logprobs_unsupported",
                "PageFit top_logprobs empty (fail closed)");
        }

        double pYes = 0;
        double pNo = 0;
        foreach (var entry in top.EnumerateArray())
        {
            if (!entry.TryGetProperty("token", out var tokenEl)) continue;
            if (!entry.TryGetProperty("logprob", out var lpEl)) continue;
            var token = NormalizeToken(tokenEl.GetString());
            var p = Math.Exp(lpEl.GetDouble());
            if (token == "YES") pYes += p;
            else if (token == "NO") pNo += p;
        }

        if (pYes <= 0 && pNo <= 0)
            throw new PageFitTransientException("no_yes_no_token", "PageFit top_logprobs had no YES/NO token");

        if (pYes > 0 && pNo <= 0) return (1.0, promptTokens, completionTokens);
        if (pNo > 0 && pYes <= 0) return (0.0, promptTokens, completionTokens);
        return (pYes / (pYes + pNo), promptTokens, completionTokens);
    }

    private (AiProviderConfig Config, string Model) ResolveProviderAndModel()
    {
        var ai = aiOptions.Value;
        var name = ai.DefaultProvider;
        if (string.IsNullOrWhiteSpace(name)
            || !ai.Providers.TryGetValue(name, out var config)
            || string.IsNullOrWhiteSpace(config.ApiKey)
            || string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            throw new PageFitPermanentException(
                "provider_not_configured",
                "PageFit AI provider missing (name/ApiKey/BaseUrl)");
        }

        var overrideModel = pageFitOptions.Value.Model?.Trim();
        var model = !string.IsNullOrWhiteSpace(overrideModel)
            ? overrideModel
            : FirstNonEmpty(config.DefaultVisionModel, config.DefaultTextModel);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new PageFitPermanentException(
                "provider_not_configured",
                "PageFit model not configured");
        }

        return (config, model);
    }

    private static string BuildUrl(AiProviderConfig config)
    {
        var baseUrl = config.BaseUrl.TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(config.ChatCompletionsPath)
            ? "/chat/completions"
            : config.ChatCompletionsPath.StartsWith('/')
                ? config.ChatCompletionsPath
                : "/" + config.ChatCompletionsPath;
        return baseUrl + path;
    }

    internal static string BuildUserMessage(string imageStateText, string profileText)
        => "Image description:\n" + (imageStateText ?? "")
           + "\n\nPage profile:\n" + (profileText ?? "")
           + "\n\nDoes the image fit this page? Answer YES or NO.";

    internal static string NormalizeToken(string? token)
        => (token ?? string.Empty).Trim().ToUpperInvariant();

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static decimal EstimateCost(int promptTokens, int completionTokens, PageFitOptions opts)
    {
        if (promptTokens <= 0 && completionTokens <= 0) return 0m;
        return (promptTokens * opts.InputPricePerMillionUsd
                + completionTokens * opts.OutputPricePerMillionUsd) / 1_000_000m;
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta > TimeSpan.Zero ? delta : null;
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : null;
        }

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                && seconds > 0)
                return TimeSpan.FromSeconds(seconds);
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var when))
            {
                var wait = when - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : null;
            }
        }

        return null;
    }

    private void LogHttpError(int status, string body)
    {
        var snippet = Redact(body, TryGetApiKey());
        if (snippet.Length > 200) snippet = snippet[..200];
        logger.LogWarning("PageFit HTTP {Status}: {Body}", status, snippet);
    }

    private string? TryGetApiKey()
    {
        var ai = aiOptions.Value;
        if (string.IsNullOrWhiteSpace(ai.DefaultProvider)) return null;
        return ai.Providers.TryGetValue(ai.DefaultProvider, out var c) ? c.ApiKey : null;
    }

    internal static string Redact(string? text, string? apiKey = null)
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
}
