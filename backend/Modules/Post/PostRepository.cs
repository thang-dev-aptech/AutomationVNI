using Backend.Data;
using Backend.Modules.PageContext;
using Backend.Modules.Post.Enums;
using Backend.Modules.PromptTemplate;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Post;

public class PostRepository : GenericRepository<PostModel>, IGenericRepository<PostModel>
{
    public PostRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<PostResponse>> FilterAsync(
        PostFilterRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.Title.Contains(keyword) || (x.Content != null && x.Content.Contains(keyword)));
        }

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);

        if (request.SocialChannelId.HasValue)
            query = query.Where(x => x.SocialChannelId == request.SocialChannelId.Value);

        if (request.GenerationFlow.HasValue)
            query = query.Where(x => x.GenerationFlow == request.GenerationFlow.Value);

        if (request.IsRecycled == true)
            query = query.Where(x => x.SourcePostId != null);
        
        if (request.IsRecycled == false)
            query = query.Where(x => x.SourcePostId == null);

        if (request.FromDate.HasValue)
            query = query.Where(x => x.CreatedAt >= request.FromDate.Value);

        if (request.ToDate.HasValue)
            query = query.Where(x => x.CreatedAt <= request.ToDate.Value);

        var paged = await PaginateAsync(query, request.Index, request.Size, cancellationToken);
        var names = await LoadTemplateNamesAsync(paged.Items, cancellationToken);
        return new PagedResult<PostResponse>
        {
            Items = paged.Items.Select(e => ToResponse(e, ResolveTemplateName(e, names))).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<PostResponse?> GetResponseByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;
        var names = await LoadTemplateNamesAsync([entity], ct);
        return ToResponse(entity, ResolveTemplateName(entity, names));
    }

    public async Task<PostModel> CreateAsync(
        CreatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        var textTpl = request.TextTemplateId;
        var imageTpl = request.ImageTemplateId;

        // Một template danh mục → gắn cả text + ảnh.
        if (request.PromptTemplateId is Guid packId && packId != Guid.Empty)
        {
            textTpl = packId;
            imageTpl = packId;
        }

        var channelId = request.SocialChannelId;
        if (channelId == Guid.Empty)
            throw new ArgumentException("Phải chọn kênh đăng");

        var entity = new PostModel
        {
            Title = request.Title.Trim(),
            SocialChannelId = channelId,
            CategoryId = request.CategoryId,
            GenerationFlow = request.GenerationFlow,
            TextTemplateId = textTpl,
            ImageTemplateId = imageTpl,
            UserId = GetCurrentUserId(),
            Status = PostStatus.Draft
        };

        if (!string.IsNullOrWhiteSpace(request.Objective))
            entity.ExtraJson = System.Text.Json.JsonSerializer.Serialize(
                new { input = new { objective = request.Objective.Trim() } });

        return await base.CreateAsync(entity, cancellationToken);
    }

    /// <summary>Tạo hàng loạt post (fan-out items × channels) ở Status=Queued cho worker sinh nền.</summary>
    public async Task<BulkCreateResult> BulkCreateAsync(
        BulkCreatePostRequest request,
        IReadOnlyDictionary<Guid, PageContextModel>? pageContextByChannel = null,
        CancellationToken ct = default)
    {
        var items = (request.Items ?? []).Where(i => !string.IsNullOrWhiteSpace(i.Idea)).ToList();
        var channels = (request.ChannelIds ?? []).Where(c => c != Guid.Empty).Distinct().ToList();
        if (items.Count == 0) throw new ArgumentException("Danh sách ý tưởng trống");
        if (channels.Count == 0) throw new ArgumentException("Phải chọn ít nhất một kênh đăng");

        var batchTemplate = request.PromptTemplateId is Guid p && p != Guid.Empty
            ? p
            : (Guid?)null;
        var legacyText = request.TextTemplateId;
        var legacyImage = request.ImageTemplateId;
        var pageMap = pageContextByChannel ?? new Dictionary<Guid, PageContextModel>();

        var batchId = Guid.NewGuid();
        var userId = GetCurrentUserId();
        var posts = new List<PostModel>();
        foreach (var ch in channels)
            foreach (var it in items)
            {
                Guid? textTpl;
                Guid? imageTpl;
                var itemPack = it.PromptTemplateId is Guid ip && ip != Guid.Empty ? ip : batchTemplate;
                if (itemPack is Guid pack)
                {
                    textTpl = pack;
                    imageTpl = pack;
                }
                else
                {
                    // Không chọn danh mục cho batch → dùng template mặc định trong PageContext của page,
                    // giống luồng tạo bài đơn. Thiếu cả hai thì để null (pipeline tự fallback default).
                    pageMap.TryGetValue(ch, out var pc);
                    var (pcText, pcImage) = PageContextRepository.ResolveDefaultTemplateIds(pc);
                    textTpl = it.TextTemplateId ?? legacyText ?? pcText;
                    imageTpl = it.ImageTemplateId ?? legacyImage ?? pcImage ?? textTpl;
                    textTpl ??= imageTpl;
                }

                var post = new PostModel
                {
                    Title = it.Idea.Trim(),
                    SocialChannelId = ch,
                    CategoryId = it.CategoryId ?? request.CategoryId,
                    GenerationFlow = request.GenerationFlow,
                    TextTemplateId = textTpl,
                    ImageTemplateId = imageTpl,
                    BatchId = batchId,
                    UserId = userId,
                    Status = PostStatus.Queued
                };
                if (!string.IsNullOrWhiteSpace(it.Objective))
                    post.ExtraJson = System.Text.Json.JsonSerializer.Serialize(
                        new { input = new { objective = it.Objective!.Trim() } });
                posts.Add(post);
            }

        await MultiCreateAsync(posts, ct);
        return new BulkCreateResult
        {
            BatchId = batchId,
            Created = posts.Count,
            PostIds = posts.Select(p => p.Id).ToList()
        };
    }

    /// <summary>
    /// Import CSV: 1 row = 1 post / 1 channel. Lịch dự kiến ghi ExtraJson.pendingSchedule
    /// (worker schedule sau khi Approved).
    /// </summary>
    public async Task<BulkCreateResult> BulkImportAsync(
        BulkImportRequest request,
        IReadOnlyDictionary<Guid, PageContextModel>? pageContextByChannel = null,
        CancellationToken ct = default)
    {
        var rows = (request.Rows ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Idea) && r.SocialChannelId != Guid.Empty)
            .ToList();
        if (rows.Count == 0)
            throw new ArgumentException("Danh sách import trống (cần idea + socialChannelId)");

        var timezone = string.IsNullOrWhiteSpace(request.Timezone)
            ? "Asia/Ho_Chi_Minh"
            : request.Timezone.Trim();
        var batchTemplate = request.PromptTemplateId is Guid p && p != Guid.Empty ? p : (Guid?)null;
        var pageMap = pageContextByChannel ?? new Dictionary<Guid, PageContextModel>();

        var batchId = Guid.NewGuid();
        var userId = GetCurrentUserId();
        var posts = new List<PostModel>();

        foreach (var row in rows)
        {
            Guid? textTpl;
            Guid? imageTpl;
            var itemPack = row.PromptTemplateId is Guid ip && ip != Guid.Empty ? ip : batchTemplate;
            if (itemPack is Guid pack)
            {
                textTpl = pack;
                imageTpl = pack;
            }
            else
            {
                pageMap.TryGetValue(row.SocialChannelId, out var pc);
                var (pcText, pcImage) = PageContextRepository.ResolveDefaultTemplateIds(pc);
                textTpl = pcText;
                imageTpl = pcImage ?? pcText;
                textTpl ??= imageTpl;
            }

            var post = new PostModel
            {
                Title = row.Idea.Trim(),
                SocialChannelId = row.SocialChannelId,
                CategoryId = row.CategoryId ?? request.CategoryId,
                GenerationFlow = request.GenerationFlow,
                TextTemplateId = textTpl,
                ImageTemplateId = imageTpl,
                BatchId = batchId,
                UserId = userId,
                Status = PostStatus.Queued,
                ExtraJson = BuildImportExtraJson(row.Objective, row.ScheduledAtLocal, timezone),
            };
            posts.Add(post);
        }

        await MultiCreateAsync(posts, ct);
        return new BulkCreateResult
        {
            BatchId = batchId,
            Created = posts.Count,
            PostIds = posts.Select(p => p.Id).ToList()
        };
    }

    private static string? BuildImportExtraJson(string? objective, string? scheduledAtLocal, string timezone)
    {
        var hasObjective = !string.IsNullOrWhiteSpace(objective);
        var hasSchedule = !string.IsNullOrWhiteSpace(scheduledAtLocal);
        if (!hasObjective && !hasSchedule) return null;

        var root = new Dictionary<string, object?>();
        if (hasObjective)
            root["input"] = new Dictionary<string, object?> { ["objective"] = objective!.Trim() };
        if (hasSchedule)
        {
            root["pendingSchedule"] = new Dictionary<string, object?>
            {
                ["atLocal"] = scheduledAtLocal!.Trim(),
                ["timezone"] = timezone,
            };
        }
        return System.Text.Json.JsonSerializer.Serialize(root);
    }

    /// <summary>
    /// Fan-out 1 ý tưởng × N kênh → Queued. Template: PromptTemplateId chung,
    /// hoặc default từ PageContext theo từng kênh.
    /// </summary>
    public async Task<BulkCreateResult> CreateFanOutQueuedAsync(
        string title,
        IReadOnlyList<Guid> channelIds,
        GenerationFlow generationFlow,
        Guid? promptTemplateId,
        IReadOnlyDictionary<Guid, PageContextModel> pageContextByChannel,
        string? objective,
        Guid? categoryId = null,
        SourceArticleBrief? sourceArticle = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Ý tưởng không được để trống");
        if (channelIds.Count == 0)
            throw new ArgumentException("Phải chọn ít nhất một kênh đăng");

        var batchId = Guid.NewGuid();
        var userId = GetCurrentUserId();
        var posts = new List<PostModel>();

        var pack = promptTemplateId is Guid p && p != Guid.Empty ? p : (Guid?)null;
        var allReady = channelIds.All(id =>
        {
            pageContextByChannel.TryGetValue(id, out var pc);
            return PageContextRepository.HasTemplateReady(pc);
        });

        var channelIndex = 0;
        foreach (var ch in channelIds.Distinct())
        {
            pageContextByChannel.TryGetValue(ch, out var pc);
            var ready = PageContextRepository.HasTemplateReady(pc);

            Guid? textTpl;
            Guid? imageTpl;
            // pack áp khi: page chưa ready, hoặc user ghi đè (mọi page đã ready + có chọn danh mục)
            if (pack.HasValue && (!ready || allReady))
            {
                textTpl = pack;
                imageTpl = pack;
            }
            else
            {
                (textTpl, imageTpl) = PageContextRepository.ResolveDefaultTemplateIds(pc);
            }

            var post = new PostModel
            {
                Title = title.Trim(),
                SocialChannelId = ch,
                GenerationFlow = generationFlow,
                CategoryId = categoryId,
                TextTemplateId = textTpl,
                ImageTemplateId = imageTpl,
                BatchId = batchId,
                UserId = userId,
                Status = PostStatus.Queued
            };
            // Mỗi kênh một góc nhìn khác nhau. Không giao góc thì AI viết tiêu đề 60 ký tự
            // gần như y hệt cho mọi page — đo thực tế chỉ khác đúng một cụm từ.
            post.ExtraJson = BuildFanOutExtraJson(
                objective,
                sourceArticle is null ? null
                    : sourceArticle with { Angle = HeadlineAngles.ForIndex(channelIndex, sourceArticle.Title) });
            channelIndex++;
            posts.Add(post);
        }

        await MultiCreateAsync(posts, ct);
        return new BulkCreateResult
        {
            BatchId = batchId,
            Created = posts.Count,
            PostIds = posts.Select(p => p.Id).ToList()
        };
    }

    /// <summary>
    /// Dựng ExtraJson lúc fan-out. Bài từ tin crawl mang thêm khối sourceArticle để pipeline
    /// sinh text đọc lại lúc dựng prompt — xem SourceArticleHelper.
    /// </summary>
    private static string? BuildFanOutExtraJson(string? objective, SourceArticleBrief? sourceArticle)
    {
        var root = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(objective))
            root["input"] = new Dictionary<string, object?> { ["objective"] = objective.Trim() };
        if (sourceArticle is not null)
            root[SourceArticleHelper.ExtraJsonKey] = SourceArticleHelper.ToJsonBlock(sourceArticle);
        return root.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(root);
    }

    /// <summary>
    /// Soft-delete all active posts. Admin: every post. Others: only posts owned by current user.
    /// </summary>
    public async Task<int> SoftDeleteAllAsync(bool deleteAllUsers, CancellationToken cancellationToken = default)
    {
        var query = QueryActive();
        if (!deleteAllUsers)
        {
            var userId = GetCurrentUserId();
            if (userId == Guid.Empty)
                return 0;
            query = query.Where(x => x.UserId == userId);
        }

        // Bỏ qua bài đang có lịch — xoá sẽ để lại bài mồ côi trên Facebook (xem PostController.SoftDelete).
        query = query.Where(x => x.Status != PostStatus.Scheduled);

        var posts = await query.ToListAsync(cancellationToken);
        if (posts.Count == 0)
            return 0;

        var actor = GetCurrentUserName();
        var now = DateTime.UtcNow;
        foreach (var post in posts)
        {
            post.IsDeleted = true;
            post.DeletedAt = now;
            post.DeletedBy = actor;
        }

        await Context.SaveChangesAsync(cancellationToken);
        return posts.Count;
    }

    public async Task<PostModel?> UpdateAsync(
        Guid id,
        UpdatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return null;

        // Chỉ cho sửa nội dung khi chưa publish — không cho đổi status qua update thường
        if (entity.Status is PostStatus.Publishing or PostStatus.Published)
            throw new ArgumentException("Không thể sửa bài viết đang/đã đăng");

        if (request.Title is not null)
            entity.Title = request.Title.Trim();

        if (request.Content is not null)
            entity.Content = request.Content.Trim();

        if (request.CategoryId.HasValue)
            entity.CategoryId = request.CategoryId;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>Lấy các post theo batchId hoặc danh sách id, lọc theo status cho phép.</summary>
    public async Task<List<PostModel>> ResolveTargetsAsync(
        Guid? batchId, List<Guid>? postIds, PostStatus[]? allowedStatuses, CancellationToken ct = default)
    {
        var q = QueryActive();
        if (batchId.HasValue) q = q.Where(p => p.BatchId == batchId.Value);
        else if (postIds is { Count: > 0 }) q = q.Where(p => postIds.Contains(p.Id));
        else return [];

        if (allowedStatuses is { Length: > 0 })
            q = q.Where(p => allowedStatuses.Contains(p.Status));

        return await q.OrderBy(p => p.CreatedAt).ToListAsync(ct);
    }

    public static PostResponse ToResponse(PostModel entity, string? promptTemplateName = null)
    {
        var promptTemplateId = entity.TextTemplateId ?? entity.ImageTemplateId;
        return new()
        {
            Id = entity.Id,
            Title = entity.Title,
            Content = entity.Content,
            CategoryId = entity.CategoryId,
            SocialChannelId = entity.SocialChannelId,
            GenerationFlow = entity.GenerationFlow,
            TextTemplateId = entity.TextTemplateId,
            ImageTemplateId = entity.ImageTemplateId,
            PromptTemplateId = promptTemplateId,
            PromptTemplateName = promptTemplateName,
            Status = entity.Status,
            UserId = entity.UserId,
            BatchId = entity.BatchId,
            SourcePostId = entity.SourcePostId,
            ScheduledPublishAt = entity.ScheduledPublishAt,
            ScheduleTimezone = entity.ScheduleTimezone,
            PublishedAt = entity.PublishedAt,
            ExternalPostId = entity.ExternalPostId,
            PublishedUrl = entity.PublishedUrl,
            RejectionReason = entity.RejectionReason,
            ApprovedBy = entity.ApprovedBy,
            ApprovedAt = entity.ApprovedAt,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private async Task<Dictionary<Guid, string>> LoadTemplateNamesAsync(
        IEnumerable<PostModel> posts, CancellationToken ct)
    {
        var ids = posts
            .Select(p => p.TextTemplateId ?? p.ImageTemplateId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0) return [];

        return await Context.Set<PromptTemplateModel>()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
    }

    private static string? ResolveTemplateName(PostModel post, IReadOnlyDictionary<Guid, string> names)
    {
        var id = post.TextTemplateId ?? post.ImageTemplateId;
        if (id is Guid tid && names.TryGetValue(tid, out var name))
            return name;
        return null;
    }
}
