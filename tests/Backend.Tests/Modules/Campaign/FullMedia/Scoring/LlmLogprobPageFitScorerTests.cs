using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Backend.Modules.Campaign.FullMedia.Scoring;
using Backend.Shared.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Campaign.FullMedia.Scoring;

public sealed class LlmLogprobPageFitScorerTests
{
    private static readonly Guid PageA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PageB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string SecretKey = "super-secret-api-key-xyz";

    [Fact]
    public async Task RequestShape_LogprobsMaxTokensTemperature_AndAuth()
    {
        var handler = new ScriptedHandler(LogprobJson(0.9, 0.1));
        var svc = CreateScorer(handler, modelOverride: "pagefit-model");

        await svc.ScoreAsync("STATE", [new PageFitCandidate(PageA, "PROFILE")], CancellationToken.None);

        Assert.Single(handler.Requests);
        var req = handler.Requests[0];
        Assert.Equal("http://ai.test/v1/chat/completions", req.Url);
        Assert.Equal("Bearer " + SecretKey, req.Authorization);
        using var doc = JsonDocument.Parse(req.Body);
        var root = doc.RootElement;
        Assert.Equal("pagefit-model", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("logprobs").GetBoolean());
        Assert.Equal(5, root.GetProperty("top_logprobs").GetInt32());
        Assert.Equal(1, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0, root.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task RequestShape_UsesProviderModel_WhenPageFitModelEmpty()
    {
        var handler = new ScriptedHandler(LogprobJson(0.9, 0.1));
        var svc = CreateScorer(handler, modelOverride: "");

        await svc.ScoreAsync("STATE", [new PageFitCandidate(PageA, "PROFILE")], CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal("vision-default", doc.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(0.9, 0.1, 0.9)]
    [InlineData(0.94, 0.06, 0.94)]
    public async Task ScoreFromTopLogprobs_Formula(double yes, double no, double expected)
    {
        var handler = new ScriptedHandler(LogprobJson(yes, no));
        var svc = CreateScorer(handler);

        var result = await svc.ScoreAsync("S", [new PageFitCandidate(PageA, "P")], CancellationToken.None);

        Assert.Equal(expected, result.Scores[PageA], 1e-9);
    }

    [Fact]
    public async Task OnlyYes_ScoreOne_OnlyNo_ScoreZero()
    {
        var yesOnly = new ScriptedHandler(TopJson(("YES", Math.Log(0.8)), ("maybe", Math.Log(0.2))));
        var noOnly = new ScriptedHandler(TopJson(("NO", Math.Log(0.7)), ("other", Math.Log(0.3))));

        var sYes = await CreateScorer(yesOnly).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        var sNo = await CreateScorer(noOnly).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);

        Assert.Equal(1.0, sYes.Scores[PageA], 1e-9);
        Assert.Equal(0.0, sNo.Scores[PageA], 1e-9);
    }

    [Fact]
    public async Task InterleavedOtherTokens_StillScoresCorrectly()
    {
        var handler = new ScriptedHandler(TopJson(
            ("other", Math.Log(0.05)),
            ("YES", Math.Log(0.7)),
            ("noise", Math.Log(0.05)),
            ("NO", Math.Log(0.2))));
        var result = await CreateScorer(handler)
            .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        Assert.Equal(0.7 / (0.7 + 0.2), result.Scores[PageA], 1e-9);
    }

    [Fact]
    public async Task CaseAndWhitespaceVariants_AreAccumulated()
    {
        var handler = new ScriptedHandler(TopJson(
            ("YES", Math.Log(0.3)),
            ("Yes", Math.Log(0.2)),
            ("yes", Math.Log(0.1)),
            (" YES", Math.Log(0.1)),
            ("NO", Math.Log(0.15)),
            ("no", Math.Log(0.15))));
        var result = await CreateScorer(handler)
            .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        // YES: 0.3+0.2+0.1+0.1=0.7; NO: 0.15+0.15=0.3
        Assert.Equal(0.7 / 1.0, result.Scores[PageA], 1e-9);
    }

    [Fact]
    public async Task NoYesNoToken_Transient()
    {
        var handler = new ScriptedHandler(TopJson(("MAYBE", Math.Log(0.9)), ("OK", Math.Log(0.1))));
        var ex = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("no_yes_no_token", ex.ReasonCode);
    }

    [Fact]
    public async Task MissingLogprobs_PermanentFailClosed_NoScoreFromContent()
    {
        var body = """{"choices":[{"message":{"content":"YES"},"logprobs":null}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";
        var handler = new ScriptedHandler(body);
        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("logprobs_unsupported", ex.ReasonCode);
    }

    [Fact]
    public async Task EmptyTopLogprobs_PermanentFailClosed()
    {
        var body = """{"choices":[{"message":{"content":"YES"},"logprobs":{"content":[{"token":"YES","logprob":0,"top_logprobs":[]}]}}]}""";
        var handler = new ScriptedHandler(body);
        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("logprobs_unsupported", ex.ReasonCode);
    }

    [Fact]
    public async Task Http400_Permanent_LogprobsUnsupportedOrBadRequest()
    {
        var handler = new StatusHandler(HttpStatusCode.BadRequest, "{}");
        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("logprobs_unsupported_or_bad_request", ex.ReasonCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Http401_403_PermanentNoRetry(HttpStatusCode status)
    {
        var handler = new StatusHandler(status, "{}");
        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            CreateScorer(handler, maxRetries: 3).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("auth", ex.ReasonCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Http500_Transient()
    {
        var handler = new StatusHandler(HttpStatusCode.InternalServerError, "err");
        var ex = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("provider_5xx", ex.ReasonCode);
    }

    [Fact]
    public async Task Timeout_Transient()
    {
        var handler = new DelayedHandler(TimeSpan.FromSeconds(5), LogprobJson(0.9, 0.1));
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
        var svc = CreateScorer(http);
        var ex = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            svc.ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("timeout", ex.ReasonCode);
    }

    [Fact]
    public async Task RateLimit_HonorsRetryAfter_ThenSucceeds()
    {
        var time = new RecordingTimeProvider();
        var handler = new SequenceStatusHandler(
            (HttpStatusCode.TooManyRequests, "{}", ("Retry-After", "7")),
            (HttpStatusCode.OK, LogprobJson(0.9, 0.1), null));
        var svc = CreateScorer(handler, timeProvider: time, maxRetries: 2, minDelayMs: 0);

        var result = await svc.ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);

        Assert.Equal(0.9, result.Scores[PageA], 1e-9);
        Assert.Equal(2, handler.RequestCount);
        Assert.Contains(time.Delays, d => Math.Abs(d.TotalSeconds - 7) < 0.01);
    }

    [Fact]
    public async Task RateLimit_ExhaustsMaxRetries_Transient()
    {
        var handler = new Always429Handler();
        var svc = CreateScorer(handler, maxRetries: 2, minDelayMs: 0, timeProvider: new RecordingTimeProvider());

        var ex = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            svc.ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("rate_limited", ex.ReasonCode);
        Assert.Equal(3, handler.RequestCount); // MaxRetries+1
    }

    [Fact]
    public async Task RequestsAreSequential_WithMinDelay()
    {
        var time = new RecordingTimeProvider();
        var handler = new ConcurrencyTrackingHandler(
            LogprobJson(0.9, 0.1), LogprobJson(0.8, 0.2));
        var svc = CreateScorer(handler, timeProvider: time, minDelayMs: 150);

        await svc.ScoreAsync("S", [
            new PageFitCandidate(PageA, "PA"),
            new PageFitCandidate(PageB, "PB")], default);

        Assert.Equal(1, handler.MaxConcurrency);
        Assert.Equal(2, handler.RequestCount);
        Assert.Contains(time.Delays, d => d.TotalMilliseconds >= 149);
    }

    [Fact]
    public async Task BodyContainsNoImageOrDataUrl_OnlyMarkers()
    {
        const string stateMarker = "IMG_STATE_MARKER_ZZ9";
        const string profileMarker = "PAGE_PROFILE_MARKER_QQ8";
        var handler = new ScriptedHandler(LogprobJson(0.9, 0.1));
        await CreateScorer(handler).ScoreAsync(
            stateMarker, [new PageFitCandidate(PageA, profileMarker)], default);

        var body = handler.Requests[0].Body;
        Assert.Contains(stateMarker, body);
        Assert.Contains(profileMarker, body);
        Assert.DoesNotContain("data:image", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("base64", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("image_url", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApiKeyNotLogged_OnSuccessOrError()
    {
        var logger = new CollectingLogger();
        var ok = new ScriptedHandler(LogprobJson(0.9, 0.1));
        await CreateScorer(ok, logger: logger).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);

        var err = new StatusHandler(HttpStatusCode.Unauthorized, "nope " + SecretKey);
        await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            CreateScorer(err, logger: logger).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));

        Assert.All(logger.Messages, m => Assert.DoesNotContain(SecretKey, m, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultiPage_MapsChannelIdToScore_EmptyPagesZeroRequests()
    {
        var handler = new ScriptedHandler(LogprobJson(0.9, 0.1), LogprobJson(0.2, 0.8));
        var svc = CreateScorer(handler, minDelayMs: 0);
        var result = await svc.ScoreAsync("S", [
            new PageFitCandidate(PageA, "A"),
            new PageFitCandidate(PageB, "B")], default);

        Assert.Equal(0.9, result.Scores[PageA], 1e-9);
        Assert.Equal(0.2, result.Scores[PageB], 1e-9);
        Assert.Equal(2, result.RequestCount);

        var empty = await svc.ScoreAsync("S", Array.Empty<PageFitCandidate>(), default);
        Assert.Empty(empty.Scores);
        Assert.Equal(0, empty.RequestCount);
    }

    [Fact]
    public async Task EstimatedCostUsd_FromUsage_AndZeroWhenMissing()
    {
        var withUsage = new ScriptedHandler(LogprobJson(0.9, 0.1, prompt: 1000, completion: 10));
        var priced = CreateScorer(withUsage, inputPrice: 1m, outputPrice: 2m, minDelayMs: 0);
        var r1 = await priced.ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        // (1000*1 + 10*2) / 1e6 = 0.00102
        Assert.Equal(0.00102m, r1.EstimatedCostUsd);

        var noUsage = """{"choices":[{"logprobs":{"content":[{"token":"YES","logprob":0,"top_logprobs":[{"token":"YES","logprob":0},{"token":"NO","logprob":-100}]}]}}]}""";
        var r2 = await CreateScorer(new ScriptedHandler(noUsage), inputPrice: 1m, outputPrice: 2m)
            .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        Assert.Equal(0m, r2.EstimatedCostUsd);
    }

    [Fact]
    public async Task Cancellation_StopsWithoutPageFitException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateScorer(new ScriptedHandler(LogprobJson(0.9, 0.1)))
                .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], cts.Token));
    }

    [Fact]
    public async Task MissingProviderOrKey_Permanent()
    {
        var handler = new ScriptedHandler(LogprobJson(0.9, 0.1));
        var ai = Options.Create(new AiProvidersOptions
        {
            DefaultProvider = "missing",
            Providers = new(StringComparer.OrdinalIgnoreCase),
        });
        var svc = new LlmLogprobPageFitScorer(
            new HttpClient(handler),
            ai,
            Options.Create(new PageFitOptions()),
            TimeProvider.System,
            new CollectingLogger());

        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            svc.ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("provider_not_configured", ex.ReasonCode);
    }

    [Fact]
    public void Di_AddFullMediaPageFit_ResolvesScorer_AndBindsOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PageFit:Model"] = "di-model",
                ["PageFit:TopLogprobs"] = "5",
                ["PageFit:TimeoutSeconds"] = "42",
                ["AiProviders:DefaultProvider"] = "test",
                ["AiProviders:Providers:test:BaseUrl"] = "http://ai.test",
                ["AiProviders:Providers:test:ApiKey"] = "k",
                ["AiProviders:Providers:test:DefaultTextModel"] = "t",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.Configure<AiProvidersOptions>(config.GetSection("AiProviders"));
        services.AddFullMediaPageFit(config);

        using var sp = services.BuildServiceProvider();
        var scorer = sp.GetRequiredService<IPageFitScorer>();
        Assert.IsType<LlmLogprobPageFitScorer>(scorer);
        var opts = sp.GetRequiredService<IOptions<PageFitOptions>>().Value;
        Assert.Equal("di-model", opts.Model);
        Assert.Equal(42, opts.TimeoutSeconds);
        Assert.Equal(5, opts.TopLogprobs);

        // M16: typed HttpClient riêng với TimeoutSeconds từ PageFit
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(typeof(IPageFitScorer).Name);
        Assert.Equal(TimeSpan.FromSeconds(42), client.Timeout);
    }

    [Fact]
    public void Program_Calls_AddFullMediaPageFit()
    {
        // N1: Program chỉ gọi AddFullMediaPipeline; PageFit được đăng ký bên trong extension.
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var programPath = Path.Combine(root, "backend", "Program.cs");
        Assert.True(File.Exists(programPath), $"Program.cs not found at {programPath}");
        var program = File.ReadAllText(programPath);
        Assert.Contains("AddFullMediaPipeline", program);
        Assert.DoesNotContain("AddFullMediaPageFit(builder.Configuration)", program);

        var extPath = Path.Combine(root, "backend", "Modules", "Campaign", "FullMedia",
            "FullMediaPipelineServiceCollectionExtensions.cs");
        Assert.True(File.Exists(extPath));
        Assert.Contains("AddFullMediaPageFit", File.ReadAllText(extPath));
    }

    [Fact]
    public async Task OnePageFails_DoesNotReturnPartialScores()
    {
        var handler = new SequenceStatusHandler(
            (HttpStatusCode.OK, LogprobJson(0.9, 0.1), null),
            (HttpStatusCode.InternalServerError, "boom", null));
        var svc = CreateScorer(handler, minDelayMs: 0);

        await Assert.ThrowsAsync<PageFitTransientException>(() =>
            svc.ScoreAsync("S", [
                new PageFitCandidate(PageA, "A"),
                new PageFitCandidate(PageB, "B")], default));
    }

    [Fact]
    public async Task Http200_HtmlBody_TransientInvalidResponse()
    {
        var handler = new ScriptedHandler("<html><body>gateway error</body></html>");
        var ex = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("invalid_response", ex.ReasonCode);
    }

    [Fact]
    public async Task Http200_UsagePromptTokensAsString_TransientInvalidResponse()
    {
        var body = """
            {"choices":[{"logprobs":{"content":[{"token":"YES","logprob":0,"top_logprobs":[{"token":"YES","logprob":0},{"token":"NO","logprob":-1}]}]}}],
             "usage":{"prompt_tokens":"twelve","completion_tokens":1}}
            """;
        var handler = new ScriptedHandler(body);
        var ex = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("invalid_response", ex.ReasonCode);
    }

    [Fact]
    public async Task RateLimit_RetryAfterCappedAtMaxRetryAfterSeconds()
    {
        var time = new RecordingTimeProvider();
        var handler = new SequenceStatusHandler(
            (HttpStatusCode.TooManyRequests, "{}", ("Retry-After", "3600")),
            (HttpStatusCode.OK, LogprobJson(0.9, 0.1), null));
        var svc = CreateScorer(handler, timeProvider: time, maxRetries: 2, minDelayMs: 0,
            maxRetryAfterSeconds: 60);

        await svc.ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);

        Assert.Contains(time.Delays, d => Math.Abs(d.TotalSeconds - 60) < 0.01);
        Assert.DoesNotContain(time.Delays, d => d.TotalSeconds > 61);
    }

    // --- helpers ---

    private static LlmLogprobPageFitScorer CreateScorer(
        HttpMessageHandler handler,
        string? modelOverride = "pagefit-model",
        int maxRetries = 2,
        int minDelayMs = 0,
        decimal inputPrice = 0,
        decimal outputPrice = 0,
        TimeProvider? timeProvider = null,
        ILogger<LlmLogprobPageFitScorer>? logger = null,
        int maxRetryAfterSeconds = 60)
        => CreateScorer(new HttpClient(handler) { BaseAddress = new Uri("http://unused/") },
            modelOverride, maxRetries, minDelayMs, inputPrice, outputPrice, timeProvider, logger,
            maxRetryAfterSeconds);

    private static LlmLogprobPageFitScorer CreateScorer(
        HttpClient http,
        string? modelOverride = "pagefit-model",
        int maxRetries = 2,
        int minDelayMs = 0,
        decimal inputPrice = 0,
        decimal outputPrice = 0,
        TimeProvider? timeProvider = null,
        ILogger<LlmLogprobPageFitScorer>? logger = null,
        int maxRetryAfterSeconds = 60)
    {
        var ai = Options.Create(new AiProvidersOptions
        {
            DefaultProvider = "test",
            Providers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = new AiProviderConfig
                {
                    BaseUrl = "http://ai.test/v1",
                    ApiKey = SecretKey,
                    ChatCompletionsPath = "/chat/completions",
                    DefaultVisionModel = "vision-default",
                    DefaultTextModel = "text-default",
                },
            },
        });
        var pageFit = Options.Create(new PageFitOptions
        {
            Model = modelOverride ?? "",
            TimeoutSeconds = 60,
            TopLogprobs = 5,
            MinDelayBetweenRequestsMs = minDelayMs,
            MaxRetries = maxRetries,
            MaxRetryAfterSeconds = maxRetryAfterSeconds,
            InputPricePerMillionUsd = inputPrice,
            OutputPricePerMillionUsd = outputPrice,
        });
        return new LlmLogprobPageFitScorer(
            http, ai, pageFit, timeProvider ?? TimeProvider.System, logger ?? new CollectingLogger());
    }

    private static string LogprobJson(double pYes, double pNo, int prompt = 10, int completion = 1)
        => TopJson(prompt, completion, ("YES", Math.Log(pYes)), ("NO", Math.Log(pNo)));

    private static string TopJson(params (string Token, double Logprob)[] tops)
        => TopJson(10, 1, tops);

    private static string TopJson(int prompt, int completion, params (string Token, double Logprob)[] tops)
    {
        var top = tops.Select(t => new { token = t.Token, logprob = t.Logprob }).ToArray();
        return JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { content = tops.FirstOrDefault().Token ?? "YES" },
                    logprobs = new
                    {
                        content = new[]
                        {
                            new { token = tops.FirstOrDefault().Token ?? "YES", logprob = 0.0, top_logprobs = top },
                        },
                    },
                },
            },
            usage = new { prompt_tokens = prompt, completion_tokens = completion },
        });
    }

    private sealed record CapturedRequest(string Url, string? Authorization, string Body);

    private sealed class ScriptedHandler(params string[] bodies) : HttpMessageHandler
    {
        private readonly Queue<string> _bodies = new(bodies);
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.RequestUri?.ToString() ?? "",
                request.Headers.Authorization?.ToString(),
                body));
            var content = _bodies.Count > 0 ? _bodies.Dequeue() : LogprobJson(0.5, 0.5);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StatusHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class DelayedHandler(TimeSpan delay, string body) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SequenceStatusHandler(
        params (HttpStatusCode Status, string Body, (string Name, string Value)? Header)[] steps)
        : HttpMessageHandler
    {
        private int _i;
        public int RequestCount => _i;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var step = steps[Math.Min(_i, steps.Length - 1)];
            _i++;
            var msg = new HttpResponseMessage(step.Status)
            {
                Content = new StringContent(step.Body, Encoding.UTF8, "application/json"),
            };
            if (step.Header is { } h) msg.Headers.TryAddWithoutValidation(h.Name, h.Value);
            return Task.FromResult(msg);
        }
    }

    private sealed class Always429Handler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var msg = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            msg.Headers.TryAddWithoutValidation("Retry-After", "0");
            return Task.FromResult(msg);
        }
    }

    private sealed class ConcurrencyTrackingHandler(params string[] bodies) : HttpMessageHandler
    {
        private readonly Queue<string> _bodies = new(bodies);
        private int _active;
        public int MaxConcurrency { get; private set; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _active);
            MaxConcurrency = Math.Max(MaxConcurrency, n);
            RequestCount++;
            try
            {
                await Task.Delay(30, cancellationToken);
                var content = _bodies.Count > 0 ? _bodies.Dequeue() : LogprobJson(0.5, 0.5);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json"),
                };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class RecordingTimeProvider : TimeProvider
    {
        public ConcurrentBag<TimeSpan> Delays { get; } = [];

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime > TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                Delays.Add(dueTime);
            callback(state);
            return new ImmediateTimer();
        }

        private sealed class ImmediateTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class CollectingLogger : ILogger<LlmLogprobPageFitScorer>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
