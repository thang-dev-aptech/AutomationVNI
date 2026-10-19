using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Backend.Modules.Campaign.FullMedia;
using Backend.Modules.Campaign.FullMedia.Scoring;
using Backend.Shared.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Modules.Campaign.FullMedia.Scoring;

public sealed class VotingPageFitScorerTests
{
    private static readonly Guid PageA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PageB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string SecretKey = "super-secret-api-key-xyz";
    private static readonly string[] DefaultVoters = ["m1", "m2", "m3"];

    [Fact]
    public async Task VoteScore_ThreeYes_IsOne()
    {
        var handler = new ScriptedHandler(VoteJson("YES"), VoteJson("YES"), VoteJson("YES"));
        var result = await CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        Assert.Equal(1.0, result.Scores[PageA], 1e-9);
        Assert.True(FitThreshold.Passes(result.Scores[PageA], FitThreshold.DefaultMinFitScore));
    }

    [Fact]
    public async Task VoteScore_TwoOfThree_NotAboveThreshold()
    {
        var handler = new ScriptedHandler(VoteJson("YES"), VoteJson("YES"), VoteJson("NO"));
        var result = await CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        Assert.Equal(2.0 / 3.0, result.Scores[PageA], 1e-9);
        Assert.False(FitThreshold.Passes(result.Scores[PageA], FitThreshold.DefaultMinFitScore));
    }

    [Fact]
    public async Task VoteScore_OneYes_ThirdOrZero()
    {
        var one = await CreateScorer(new ScriptedHandler(VoteJson("YES"), VoteJson("NO"), VoteJson("NO")))
            .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        Assert.Equal(1.0 / 3.0, one.Scores[PageA], 1e-9);
        Assert.False(FitThreshold.Passes(one.Scores[PageA], FitThreshold.DefaultMinFitScore));

        var zero = await CreateScorer(new ScriptedHandler(VoteJson("NO"), VoteJson("NO"), VoteJson("NO")))
            .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        Assert.Equal(0.0, zero.Scores[PageA], 1e-9);
        Assert.False(FitThreshold.Passes(zero.Scores[PageA], FitThreshold.DefaultMinFitScore));
    }

    [Fact]
    public void VoteConsensus_N3_PossibleValues_OnlyUnanimousPassesThreshold()
    {
        // Với N=3 mọi giá trị khả dĩ: 0, 1/3, 2/3, 1 — chỉ 1 đạt 0.94.
        double[] possible = [0, 1.0 / 3.0, 2.0 / 3.0, 1.0];
        Assert.Equal(possible, possible.Distinct().OrderBy(x => x).ToArray());
        Assert.False(FitThreshold.Passes(0, FitThreshold.DefaultMinFitScore));
        Assert.False(FitThreshold.Passes(1.0 / 3.0, FitThreshold.DefaultMinFitScore));
        Assert.False(FitThreshold.Passes(2.0 / 3.0, FitThreshold.DefaultMinFitScore));
        Assert.True(FitThreshold.Passes(1.0, FitThreshold.DefaultMinFitScore));
    }

    [Theory]
    [InlineData("YES", "YES")]
    [InlineData("yes", "YES")]
    [InlineData(" Yes. ", "YES")]
    [InlineData("NO!", "NO")]
    [InlineData("\"NO\"", "NO")]
    public void AnswerNormalization_ScalarVariants(string raw, string expected)
    {
        Assert.Equal(expected, PageFitScorerSupport.NormalizeVoteAnswer(raw));
    }

    [Fact]
    public async Task AnswerNormalization_ArrayTextContent()
    {
        var body = """{"choices":[{"message":{"content":[{"type":"text","text":"YES"}]}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";
        var handler = new ScriptedHandler(body, body, body);
        var result = await CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        Assert.Equal(1.0, result.Scores[PageA], 1e-9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I cannot")]
    [InlineData("YES because it fits")]
    public async Task UnparseableAnswer_Transient_NoPartialScores(string content)
    {
        var handler = new ScriptedHandler(
            VoteJson("YES"), VoteJson("YES"), VoteJson(content));
        var ex = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(handler).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("unparseable_answer", ex.ReasonCode);
    }

    [Fact]
    public async Task UnparseableAnswer_NullContent_MissingChoices()
    {
        var nullContent = """{"choices":[{"message":{"content":null}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";
        var missing = """{"choices":[],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";

        var ex1 = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(new ScriptedHandler(nullContent, VoteJson("YES"), VoteJson("YES")))
                .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("unparseable_answer", ex1.ReasonCode);

        var ex2 = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(new ScriptedHandler(missing, VoteJson("YES"), VoteJson("YES")))
                .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("unparseable_answer", ex2.ReasonCode);
    }

    [Fact]
    public void NeverTreatsUnknownAsYes()
    {
        Assert.Null(PageFitScorerSupport.NormalizeVoteAnswer("MAYBE"));
        Assert.Null(PageFitScorerSupport.NormalizeVoteAnswer("Y"));
        Assert.Null(PageFitScorerSupport.NormalizeVoteAnswer("N"));
        Assert.Null(PageFitScorerSupport.NormalizeVoteAnswer("YES because..."));
        Assert.Null(PageFitScorerSupport.NormalizeVoteAnswer("true"));
    }

    [Fact]
    public async Task RequestShape_Vote_NoLogprobs_CorrectModels()
    {
        var handler = new ScriptedHandler(VoteJson("YES"), VoteJson("NO"), VoteJson("YES"));
        await CreateScorer(handler, voteMaxTokens: 16)
            .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);

        Assert.Equal(3, handler.Requests.Count);
        for (var i = 0; i < 3; i++)
        {
            var req = handler.Requests[i];
            Assert.Equal("http://ai.test/v1/chat/completions", req.Url);
            Assert.Equal("Bearer " + SecretKey, req.Authorization);
            using var doc = JsonDocument.Parse(req.Body);
            var root = doc.RootElement;
            Assert.Equal(DefaultVoters[i], root.GetProperty("model").GetString());
            Assert.Equal(0, root.GetProperty("temperature").GetDouble());
            Assert.Equal(16, root.GetProperty("max_tokens").GetInt32());
            Assert.False(root.TryGetProperty("logprobs", out _));
            Assert.False(root.TryGetProperty("top_logprobs", out _));
        }
    }

    [Fact]
    public async Task RequestCount_IsModelsTimesPages_Sequential()
    {
        var time = new RecordingTimeProvider();
        var bodies = Enumerable.Range(0, 6).Select(_ => VoteJson("YES")).ToArray();
        var handler = new ConcurrencyTrackingHandler(bodies);
        var svc = CreateScorer(handler, timeProvider: time, minDelayMs: 150);

        var result = await svc.ScoreAsync("S", [
            new PageFitCandidate(PageA, "PA"),
            new PageFitCandidate(PageB, "PB")], default);

        Assert.Equal(6, result.RequestCount);
        Assert.Equal(1, handler.MaxConcurrency);
        // Delay giữa mọi cặp request liên tiếp (5 khoảng cho 6 request).
        Assert.Equal(5, time.Delays.Count(d => d.TotalMilliseconds >= 149));
    }

    [Fact]
    public async Task FewerThanMinVoters_Permanent_NoNetwork()
    {
        var handler = new ScriptedHandler(VoteJson("YES"));
        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            CreateScorer(handler, models: ["only-one"]).ScoreAsync(
                "S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("voters_insufficient", ex.ReasonCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DuplicateModels_Permanent_NoNetwork()
    {
        var handler = new ScriptedHandler(VoteJson("YES"));
        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            CreateScorer(handler, models: ["m1", "M1", "m2"]).ScoreAsync(
                "S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("voters_insufficient", ex.ReasonCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EmptyModelName_Permanent()
    {
        var handler = new ScriptedHandler(VoteJson("YES"));
        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            CreateScorer(handler, models: ["m1", "  ", "m3"]).ScoreAsync(
                "S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("voters_insufficient", ex.ReasonCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProviderMissing_Permanent()
    {
        var handler = new ScriptedHandler(VoteJson("YES"));
        var ai = Options.Create(new AiProvidersOptions
        {
            DefaultProvider = "missing",
            Providers = new(StringComparer.OrdinalIgnoreCase),
        });
        var svc = new VotingPageFitScorer(
            new HttpClient(handler),
            ai,
            Options.Create(new PageFitOptions { Models = [..DefaultVoters], MinVoters = 3 }),
            TimeProvider.System,
            new CollectingLogger());

        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            svc.ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("provider_not_configured", ex.ReasonCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PageFitProviderOverride_UsesNamedProvider()
    {
        var handler = new ScriptedHandler(VoteJson("YES"), VoteJson("YES"), VoteJson("YES"));
        var ai = Options.Create(new AiProvidersOptions
        {
            DefaultProvider = "default-prov",
            Providers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["default-prov"] = new AiProviderConfig
                {
                    BaseUrl = "http://default.test/v1",
                    ApiKey = "default-key",
                    ChatCompletionsPath = "/chat/completions",
                },
                ["override-prov"] = new AiProviderConfig
                {
                    BaseUrl = "http://override.test/v1",
                    ApiKey = SecretKey,
                    ChatCompletionsPath = "/chat/completions",
                },
            },
        });
        var pageFit = Options.Create(new PageFitOptions
        {
            Provider = "override-prov",
            Models = [..DefaultVoters],
            MinVoters = 3,
            MinDelayBetweenRequestsMs = 0,
        });
        var svc = new VotingPageFitScorer(
            new HttpClient(handler), ai, pageFit, TimeProvider.System, new CollectingLogger());

        await svc.ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        Assert.All(handler.Requests, r => Assert.Equal("http://override.test/v1/chat/completions", r.Url));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "auth")]
    [InlineData(HttpStatusCode.Forbidden, "auth")]
    [InlineData(HttpStatusCode.BadRequest, "bad_request_or_model_unavailable")]
    [InlineData(HttpStatusCode.NotFound, "bad_request_or_model_unavailable")]
    [InlineData((HttpStatusCode)422, "bad_request_or_model_unavailable")]
    public async Task Http401_403_400_404_422_Permanent_NoRetry(HttpStatusCode status, string reason)
    {
        var handler = new StatusHandler(status, "{}");
        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            CreateScorer(handler, maxRetries: 3).ScoreAsync(
                "S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal(reason, ex.ReasonCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Http500_Timeout_InvalidJson200_Transient()
    {
        var s500 = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(new StatusHandler(HttpStatusCode.InternalServerError, "err"))
                .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("provider_5xx", s500.ReasonCode);

        var http = new HttpClient(new DelayedHandler(TimeSpan.FromSeconds(5), VoteJson("YES")))
            { Timeout = TimeSpan.FromMilliseconds(50) };
        var timeout = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(http).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("timeout", timeout.ReasonCode);

        var badJson = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(new ScriptedHandler("<html>nope</html>"))
                .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("invalid_response", badJson.ReasonCode);
    }

    [Fact]
    public async Task RateLimit_HonorsRetryAfterCapped_ThenSucceeds()
    {
        var time = new RecordingTimeProvider();
        var handler = new SequenceStatusHandler(
            (HttpStatusCode.TooManyRequests, "{}", ("Retry-After", "3600")),
            (HttpStatusCode.OK, VoteJson("YES"), null),
            (HttpStatusCode.OK, VoteJson("YES"), null),
            (HttpStatusCode.OK, VoteJson("YES"), null));
        var result = await CreateScorer(handler, timeProvider: time, maxRetries: 2, minDelayMs: 0,
                maxRetryAfterSeconds: 60)
            .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);

        Assert.Equal(1.0, result.Scores[PageA], 1e-9);
        Assert.Contains(time.Delays, d => Math.Abs(d.TotalSeconds - 60) < 0.01);
        Assert.DoesNotContain(time.Delays, d => d.TotalSeconds > 61);
    }

    [Fact]
    public async Task RateLimit_Exhausted_Transient()
    {
        var handler = new Always429Handler();
        var ex = await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(handler, maxRetries: 2, minDelayMs: 0, timeProvider: new RecordingTimeProvider())
                .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("rate_limited", ex.ReasonCode);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task OneVoterFails_NoPartialScores()
    {
        // page1: 3 YES; page2: first vote 500 → throw, no Scores returned
        var handler = new SequenceStatusHandler(
            (HttpStatusCode.OK, VoteJson("YES"), null),
            (HttpStatusCode.OK, VoteJson("YES"), null),
            (HttpStatusCode.OK, VoteJson("YES"), null),
            (HttpStatusCode.InternalServerError, "boom", null));
        await Assert.ThrowsAsync<PageFitTransientException>(() =>
            CreateScorer(handler, minDelayMs: 0).ScoreAsync("S", [
                new PageFitCandidate(PageA, "A"),
                new PageFitCandidate(PageB, "B")], default));
    }

    [Fact]
    public async Task BodyContainsNoImageOrDataUrl_OnlyMarkers()
    {
        const string stateMarker = "IMG_STATE_MARKER_ZZ9";
        const string profileMarker = "PAGE_PROFILE_MARKER_QQ8";
        var handler = new ScriptedHandler(VoteJson("YES"), VoteJson("YES"), VoteJson("YES"));
        await CreateScorer(handler).ScoreAsync(
            stateMarker, [new PageFitCandidate(PageA, profileMarker)], default);

        Assert.All(handler.Requests, r =>
        {
            Assert.Contains(stateMarker, r.Body);
            Assert.Contains(profileMarker, r.Body);
            Assert.DoesNotContain("data:image", r.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("base64", r.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("image_url", r.Body, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ApiKeyNotLogged_OnSuccessOrError()
    {
        var logger = new CollectingLogger();
        await CreateScorer(new ScriptedHandler(VoteJson("YES"), VoteJson("YES"), VoteJson("YES")),
            logger: logger).ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);

        await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            CreateScorer(new StatusHandler(HttpStatusCode.Unauthorized, "nope " + SecretKey), logger: logger)
                .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));

        Assert.All(logger.Messages, m => Assert.DoesNotContain(SecretKey, m, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultiPage_MapsChannelIdToScore()
    {
        var handler = new ScriptedHandler(
            VoteJson("YES"), VoteJson("YES"), VoteJson("YES"),
            VoteJson("YES"), VoteJson("NO"), VoteJson("NO"));
        var result = await CreateScorer(handler).ScoreAsync("S", [
            new PageFitCandidate(PageA, "A"),
            new PageFitCandidate(PageB, "B")], default);

        Assert.Equal(1.0, result.Scores[PageA], 1e-9);
        Assert.Equal(1.0 / 3.0, result.Scores[PageB], 1e-9);
        Assert.Equal(6, result.RequestCount);
    }

    [Fact]
    public async Task EmptyPages_ZeroRequests()
    {
        var handler = new ScriptedHandler(VoteJson("YES"));
        var empty = await CreateScorer(handler).ScoreAsync("S", Array.Empty<PageFitCandidate>(), default);
        Assert.Empty(empty.Scores);
        Assert.Equal(0, empty.RequestCount);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CostAccumulatesAcrossAllVotes()
    {
        var handler = new ScriptedHandler(
            VoteJson("YES", 100, 2), VoteJson("YES", 100, 2), VoteJson("YES", 100, 2));
        var result = await CreateScorer(handler, inputPrice: 1m, outputPrice: 2m)
            .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        // 3 * (100*1 + 2*2) / 1e6 = 3 * 0.000104 = 0.000312
        Assert.Equal(0.000312m, result.EstimatedCostUsd);
    }

    [Fact]
    public async Task MissingUsage_ZeroCost()
    {
        var body = """{"choices":[{"message":{"content":"YES"}}]}""";
        var handler = new ScriptedHandler(body, body, body);
        var result = await CreateScorer(handler, inputPrice: 1m, outputPrice: 2m)
            .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default);
        Assert.Equal(0m, result.EstimatedCostUsd);
    }

    [Fact]
    public async Task Cancellation_Stops_NotWrapped()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateScorer(new ScriptedHandler(VoteJson("YES"), VoteJson("YES"), VoteJson("YES")))
                .ScoreAsync("S", [new PageFitCandidate(PageA, "P")], cts.Token));
    }

    [Fact]
    public void Di_ModeVote_ResolvesVotingScorer()
    {
        using var sp = BuildDi(new Dictionary<string, string?>
        {
            ["PageFit:Mode"] = "Vote",
            ["PageFit:TimeoutSeconds"] = "33",
            ["PageFit:Models:0"] = "a",
            ["PageFit:Models:1"] = "b",
            ["PageFit:Models:2"] = "c",
            ["AiProviders:DefaultProvider"] = "test",
            ["AiProviders:Providers:test:BaseUrl"] = "http://ai.test",
            ["AiProviders:Providers:test:ApiKey"] = "k",
        });
        Assert.IsType<VotingPageFitScorer>(sp.GetRequiredService<IPageFitScorer>());
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        Assert.Equal(TimeSpan.FromSeconds(33), factory.CreateClient(nameof(VotingPageFitScorer)).Timeout);
    }

    [Fact]
    public void Di_ModeLogprobs_ResolvesLogprobScorer()
    {
        using var sp = BuildDi(new Dictionary<string, string?>
        {
            ["PageFit:Mode"] = "Logprobs",
            ["AiProviders:DefaultProvider"] = "test",
            ["AiProviders:Providers:test:BaseUrl"] = "http://ai.test",
            ["AiProviders:Providers:test:ApiKey"] = "k",
            ["AiProviders:Providers:test:DefaultTextModel"] = "t",
        });
        Assert.IsType<LlmLogprobPageFitScorer>(sp.GetRequiredService<IPageFitScorer>());
    }

    [Fact]
    public async Task Di_InvalidMode_Fails()
    {
        using var sp = BuildDi(new Dictionary<string, string?>
        {
            ["PageFit:Mode"] = "Magic",
            ["AiProviders:DefaultProvider"] = "test",
            ["AiProviders:Providers:test:BaseUrl"] = "http://ai.test",
            ["AiProviders:Providers:test:ApiKey"] = "k",
        });
        var scorer = sp.GetRequiredService<IPageFitScorer>();
        Assert.IsType<InvalidPageFitModeScorer>(scorer);
        var ex = await Assert.ThrowsAsync<PageFitPermanentException>(() =>
            scorer.ScoreAsync("S", [new PageFitCandidate(PageA, "P")], default));
        Assert.Equal("invalid_mode", ex.ReasonCode);
    }

    // --- helpers ---

    private static ServiceProvider BuildDi(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.Configure<AiProvidersOptions>(config.GetSection("AiProviders"));
        services.AddFullMediaPageFit(config);
        return services.BuildServiceProvider();
    }

    private static VotingPageFitScorer CreateScorer(
        HttpMessageHandler handler,
        string[]? models = null,
        int maxRetries = 2,
        int minDelayMs = 0,
        decimal inputPrice = 0,
        decimal outputPrice = 0,
        TimeProvider? timeProvider = null,
        ILogger<VotingPageFitScorer>? logger = null,
        int maxRetryAfterSeconds = 60,
        int voteMaxTokens = 16,
        string provider = "")
        => CreateScorer(new HttpClient(handler), models, maxRetries, minDelayMs, inputPrice, outputPrice,
            timeProvider, logger, maxRetryAfterSeconds, voteMaxTokens, provider);

    private static VotingPageFitScorer CreateScorer(
        HttpClient http,
        string[]? models = null,
        int maxRetries = 2,
        int minDelayMs = 0,
        decimal inputPrice = 0,
        decimal outputPrice = 0,
        TimeProvider? timeProvider = null,
        ILogger<VotingPageFitScorer>? logger = null,
        int maxRetryAfterSeconds = 60,
        int voteMaxTokens = 16,
        string provider = "")
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
            Mode = "Vote",
            Provider = provider,
            Models = [..(models ?? DefaultVoters)],
            MinVoters = 3,
            VoteMaxTokens = voteMaxTokens,
            TimeoutSeconds = 60,
            MinDelayBetweenRequestsMs = minDelayMs,
            MaxRetries = maxRetries,
            MaxRetryAfterSeconds = maxRetryAfterSeconds,
            InputPricePerMillionUsd = inputPrice,
            OutputPricePerMillionUsd = outputPrice,
        });
        return new VotingPageFitScorer(
            http, ai, pageFit, timeProvider ?? TimeProvider.System, logger ?? new CollectingLogger());
    }

    private static string VoteJson(string content, int prompt = 10, int completion = 1)
        => JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } },
            usage = new { prompt_tokens = prompt, completion_tokens = completion },
        });

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
            var content = _bodies.Count > 0 ? _bodies.Dequeue() : VoteJson("YES");
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
                var content = _bodies.Count > 0 ? _bodies.Dequeue() : VoteJson("YES");
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

    private sealed class CollectingLogger : ILogger<VotingPageFitScorer>
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
