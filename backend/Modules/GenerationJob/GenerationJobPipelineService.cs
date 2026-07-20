using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Category;
using Backend.Modules.GenerationJob.Enums;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.PageContext;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.PromptTemplate;
using Backend.Modules.PromptTemplate.Enums;
using Backend.Modules.SocialChannel;
using Backend.Shared.Ai;
using Backend.Shared.Repositories;
using Backend.Shared.Storage;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.GenerationJob;

public class GenerationJobPipelineService(
    AppDbContext context,
    PostRepository postRepository,
    GenerationJobRepository jobRepository,
    MediaAssetRepository mediaAssetRepository,
    PostMediaRepository postMediaRepository,
    PageContextRepository pageContextRepository,
    PromptTemplateRepository promptTemplateRepository,
    IFileStorageService fileStorageService,
    IImageOverlayService imageOverlayService,
    IAiTextGenerationService aiTextGenerationService,
    IAiImageGenerationService aiImageGenerationService,
    IUserContext userContext,
    ILogger<GenerationJobPipelineService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private static readonly PostStatus[] QueueTextAllowedStatuses =
        [PostStatus.Draft, PostStatus.Queued, PostStatus.Failed, PostStatus.WaitingReview, PostStatus.Approved];

    private static readonly PostStatus[] QueueImageAllowedStatuses =
        [PostStatus.WaitingReview, PostStatus.NeedMedia, PostStatus.Failed, PostStatus.Approved];

    private static readonly PostStatus[] QueueRenderAllowedStatuses =
        [PostStatus.WaitingReview, PostStatus.NeedFix, PostStatus.Failed];

    /// <summary>
    /// Sinh trọn nội dung cho 1 post: text → image. Dùng chung cho create-and-generate (đồng bộ)
    /// và PostGenerationWorker (bulk, chạy nền). Caller tự Approve nếu muốn bỏ cổng duyệt
    /// (create-and-generate + worker đều auto-approve sau bước này).
    /// </summary>
    public async Task GenerateForPostAsync(Guid postId, CancellationToken ct = default)
    {
        var textJob = await QueueTextGenerationAsync(postId, ct);
        await ProcessAsync(textJob.JobId, ct);

        var imageJob = await QueueImageGenerationAsync(postId, ct);
        await ProcessAsync(imageJob.JobId, ct);
    }

    public async Task<QueueTextGenerationResponse> QueueTextGenerationAsync(
        Guid postId, CancellationToken ct = default)
    {
        var post = await RequirePostAsync(postId, ct);
        EnsurePostStatus(post, "queue text generation", QueueTextAllowedStatuses);

        var activeJob = await FindActiveJobAsync(postId, JobType.TextGeneration, ct);
        if (activeJob is not null)
            throw new ArgumentException("Đã có job sinh text đang chờ hoặc đang xử lý");

        var idempotencyKey = $"text_generation:{postId}:{DateTime.UtcNow:yyyyMMddHHmmss}";
        var flowType = ResolveFlowType(post);

        var job = await jobRepository.CreateAsync(new CreateGenerationJobRequest
        {
            PostId = postId,
            JobType = JobType.TextGeneration,
            FlowType = flowType,
            Priority = 0,
            MaxRetries = 3,
            IdempotencyKey = idempotencyKey,
            InputPayload = $"{{\"title\":\"{EscapeJson(post.Title)}\",\"flow\":\"{post.GenerationFlow}\"}}"
        }, ct);

        post.Status = PostStatus.Queued;
        post.GenerationError = null;
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);

        return new QueueTextGenerationResponse
        {
            PostId = postId,
            JobId = job.Id,
            IdempotencyKey = idempotencyKey,
            JobStatus = job.Status,
            JobType = job.JobType
        };
    }

    public async Task<QueueImageGenerationResponse> QueueImageGenerationAsync(
        Guid postId, CancellationToken ct = default)
    {
        var post = await RequirePostAsync(postId, ct);
        EnsurePostStatus(post, "queue image generation", QueueImageAllowedStatuses);

        if (string.IsNullOrWhiteSpace(post.Content))
            throw new ArgumentException("Bài viết chưa có nội dung text, cần sinh text trước");

        var activeJob = await FindActiveJobAsync(postId, JobType.ImageGeneration, ct);
        if (activeJob is not null)
            throw new ArgumentException("Đã có job sinh ảnh đang chờ hoặc đang xử lý");

        var imagePrompt = await ResolveImagePromptAsync(postId, ct);
        var idempotencyKey = $"image_generation:{postId}:{DateTime.UtcNow:yyyyMMddHHmmss}";

        var job = await jobRepository.CreateAsync(new CreateGenerationJobRequest
        {
            PostId = postId,
            JobType = JobType.ImageGeneration,
            FlowType = ResolveFlowType(post),
            Priority = 0,
            MaxRetries = 2,
            IdempotencyKey = idempotencyKey,
            InputPayload = $"{{\"postId\":\"{postId}\",\"imagePrompt\":\"{EscapeJson(imagePrompt ?? post.Title)}\"}}"
        }, ct);

        post.Status = PostStatus.GeneratingMedia;
        post.GenerationError = null;
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);

        return new QueueImageGenerationResponse
        {
            PostId = postId,
            JobId = job.Id,
            IdempotencyKey = idempotencyKey,
            JobStatus = job.Status,
            JobType = job.JobType
        };
    }

    public async Task<QueueImageRenderResponse> QueueImageRenderAsync(
        Guid postId, CancellationToken ct = default)
    {
        var post = await RequirePostAsync(postId, ct);
        EnsurePostStatus(post, "queue image render", QueueRenderAllowedStatuses);

        if (string.IsNullOrWhiteSpace(post.Content))
            throw new ArgumentException("Bài viết chưa có nội dung text");

        await RequireCoverMediaAsync(postId, ct);

        var activeJob = await FindActiveJobAsync(postId, JobType.ImageOverlay, ct);
        if (activeJob is not null)
            throw new ArgumentException("Đã có job render overlay đang chờ hoặc đang xử lý");

        var idempotencyKey = $"image_render:{postId}:{DateTime.UtcNow:yyyyMMddHHmmss}";

        var job = await jobRepository.CreateAsync(new CreateGenerationJobRequest
        {
            PostId = postId,
            JobType = JobType.ImageOverlay,
            FlowType = ResolveFlowType(post),
            Priority = 0,
            MaxRetries = 2,
            IdempotencyKey = idempotencyKey,
            InputPayload = $"{{\"postId\":\"{postId}\",\"title\":\"{EscapeJson(post.Title)}\"}}"
        }, ct);

        post.Status = PostStatus.RenderingTemplate;
        post.GenerationError = null;
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);

        return new QueueImageRenderResponse
        {
            PostId = postId,
            JobId = job.Id,
            IdempotencyKey = idempotencyKey,
            JobStatus = job.Status,
            JobType = job.JobType
        };
    }

    public async Task<ProcessGenerationJobResponse> ProcessAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var job = await RequireJobAsync(jobId, ct);
        EnsureJobStatus(job, "xử lý", JobStatus.Pending, JobStatus.Retry);

        return job.JobType switch
        {
            JobType.TextGeneration => await ProcessTextGenerationAsync(job, ct),
            JobType.ImageGeneration => await ProcessImageGenerationAsync(job, ct),
            JobType.ImageOverlay => await ProcessImageOverlayAsync(job, ct),
            _ => throw new ArgumentException($"Job type '{job.JobType}' chưa được hỗ trợ mock process")
        };
    }

    public async Task<GenerationJobModel> FailAsync(
        Guid jobId, FailGenerationJobRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ErrorCode))
            throw new ArgumentException("ErrorCode không được để trống");

        var job = await RequireJobAsync(jobId, ct);
        EnsureJobStatus(job, "fail", JobStatus.Pending, JobStatus.Retry, JobStatus.Processing);

        var post = await RequirePostAsync(job.PostId, ct);
        var sanitizedMessage = SanitizeErrorMessage(request.ErrorMessage);
        var errorCode = request.ErrorCode.Trim();

        job.RetryCount++;
        job.ErrorCode = errorCode;
        job.ErrorMessage = sanitizedMessage;
        job.CompletedAt = DateTime.UtcNow;

        if (job.RetryCount >= job.MaxRetries)
        {
            job.Status = JobStatus.DeadLetter;
            post.Status = ResolveDeadLetterPostStatus(job.JobType);
            post.GenerationError = $"[{errorCode}] {sanitizedMessage}";
        }
        else
        {
            job.Status = JobStatus.Failed;
            post.Status = ResolveRetryableFailPostStatus(job.JobType);
            post.GenerationError = $"[{errorCode}] {sanitizedMessage}";
        }

        ApplyJobUpdate(job);
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);
        return job;
    }

    public async Task<GenerationJobModel> RetryAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await RequireJobAsync(jobId, ct);
        EnsureJobStatus(job, "retry", JobStatus.Failed, JobStatus.DeadLetter);

        if (job.RetryCount >= job.MaxRetries)
            throw new ArgumentException("Job đã hết số lần retry");

        var post = await RequirePostAsync(job.PostId, ct);

        job.Status = JobStatus.Retry;
        job.ErrorMessage = null;
        job.ErrorCode = null;
        job.StartedAt = null;
        job.CompletedAt = null;
        ApplyJobUpdate(job);

        post.Status = ResolveRetryPostStatus(job.JobType);
        post.GenerationError = null;
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);
        return job;
    }

    public async Task<GenerationJobModel> CancelAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await RequireJobAsync(jobId, ct);
        EnsureJobStatus(job, "cancel", JobStatus.Pending, JobStatus.Retry, JobStatus.Processing);

        var post = await RequirePostAsync(job.PostId, ct);

        job.Status = JobStatus.Cancelled;
        job.CompletedAt = DateTime.UtcNow;
        ApplyJobUpdate(job);

        ApplyCancelPostStatus(post, job.JobType);
        await context.SaveChangesAsync(ct);
        return job;
    }

    public async Task<PostGenerationStatusResponse> GetGenerationStatusAsync(
        Guid postId, CancellationToken ct = default)
    {
        var post = await RequirePostAsync(postId, ct);
        var jobs = await jobRepository.GetByPostAsync(postId, ct);

        var lastError = jobs
            .Where(j => !string.IsNullOrWhiteSpace(j.ErrorMessage))
            .OrderByDescending(j => j.UpdatedAt ?? j.CreatedAt)
            .FirstOrDefault();

        return new PostGenerationStatusResponse
        {
            PostId = post.Id,
            PostStatus = post.Status,
            GenerationError = post.GenerationError,
            LastErrorCode = lastError?.ErrorCode,
            LastErrorMessage = lastError?.ErrorMessage,
            Steps = jobs.Select(j =>
            {
                var mediaInfo = j.Status == JobStatus.Completed
                    && j.JobType is JobType.ImageGeneration or JobType.ImageOverlay
                    ? ParseMediaJobOutput(j.OutputPayload)
                    : null;

                return new GenerationStepResponse
                {
                    JobId = j.Id,
                    JobType = j.JobType,
                    JobStatus = j.Status,
                    FlowType = j.FlowType,
                    RetryCount = j.RetryCount,
                    MaxRetries = j.MaxRetries,
                    IdempotencyKey = j.IdempotencyKey,
                    ErrorCode = j.ErrorCode,
                    StartedAt = j.StartedAt,
                    CompletedAt = j.CompletedAt,
                    ErrorMessage = j.ErrorMessage,
                    OutputPayload = j.Status == JobStatus.Completed ? j.OutputPayload : null,
                    MediaAssetId = mediaInfo?.MediaAssetId,
                    PostMediaId = mediaInfo?.PostMediaId,
                    PublicUrl = mediaInfo?.PublicUrl
                };
            }).ToList()
        };
    }

    // --- process handlers ---

    private async Task<ProcessGenerationJobResponse> ProcessTextGenerationAsync(
        GenerationJobModel job, CancellationToken ct)
    {
        var post = await RequirePostAsync(job.PostId, ct);

        job.Status = JobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        job.ErrorMessage = null;
        job.ErrorCode = null;
        ApplyJobUpdate(job);

        post.Status = PostStatus.Generating;
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);

        // Build request (resolve template + thay biến) trước, ghi vào InputPayload để audit/verify.
        var request = await BuildAiTextRequestAsync(post, ct);
        job.InputPayload = BuildTextJobInputPayload(request);
        var output = await GenerateTextOutputAsync(post, request, ct);
        var outputJson = MockTextGenerator.ToJson(output);

        job.Status = JobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        job.OutputPayload = outputJson;
        ApplyJobUpdate(job);

        post.Content = output.Content;
        post.ExtraJson = MergeTextGenerationExtraJson(post.ExtraJson, output);
        post.Status = PostStatus.WaitingReview;
        post.GenerationError = null;
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);

        return new ProcessGenerationJobResponse
        {
            JobId = job.Id,
            PostId = post.Id,
            JobType = job.JobType,
            JobStatus = job.Status,
            OutputPayload = outputJson
        };
    }

    private async Task<TextGenerationJobOutput> GenerateTextOutputAsync(
        PostModel post, AiTextGenerationRequest request, CancellationToken ct)
    {
        if (aiTextGenerationService.IsAvailable())
        {
            try
            {
                var aiResult = await aiTextGenerationService.GenerateAsync(request, ct);
                logger.LogInformation(
                    "AI text generation succeeded for post {PostId} via provider {Provider}",
                    post.Id, aiResult.Provider ?? request.Provider ?? "default");
                request.Provider ??= aiResult.Provider;
                request.Model ??= aiResult.Model;
                return MapAiResult(aiResult, request);
            }
            catch (AiProviderUnavailableException)
            {
                logger.LogInformation(
                    "AI text generation unavailable for post {PostId}, using mock fallback",
                    post.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "AI text generation failed for post {PostId}, using mock fallback",
                    post.Id);
            }
        }

        var mock = MockTextGenerator.Generate(post);
        return new TextGenerationJobOutput
        {
            Source = "mock",
            Content = mock.Content,
            Hashtags = mock.Hashtags,
            Cta = mock.Cta,
            ImagePrompt = mock.ImagePrompt
        };
    }

    private async Task<AiTextGenerationRequest> BuildAiTextRequestAsync(
        PostModel post, CancellationToken ct)
    {
        var ctx = await ResolvePromptContextAsync(post, ct);

        var request = new AiTextGenerationRequest
        {
            Title = post.Title,
            Objective = ctx.Objective,
            Category = ctx.Category,
            BrandContext = ctx.Brand,
            Tone = ctx.Tone,
            CtaText = ctx.Cta,
            Hashtags = ctx.Hashtags
        };

        var templateBody = await ResolveTextTemplateBodyAsync(post, ctx.PageContext, ct);
        if (!string.IsNullOrWhiteSpace(templateBody))
        {
            request.PromptOverride = PromptTemplateRenderer.Render(
                templateBody, ctx.ToVariables(title: post.Title));
            logger.LogInformation(
                "Post {PostId} text generation uses prompt template ({Len} chars rendered, pageContext={HasPc})",
                post.Id, request.PromptOverride.Length, ctx.PageContext is not null);
        }

        return request;
    }

    /// <summary>
    /// Gom biến template: PageContext nếu có; không có thì fallback từ tên Page kênh + default ổn định
    /// để prompt vẫn mạch lạc (không để {{brand}}/{{cta}} rỗng).
    /// </summary>
    private async Task<ResolvedPromptContext> ResolvePromptContextAsync(PostModel post, CancellationToken ct)
    {
        var pageContext = await pageContextRepository.GetByChannelAsync(post.SocialChannelId, ct);
        var categoryName = await ResolveCategoryNameAsync(post, ct);
        var channelName = await context.Set<SocialChannelModel>()
            .Where(x => !x.IsDeleted && x.Id == post.SocialChannelId)
            .Select(x => x.PageName)
            .FirstOrDefaultAsync(ct);

        var category = FirstNonEmpty(categoryName, "Chung")!;
        var brand = FirstNonEmpty(pageContext?.BrandName, channelName, "Page của bạn")!;
        var tone = FirstNonEmpty(pageContext?.ToneOfVoice, "thân thiện, rõ ràng, chuyên nghiệp, gần gũi như nói chuyện trên Facebook")!;
        var cta = FirstNonEmpty(
            pageContext?.CtaText,
            "Inbox ngay để được tư vấn chi tiết nhé 💬")!;
        var hashtags = FirstNonEmpty(pageContext?.DefaultHashtags, BuildFallbackHashtags(category))!;
        var audience = "khách hàng mục tiêu trên Facebook";
        var objective = FirstNonEmpty(ExtractObjective(post.ExtraJson), post.Title.Trim())!;

        if (pageContext is null)
        {
            logger.LogInformation(
                "Post {PostId}: không có PageContext cho channel {ChannelId} — dùng fallback brand={Brand}, tone, cta, hashtags",
                post.Id, post.SocialChannelId, brand);
        }

        return new ResolvedPromptContext(
            pageContext, category, brand, tone, audience, objective, cta, hashtags);
    }

    private static string BuildFallbackHashtags(string category)
    {
        var slug = new string(category
            .Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '/')
            .ToArray())
            .Trim()
            .Replace('/', ' ')
            .Replace(' ', '_');
        while (slug.Contains("__", StringComparison.Ordinal))
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        slug = slug.Trim('_');
        if (string.IsNullOrWhiteSpace(slug))
            return "#Facebook #Marketing #BanHang";
        return $"#{slug} #Facebook #Marketing #BanHang";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return null;
    }

    private async Task<string?> ResolveCategoryNameAsync(PostModel post, CancellationToken ct)
    {
        if (post.CategoryId.HasValue)
        {
            var fromCategory = await context.Set<CategoryModel>()
                .Where(x => !x.IsDeleted && x.Id == post.CategoryId.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(fromCategory))
                return fromCategory;
        }

        var templateId = post.TextTemplateId ?? post.ImageTemplateId;
        if (templateId is Guid tid)
        {
            var tpl = await promptTemplateRepository.GetActiveByIdAsync(tid, ct);
            if (!string.IsNullOrWhiteSpace(tpl?.Name))
                return tpl.Name;
        }

        return null;
    }

    /// <summary>Ưu tiên: Post.TextTemplateId → PageContext → default Category pack → legacy Text default.</summary>
    private async Task<string?> ResolveTextTemplateBodyAsync(
        PostModel post, PageContextModel? pageContext, CancellationToken ct)
    {
        if (post.TextTemplateId is Guid postTid)
        {
            var t = await promptTemplateRepository.GetActiveByIdAsync(postTid, ct);
            var body = t?.ResolveTextBody();
            if (!string.IsNullOrWhiteSpace(body)) return body;
        }
        if (pageContext?.DefaultTextTemplateId is Guid pageTid)
        {
            var t = await promptTemplateRepository.GetActiveByIdAsync(pageTid, ct);
            var body = t?.ResolveTextBody();
            if (!string.IsNullOrWhiteSpace(body)) return body;
        }
        if (!string.IsNullOrWhiteSpace(pageContext?.PromptTemplateText))
            return pageContext!.PromptTemplateText;

        var catDef = await promptTemplateRepository.GetDefaultCategoryAsync(ct);
        var catBody = catDef?.ResolveTextBody();
        if (!string.IsNullOrWhiteSpace(catBody)) return catBody;

        var legacy = await promptTemplateRepository.GetDefaultAsync(PromptTemplateType.TextContent, ct);
        return string.IsNullOrWhiteSpace(legacy?.ResolveTextBody()) ? null : legacy!.ResolveTextBody();
    }

    /// <summary>Ưu tiên: Post.ImageTemplateId → PageContext → default Category pack → legacy Image default.</summary>
    private async Task<string?> ResolveImageTemplateBodyAsync(
        PostModel post, PageContextModel? pageContext, CancellationToken ct)
    {
        if (post.ImageTemplateId is Guid postIid)
        {
            var t = await promptTemplateRepository.GetActiveByIdAsync(postIid, ct);
            var body = t?.ResolveImageBody();
            if (!string.IsNullOrWhiteSpace(body)) return body;
        }
        if (pageContext?.DefaultImageTemplateId is Guid pageIid)
        {
            var t = await promptTemplateRepository.GetActiveByIdAsync(pageIid, ct);
            var body = t?.ResolveImageBody();
            if (!string.IsNullOrWhiteSpace(body)) return body;
        }
        if (!string.IsNullOrWhiteSpace(pageContext?.PromptTemplateImage))
            return pageContext!.PromptTemplateImage;

        var catDef = await promptTemplateRepository.GetDefaultCategoryAsync(ct);
        var catBody = catDef?.ResolveImageBody();
        if (!string.IsNullOrWhiteSpace(catBody)) return catBody;

        var legacy = await promptTemplateRepository.GetDefaultAsync(PromptTemplateType.Image, ct);
        return string.IsNullOrWhiteSpace(legacy?.ResolveImageBody()) ? null : legacy!.ResolveImageBody();
    }

    private sealed record ResolvedPromptContext(
        PageContextModel? PageContext,
        string Category,
        string Brand,
        string Tone,
        string Audience,
        string Objective,
        string Cta,
        string Hashtags)
    {
        public Dictionary<string, string?> ToVariables(
            string? title = null, string? caption = null, string? aiImagePrompt = null)
            => new(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = title,
                ["category"] = Category,
                ["brand"] = Brand,
                ["tone"] = Tone,
                ["audience"] = Audience,
                ["objective"] = Objective,
                ["cta"] = Cta,
                ["hashtags"] = Hashtags,
                ["caption"] = caption,
                ["imagePrompt"] = aiImagePrompt
            };
    }

    private static string BuildTextJobInputPayload(AiTextGenerationRequest request)
        => JsonSerializer.Serialize(new
        {
            provider = request.Provider,
            model = request.Model,
            title = request.Title,
            objective = request.Objective,
            category = request.Category,
            tone = request.Tone,
            brand = request.BrandContext,
            cta = request.CtaText,
            hashtags = request.Hashtags,
            usedTemplate = !string.IsNullOrWhiteSpace(request.PromptOverride),
            promptOverride = request.PromptOverride
        }, JsonOptions);

    private static string? ExtractObjective(string? extraJson)
    {
        if (string.IsNullOrWhiteSpace(extraJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            if (doc.RootElement.TryGetProperty("input", out var input)
                && input.TryGetProperty("objective", out var obj))
                return obj.GetString();
        }
        catch (JsonException)
        {
            // ExtraJson không phải JSON hợp lệ — bỏ qua.
        }
        return null;
    }

    private static TextGenerationJobOutput MapAiResult(
        AiTextGenerationResult ai, AiTextGenerationRequest request)
    {
        var hashtags = NormalizeHashtags(ai.Hashtags);
        var cta = string.IsNullOrWhiteSpace(ai.Cta)
            ? (request.CtaText?.Trim() ?? "Inbox ngay để được tư vấn chi tiết nhé 💬")
            : ai.Cta.Trim();

        return new TextGenerationJobOutput
        {
            Source = "ai",
            Provider = request.Provider,
            Model = request.Model,
            Content = ComposeFacebookPost(ai.Caption, cta, hashtags),
            Hashtags = hashtags,
            Cta = cta,
            ImagePrompt = ai.ImagePrompt,
            BannerHeadline = ai.BannerHeadline,
            BannerSubheadline = ai.BannerSubheadline,
            BannerCta = ai.BannerCta
        };
    }

    /// <summary>
    /// Ghép caption + CTA + hashtag thành 1 bài Facebook sẵn đăng
    /// (tránh mất CTA/hashtag khi chỉ lưu field caption).
    /// </summary>
    private static string ComposeFacebookPost(string? caption, string cta, IReadOnlyList<string> hashtags)
    {
        var body = (caption ?? string.Empty).Trim();
        var sb = new StringBuilder(body);

        if (!string.IsNullOrWhiteSpace(cta) && !ContainsIgnoreCase(body, cta))
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(cta.Trim());
        }

        if (hashtags.Count > 0)
        {
            var tagLine = string.Join(' ', hashtags);
            if (!ContainsIgnoreCase(sb.ToString(), tagLine) && !hashtags.All(t => ContainsIgnoreCase(sb.ToString(), t)))
            {
                sb.Append("\n\n");
                sb.Append(tagLine);
            }
        }

        return sb.ToString().Trim();
    }

    private static List<string> NormalizeHashtags(IEnumerable<string>? tags)
    {
        var list = new List<string>();
        if (tags is null) return list;
        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var t = raw.Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            t = t.Replace(' ', '_');
            if (!list.Contains(t, StringComparer.OrdinalIgnoreCase))
                list.Add(t);
        }
        return list.Take(8).ToList();
    }

    private static bool ContainsIgnoreCase(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string? MergeTextGenerationExtraJson(
        string? existingExtraJson, TextGenerationJobOutput output)
    {
        var payload = new Dictionary<string, object?>
        {
            ["textGeneration"] = new
            {
                output.Source,
                output.Provider,
                output.Model,
                output.Hashtags,
                output.Cta,
                output.ImagePrompt,
                output.BannerHeadline,
                output.BannerSubheadline,
                output.BannerCta
            }
        };

        if (string.IsNullOrWhiteSpace(existingExtraJson))
            return JsonSerializer.Serialize(payload, JsonOptions);

        try
        {
            var merged = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingExtraJson, JsonOptions)
                ?? new Dictionary<string, JsonElement>();
            merged["textGeneration"] = JsonSerializer.SerializeToElement(payload["textGeneration"], JsonOptions);
            return JsonSerializer.Serialize(merged, JsonOptions);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(payload, JsonOptions);
        }
    }

    private async Task<ProcessGenerationJobResponse> ProcessImageGenerationAsync(
        GenerationJobModel job, CancellationToken ct)
    {
        var post = await RequirePostAsync(job.PostId, ct);

        job.Status = JobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        job.ErrorMessage = null;
        job.ErrorCode = null;
        ApplyJobUpdate(job);

        post.Status = PostStatus.GeneratingMedia;
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);

        var imagePrompt = await ResolveImagePromptAsync(post.Id, ct);
        var image = await GenerateImageAssetAsync(post, imagePrompt, ct);

        var saveResult = await fileStorageService.SaveBytesAsync(
            image.Bytes,
            "ai-generated",
            image.Extension,
            image.MimeType,
            ct);

        var mediaAsset = await mediaAssetRepository.CreateAsync(new CreateMediaAssetRequest
        {
            FileName = saveResult.StorageKey.Split('/').Last(),
            OriginalFileName = image.OriginalFileName,
            StoragePath = saveResult.StorageKey,
            MimeType = saveResult.ContentType,
            FileSize = saveResult.SizeBytes,
            Source = image.Source,
            AltText = image.AltText,
            Description = image.Description,
            Width = image.Width,
            Height = image.Height,
            Tags = image.Tags
        }, ct);

        await mediaAssetRepository.SetPreviewUrlAsync(mediaAsset, ct);
        var previewUrl = MediaAssetUrls.Preview(mediaAsset.Id);

        // Replace (không cộng dồn) — "tạo lại ảnh" thay cover cũ thay vì thêm cover mới.
        var postMedia = await postMediaRepository.ReplaceCoverAsync(post.Id, mediaAsset.Id, ct);

        var outputJson = JsonSerializer.Serialize(new
        {
            mediaAssetId = mediaAsset.Id,
            postMediaId = postMedia.Id,
            previewUrl,
            prompt = image.Prompt,
            source = image.GenSource,
            provider = image.Provider,
            model = image.Model,
            mimeType = mediaAsset.MimeType
        }, JsonOptions);

        job.Status = JobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        job.OutputPayload = outputJson;
        ApplyJobUpdate(job);

        post.Status = PostStatus.WaitingReview;
        post.GenerationError = null;
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);

        return new ProcessGenerationJobResponse
        {
            JobId = job.Id,
            PostId = post.Id,
            JobType = job.JobType,
            JobStatus = job.Status,
            OutputPayload = outputJson,
            MediaAssetId = mediaAsset.Id,
            PostMediaId = postMedia.Id,
            PublicUrl = previewUrl
        };
    }

    private async Task<ProcessGenerationJobResponse> ProcessImageOverlayAsync(
        GenerationJobModel job, CancellationToken ct)
    {
        var post = await RequirePostAsync(job.PostId, ct);

        job.Status = JobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        job.ErrorMessage = null;
        job.ErrorCode = null;
        ApplyJobUpdate(job);

        post.Status = PostStatus.RenderingTemplate;
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);

        var (_, sourceMedia) = await RequireCoverMediaAsync(post.Id, ct);
        var pageContext = await pageContextRepository.GetByChannelAsync(post.SocialChannelId, ct);
        var ctaText = string.IsNullOrWhiteSpace(pageContext?.CtaText) ? "Đăng ký ngay" : pageContext.CtaText.Trim();
        var headline = BuildHeadline(post);
        var logoStorageKey = await ResolveLogoStorageKeyAsync(pageContext?.LogoMediaId, ct);

        var renderResult = await imageOverlayService.RenderAsync(new ImageOverlayRequest
        {
            SourceStorageKey = sourceMedia.StoragePath,
            PostTitle = post.Title,
            Headline = headline,
            CtaText = ctaText,
            OutputFolder = "rendered",
            LogoStorageKey = logoStorageKey
        }, ct);

        var renderedAsset = await mediaAssetRepository.CreateAsync(new CreateMediaAssetRequest
        {
            FileName = renderResult.StorageKey.Split('/').Last(),
            OriginalFileName = $"{SanitizeFileName(post.Title)}-rendered.png",
            StoragePath = renderResult.StorageKey,
            MimeType = renderResult.ContentType,
            FileSize = renderResult.SizeBytes,
            Source = MediaSource.Overlay,
            AltText = $"Rendered cover for {post.Title}",
            Description = headline,
            Width = renderResult.Width > 0 ? renderResult.Width : null,
            Height = renderResult.Height > 0 ? renderResult.Height : null,
            Tags = renderResult.UsedFallbackCopy
                ? "{\"overlay\":\"fallback-copy\"}"
                : $"{{\"overlay\":\"imagesharp\",\"textRendered\":{renderResult.TextRendered.ToString().ToLowerInvariant()}}}"
        }, ct);

        await mediaAssetRepository.SetPreviewUrlAsync(renderedAsset, ct);
        var previewUrl = MediaAssetUrls.Preview(renderedAsset.Id);

        var postMedia = await postMediaRepository.ReplaceCoverAsync(post.Id, renderedAsset.Id, ct);

        var outputJson = RenderOutputHelper.ToJson(
            renderedAsset.Id, postMedia.Id, previewUrl, renderResult);

        job.Status = JobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        job.OutputPayload = outputJson;
        ApplyJobUpdate(job);

        post.Status = PostStatus.WaitingReview;
        post.GenerationError = null;
        ApplyPostUpdate(post);
        await context.SaveChangesAsync(ct);

        return new ProcessGenerationJobResponse
        {
            JobId = job.Id,
            PostId = post.Id,
            JobType = job.JobType,
            JobStatus = job.Status,
            OutputPayload = outputJson,
            MediaAssetId = renderedAsset.Id,
            PostMediaId = postMedia.Id,
            PublicUrl = previewUrl
        };
    }

    // --- helpers ---

    private async Task<PostModel> RequirePostAsync(Guid id, CancellationToken ct)
    {
        var post = await postRepository.GetByIdAsync(id, ct);
        if (post is null) throw new KeyNotFoundException("Không tìm thấy bài viết");
        return post;
    }

    private async Task<GenerationJobModel> RequireJobAsync(Guid id, CancellationToken ct)
    {
        var job = await jobRepository.GetByIdAsync(id, ct);
        if (job is null) throw new KeyNotFoundException("Không tìm thấy generation job");
        return job;
    }

    private async Task<(PostMediaModel PostMedia, MediaAssetModel Media)> RequireCoverMediaAsync(
        Guid postId, CancellationToken ct)
    {
        var postMedias = await postMediaRepository.GetByPostAsync(postId, ct);
        if (postMedias.Count == 0)
            throw new ArgumentException("Bài viết chưa có media để render overlay");

        var cover = postMedias.FirstOrDefault(pm => pm.MediaRole == MediaRole.Cover)
            ?? postMedias.FirstOrDefault(pm => pm.MediaRole == MediaRole.Primary)
            ?? postMedias.First();

        var media = await mediaAssetRepository.GetByIdAsync(cover.MediaId, ct);
        if (media is null || string.IsNullOrWhiteSpace(media.StoragePath))
            throw new ArgumentException("Media gốc không hợp lệ");

        if (!await fileStorageService.ExistsAsync(media.StoragePath, ct))
            throw new ArgumentException("File media gốc không tồn tại trên storage");

        return (cover, media);
    }

    private async Task<string?> ResolveLogoStorageKeyAsync(Guid? logoMediaId, CancellationToken ct)
    {
        if (!logoMediaId.HasValue) return null;
        var logo = await mediaAssetRepository.GetByIdAsync(logoMediaId.Value, ct);
        if (logo is null || string.IsNullOrWhiteSpace(logo.StoragePath)) return null;
        return await fileStorageService.ExistsAsync(logo.StoragePath, ct) ? logo.StoragePath : null;
    }

    private async Task<GenerationJobModel?> FindActiveJobAsync(
        Guid postId, JobType jobType, CancellationToken ct)
        => await context.Set<GenerationJobModel>()
            .Where(x => !x.IsDeleted
                && x.PostId == postId
                && x.JobType == jobType
                && (x.Status == JobStatus.Pending
                    || x.Status == JobStatus.Retry
                    || x.Status == JobStatus.Processing))
            .FirstOrDefaultAsync(ct);

    private async Task<string?> ResolveImagePromptAsync(Guid postId, CancellationToken ct)
    {
        var textJob = await context.Set<GenerationJobModel>()
            .Where(x => !x.IsDeleted
                && x.PostId == postId
                && x.JobType == JobType.TextGeneration
                && x.Status == JobStatus.Completed
                && x.OutputPayload != null)
            .OrderByDescending(x => x.CompletedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return textJob is null
            ? null
            : MockImageGenerator.TryExtractImagePrompt(textJob.OutputPayload);
    }

    private static JobFlowType ResolveFlowType(PostModel post)
        => post.GenerationFlow == GenerationFlow.RAG ? JobFlowType.RAG : JobFlowType.FullAI;

    private static PostStatus ResolveDeadLetterPostStatus(JobType jobType) => jobType switch
    {
        JobType.ImageGeneration => PostStatus.NeedMedia,
        JobType.ImageOverlay => PostStatus.NeedFix,
        _ => PostStatus.Failed
    };

    private static PostStatus ResolveRetryableFailPostStatus(JobType jobType) => jobType switch
    {
        JobType.TextGeneration => PostStatus.Queued,
        JobType.ImageGeneration or JobType.ImageOverlay => PostStatus.WaitingReview,
        _ => PostStatus.Queued
    };

    private static PostStatus ResolveRetryPostStatus(JobType jobType) => jobType switch
    {
        JobType.ImageGeneration => PostStatus.GeneratingMedia,
        JobType.ImageOverlay => PostStatus.RenderingTemplate,
        _ => PostStatus.Queued
    };

    private void ApplyCancelPostStatus(PostModel post, JobType jobType)
    {
        switch (jobType)
        {
            case JobType.ImageGeneration when post.Status == PostStatus.GeneratingMedia:
                post.Status = PostStatus.WaitingReview;
                ApplyPostUpdate(post);
                break;
            case JobType.ImageOverlay when post.Status == PostStatus.RenderingTemplate:
                post.Status = PostStatus.WaitingReview;
                ApplyPostUpdate(post);
                break;
            case JobType.TextGeneration when post.Status is PostStatus.Queued or PostStatus.Generating:
                post.Status = PostStatus.Draft;
                ApplyPostUpdate(post);
                break;
        }
    }

    private static string BuildHeadline(PostModel post)
    {
        if (!string.IsNullOrWhiteSpace(post.Title)) return post.Title.Trim();
        if (string.IsNullOrWhiteSpace(post.Content)) return "VNI Automation";
        var line = post.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(line)) return "VNI Automation";
        return line.Length > 80 ? line[..80] : line;
    }

    private static MediaJobOutputInfo? ParseMediaJobOutput(string? outputPayload)
    {
        if (string.IsNullOrWhiteSpace(outputPayload)) return null;
        try
        {
            return JsonSerializer.Deserialize<MediaJobOutputInfo>(outputPayload, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void EnsurePostStatus(PostModel post, string action, params PostStatus[] allowed)
    {
        if (!allowed.Contains(post.Status))
            throw new ArgumentException(
                $"Không thể {action} khi bài viết đang ở trạng thái '{post.Status}'");
    }

    private static void EnsureJobStatus(GenerationJobModel job, string action, params JobStatus[] allowed)
    {
        if (!allowed.Contains(job.Status))
            throw new ArgumentException(
                $"Không thể {action} job khi trạng thái là '{job.Status}'");
    }

    private static string SanitizeErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown error";
        var trimmed = message.Trim();
        if (trimmed.Length > 500) trimmed = trimmed[..500];
        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("password") || lower.Contains("token") || lower.Contains("secret"))
            return "Error details redacted";
        return trimmed;
    }

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string SanitizeFileName(string input)
    {
        var name = new string(input.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(name) ? "post" : name[..Math.Min(name.Length, 80)];
    }

    private void ApplyPostUpdate(PostModel post)
    {
        post.UpdatedAt = DateTime.UtcNow;
        post.UpdatedBy = userContext.GetCurrentUserName();
    }

    private void ApplyJobUpdate(GenerationJobModel job)
    {
        job.UpdatedAt = DateTime.UtcNow;
        job.UpdatedBy = userContext.GetCurrentUserName();
    }

    // --- image generation (real AI with mock fallback) ---

    /// <summary>
    /// Dựng prompt ảnh: nếu có template ảnh (Post/Page/default) thì render template (biến gồm cả
    /// {{imagePrompt}} do AI text gợi ý và {{caption}} nội dung bài); ngược lại dùng imagePrompt AI, cuối cùng generic.
    /// </summary>
    private async Task<string> BuildImagePromptAsync(
        PostModel post, string? aiImagePrompt, CancellationToken ct)
    {
        var ctx = await ResolvePromptContextAsync(post, ct);
        var templateBody = await ResolveImageTemplateBodyAsync(post, ctx.PageContext, ct);

        if (!string.IsNullOrWhiteSpace(templateBody))
        {
            var vars = ctx.ToVariables(
                title: post.Title, caption: post.Content, aiImagePrompt: aiImagePrompt);
            var rendered = PromptTemplateRenderer.Render(templateBody, vars);
            if (!string.IsNullOrWhiteSpace(rendered))
            {
                logger.LogInformation(
                    "Post {PostId} image generation uses prompt template (pageContext={HasPc})",
                    post.Id, ctx.PageContext is not null);
                return rendered;
            }
        }

        return string.IsNullOrWhiteSpace(aiImagePrompt)
            ? $"Professional social media visual for '{post.Title.Trim()}', brand '{ctx.Brand}', modern clean style, no text"
            : aiImagePrompt.Trim();
    }

    private async Task<GeneratedImageAsset> GenerateImageAssetAsync(
        PostModel post, string? imagePrompt, CancellationToken ct)
    {
        var prompt = await BuildImagePromptAsync(post, imagePrompt, ct);

        if (aiImageGenerationService.IsAvailable())
        {
            try
            {
                var ai = await aiImageGenerationService.GenerateAsync(
                    new AiImageGenerationRequest { Prompt = prompt }, ct);

                logger.LogInformation(
                    "AI image generation succeeded for post {PostId} via {Provider}/{Model} ({Bytes} bytes)",
                    post.Id, ai.Provider, ai.Model, ai.ImageBytes.Length);

                var ext = ExtensionForMime(ai.MimeType);
                return new GeneratedImageAsset
                {
                    Bytes = ai.ImageBytes,
                    MimeType = ai.MimeType,
                    Extension = ext,
                    Source = MediaSource.AIGenerated,
                    AltText = $"AI image for {post.Title.Trim()}",
                    Description = prompt,
                    OriginalFileName = $"ai-{SanitizeFileName(post.Title)}{ext}",
                    Prompt = prompt,
                    GenSource = "ai",
                    Provider = ai.Provider,
                    Model = ai.Model,
                    Tags = $"{{\"imageGen\":\"{EscapeJson(ai.Provider)}\",\"model\":\"{EscapeJson(ai.Model)}\"}}"
                };
            }
            catch (AiProviderUnavailableException ex)
            {
                logger.LogInformation(
                    "AI image unavailable for post {PostId}: {Message}. Using mock placeholder.", post.Id, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "AI image generation failed for post {PostId}. Using mock placeholder.", post.Id);
            }
        }

        var mock = MockImageGenerator.Generate(post, prompt);
        return new GeneratedImageAsset
        {
            Bytes = MockImageGenerator.GetPlaceholderPngBytes(),
            MimeType = mock.MimeType,
            Extension = ".png",
            Source = mock.Source,
            AltText = mock.AltText,
            Description = mock.Description,
            OriginalFileName = mock.OriginalFileName,
            Width = mock.Width,
            Height = mock.Height,
            Prompt = mock.Prompt,
            GenSource = "mock",
            Provider = "mock",
            Model = "mock",
            Tags = "{\"imageGen\":\"mock\"}"
        };
    }

    private static string ExtensionForMime(string? mimeType) => mimeType?.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".png"
    };

    private sealed class GeneratedImageAsset
    {
        public byte[] Bytes { get; init; } = [];
        public string MimeType { get; init; } = "image/png";
        public string Extension { get; init; } = ".png";
        public MediaSource Source { get; init; }
        public string AltText { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string OriginalFileName { get; init; } = string.Empty;
        public int? Width { get; init; }
        public int? Height { get; init; }
        public string Prompt { get; init; } = string.Empty;
        public string GenSource { get; init; } = "mock";
        public string Provider { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public string Tags { get; init; } = string.Empty;
    }

    private sealed class MediaJobOutputInfo
    {
        public Guid MediaAssetId { get; set; }
        public Guid PostMediaId { get; set; }
        public string? PublicUrl { get; set; }
        public string? PreviewUrl { get; set; }
    }

}
