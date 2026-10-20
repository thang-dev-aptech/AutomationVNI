using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Backend.Shared.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Campaign.FullMedia.Scoring;

/// <summary>
/// Bộ chấm bỏ phiếu nhiều model trên văn bản (mô tả ảnh + hồ sơ page).
/// Điểm page = số model trả YES / tổng số model — đây là <b>tỉ lệ đồng thuận</b>,
/// không phải xác suất. Với N≤16 model, ngưỡng FitThreshold 0,94 nghĩa là nhất trí tuyệt đối.
/// Không gửi ảnh/data URL; không dùng điểm tự khai.
/// <para>
/// Dừng sớm theo page (PageFit:EarlyExit, mặc định bật): sau mỗi phiếu, nếu
/// (yes + còn_lại) / N không đạt <see cref="FullMediaPipelineOptions.MinFitScore"/>
/// (so sánh chỉ qua <see cref="FitThreshold.Passes"/>) thì bỏ các phiếu còn lại của page đó.
/// Điểm báo cáo vẫn là yes/N (phiếu bỏ tính như NO — cận dưới); nhận/loại không đổi vì page
/// chắc chắn không đạt. Kiểm phiếu (yes/đã gửi/N) được báo kèm trong PageFitResult.Votes
/// để chấm lại biết điểm dừng sớm còn đáng tin hay không. Exhaustive=true hoặc EarlyExit=false: luôn gửi đủ N phiếu.
/// </para>
/// </summary>
public sealed class VotingPageFitScorer(
    HttpClient http,
    IOptions<AiProvidersOptions> aiOptions,
    IOptions<PageFitOptions> pageFitOptions,
    IOptions<FullMediaPipelineOptions> pipelineOptions,
    TimeProvider timeProvider,
    ILogger<VotingPageFitScorer> logger) : IPageFitScorer
{
    internal const string SystemPrompt =
        "Reply with exactly one word: YES or NO.";

    public Task<PageFitResult> ScoreAsync(
        string imageStateText,
        IReadOnlyList<PageFitCandidate> pages,
        CancellationToken ct = default)
        => ScoreCoreAsync(imageStateText, pages, exhaustive: false, ct);

    public Task<PageFitResult> ScoreAsync(
        string imageStateText,
        IReadOnlyList<PageFitCandidate> pages,
        PageFitScoreOptions options,
        CancellationToken ct = default)
        => ScoreCoreAsync(imageStateText, pages, exhaustive: options.Exhaustive, ct);

    private async Task<PageFitResult> ScoreCoreAsync(
        string imageStateText,
        IReadOnlyList<PageFitCandidate> pages,
        bool exhaustive,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
            return new PageFitResult(
                new Dictionary<Guid, double>(), 0m, 0, new Dictionary<Guid, PageFitVoteTally>());

        var opts = pageFitOptions.Value;
        var minScore = pipelineOptions.Value.MinFitScore;
        var allowEarlyExit = opts.EarlyExit && !exhaustive;
        var config = PageFitScorerSupport.ResolveProvider(aiOptions.Value, opts);
        var voters = PageFitScorerSupport.ResolveDistinctVoters(opts);
        var n = voters.Count;
        var url = PageFitScorerSupport.BuildUrl(config);

        var scores = new Dictionary<Guid, double>(pages.Count);
        var votes = new Dictionary<Guid, PageFitVoteTally>(pages.Count);
        var totalPrompt = 0;
        var totalCompletion = 0;
        var requestCount = 0;
        var requestIndex = 0;

        for (var p = 0; p < pages.Count; p++)
        {
            ct.ThrowIfCancellationRequested();
            var page = pages[p];
            var yesCount = 0;
            var voted = 0;
            var skipped = 0;

            for (var v = 0; v < n; v++)
            {
                ct.ThrowIfCancellationRequested();
                if (requestIndex > 0 && opts.MinDelayBetweenRequestsMs > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(opts.MinDelayBetweenRequestsMs), timeProvider, ct);

                var model = voters[v];
                var (answer, promptTokens, completionTokens, attempts) =
                    await VoteOnceWithRetryAsync(url, config.ApiKey, model, imageStateText, page, ct);
                requestCount += attempts;
                requestIndex++;
                totalPrompt += promptTokens;
                totalCompletion += completionTokens;
                voted++;

                if (answer == "YES")
                    yesCount++;
                else if (answer != "NO")
                    throw new PageFitTransientException("unparseable_answer", "PageFit vote answer was not YES/NO");

                if (!allowEarlyExit)
                    continue;

                var remaining = n - voted;
                // Điểm tối đa còn đạt được nếu mọi phiếu còn lại là YES.
                var maxPossible = (double)(yesCount + remaining) / n;
                if (!FitThreshold.Passes(maxPossible, minScore))
                {
                    skipped = remaining;
                    break;
                }
            }

            // Cận dưới: phiếu bỏ = NO ⇒ yes/N (không phải yes/voted).
            var score = (double)yesCount / n;
            scores[page.ChannelId] = score;
            // Cast = số phiếu ĐÃ gửi (không tính phiếu bỏ qua do dừng sớm).
            votes[page.ChannelId] = new PageFitVoteTally(yesCount, voted, n);
            logger.LogInformation(
                "PageFit vote page={ChannelId} yes={Yes} voted={Voted} skipped={Skipped} total={Total} score={Score:F4}",
                page.ChannelId, yesCount, voted, skipped, n, score);
        }

        var cost = PageFitScorerSupport.EstimateCost(totalPrompt, totalCompletion, opts);
        return new PageFitResult(scores, cost, requestCount, votes);
    }

    private async Task<(string Answer, int PromptTokens, int CompletionTokens, int Attempts)> VoteOnceWithRetryAsync(
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
                var (answer, prompt, completion) = await SendOnceAsync(
                    url, apiKey, model, imageStateText, page, ct);
                return (answer, prompt, completion, attempt + 1);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new PageFitTransientException("timeout", "PageFit vote request timed out");
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
                delay = PageFitScorerSupport.CapRetryAfter(delay, opts);
                logger.LogWarning("PageFit vote 429, waiting {Delay} before retry {Attempt}/{Max}",
                    delay, attempt + 1, maxAttempts);
                await Task.Delay(delay, timeProvider, ct);
            }
            catch (PageFitTransientException)
            {
                throw;
            }
        }

        throw last ?? new PageFitTransientException("rate_limited", "PageFit vote rate limited after retries");
    }

    private async Task<(string Answer, int PromptTokens, int CompletionTokens)> SendOnceAsync(
        string url,
        string apiKey,
        string model,
        string imageStateText,
        PageFitCandidate page,
        CancellationToken ct)
    {
        var opts = pageFitOptions.Value;
        var maxTokens = opts.VoteMaxTokens > 0 ? opts.VoteMaxTokens : 16;
        var userText = PageFitScorerSupport.BuildUserMessage(imageStateText, page.ProfileText);
        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userText },
            },
            max_tokens = maxTokens,
            temperature = 0,
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
            throw new PageFitTransientException("timeout", "PageFit vote request timed out", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PageFitTransientException("http_error", "PageFit vote HTTP transport error", ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (status is 401 or 403)
            {
                LogHttpError(status, raw);
                throw new PageFitPermanentException("auth", $"PageFit vote auth failed HTTP {status}");
            }

            if (status is 400 or 404 or 422)
            {
                LogHttpError(status, raw);
                throw new PageFitPermanentException(
                    "bad_request_or_model_unavailable",
                    $"PageFit vote provider rejected request HTTP {status}");
            }

            if (status == 429)
            {
                LogHttpError(status, raw);
                var ra = PageFitScorerSupport.ParseRetryAfter(response, opts);
                var ex = new PageFitTransientException("rate_limited", "PageFit vote rate limited (429)");
                if (ra is not null) ex.Data["retryAfter"] = ra.Value;
                throw ex;
            }

            if (status >= 500)
            {
                LogHttpError(status, raw);
                throw new PageFitTransientException("provider_5xx", $"PageFit vote provider error HTTP {status}");
            }

            if (!response.IsSuccessStatusCode)
            {
                LogHttpError(status, raw);
                throw new PageFitTransientException("http_error", $"PageFit vote unexpected HTTP {status}");
            }

            return ParseSuccess(raw);
        }
    }

    private static (string Answer, int PromptTokens, int CompletionTokens) ParseSuccess(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw, default);
            var root = doc.RootElement;

            var promptTokens = 0;
            var completionTokens = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt))
                    promptTokens = ReadTokenCount(pt, "prompt_tokens");
                if (usage.TryGetProperty("completion_tokens", out var ctTok))
                    completionTokens = ReadTokenCount(ctTok, "completion_tokens");
            }

            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                throw new PageFitTransientException("unparseable_answer", "PageFit vote response missing choices");
            }

            var choice0 = choices[0];
            if (!choice0.TryGetProperty("message", out var message)
                || message.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new PageFitTransientException("unparseable_answer", "PageFit vote response missing message");
            }

            var content = PageFitScorerSupport.ExtractMessageContent(message);
            var answer = PageFitScorerSupport.NormalizeVoteAnswer(content);
            if (answer is null)
            {
                throw new PageFitTransientException(
                    "unparseable_answer",
                    "PageFit vote answer was not exactly YES or NO");
            }

            return (answer, promptTokens, completionTokens);
        }
        catch (PageFitPermanentException)
        {
            throw;
        }
        catch (PageFitTransientException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException
                                       or ArgumentException or NotSupportedException)
        {
            throw new PageFitTransientException(
                "invalid_response",
                "PageFit vote response body is not usable JSON",
                ex);
        }
    }

    private static int ReadTokenCount(JsonElement el, string field)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        throw new FormatException($"usage.{field} must be an integer");
    }

    private void LogHttpError(int status, string body)
    {
        var snippet = PageFitScorerSupport.Redact(body, TryGetApiKey());
        if (snippet.Length > 200) snippet = snippet[..200];
        logger.LogWarning("PageFit vote HTTP {Status}: {Body}", status, snippet);
    }

    private string? TryGetApiKey()
    {
        try
        {
            return PageFitScorerSupport.ResolveProvider(aiOptions.Value, pageFitOptions.Value).ApiKey;
        }
        catch
        {
            return null;
        }
    }
}
