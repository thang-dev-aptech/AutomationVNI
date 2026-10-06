using Backend.Data;
using Backend.Modules.Category;
using Backend.Modules.ChannelGroup;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.MediaFolder;
using Backend.Modules.PageContext;
using Backend.Modules.Post.Enums;
using Backend.Modules.PromptTemplate;
using Backend.Modules.SocialChannel;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Post;

public class PostRepository : GenericRepository<PostModel>, IGenericRepository<PostModel>
{
    private readonly MediaFolderRepository _mediaFolders;
    private readonly ChannelGroupRepository _channelGroups;

    public PostRepository(AppDbContext context, IUserContext userContext)
        : this(
            context,
            userContext,
            new MediaFolderRepository(context, userContext),
            new ChannelGroupRepository(context, userContext)) { }

    public PostRepository(
        AppDbContext context,
        IUserContext userContext,
        MediaFolderRepository mediaFolders)
        : this(context, userContext, mediaFolders, new ChannelGroupRepository(context, userContext)) { }

    public PostRepository(
        AppDbContext context,
        IUserContext userContext,
        MediaFolderRepository mediaFolders,
        ChannelGroupRepository channelGroups)
        : base(context, userContext)
    {
        _mediaFolders = mediaFolders;
        _channelGroups = channelGroups;
    }

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

    /// <summary>
    /// Lấy bài để dựng lưới lịch trong một khoảng thời gian (không phân trang, tối đa 1000).
    /// Dùng cùng bộ lọc với <see cref="GetCalendarListAsync"/> / <see cref="GetCalendarFacetsAsync"/>.
    /// </summary>
    public async Task<List<PostResponse>> GetCalendarAsync(
        PostCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        const int MaxCalendarItems = 1000;
        var ordered = await LoadFilteredOrderedAsync(request, cancellationToken);
        var page = ordered.Count <= MaxCalendarItems
            ? ordered
            : ordered.Take(MaxCalendarItems).ToList();
        return await MapCalendarResponsesAsync(page, cancellationToken);
    }

    /// <summary>Danh sách phân trang theo cùng bộ lọc lịch; sắp theo giờ đăng.</summary>
    public async Task<PagedResult<PostResponse>> GetCalendarListAsync(
        PostCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        var index = request.Index < 1 ? 1 : request.Index;
        var size = request.Size < 1 ? 20 : Math.Min(request.Size, 200);

        var ordered = await LoadFilteredOrderedAsync(request, cancellationToken);
        var total = ordered.Count;
        var page = ordered.Skip((index - 1) * size).Take(size).ToList();
        var items = await MapCalendarResponsesAsync(page, cancellationToken);
        return new PagedResult<PostResponse>
        {
            Items = items,
            Total = total,
            Index = index,
            Size = size
        };
    }

    /// <summary>
    /// Facets tác giả + chủ đề có bài trong khoảng (cùng bộ lọc, bỏ Authors/CategoryIds để sidebar
    /// vẫn liệt kê được các giá trị còn lại).
    /// </summary>
    public async Task<PostCalendarFacetsResponse> GetCalendarFacetsAsync(
        PostCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        var facetRequest = CloneCalendarRequest(request);
        facetRequest.Authors = null;
        facetRequest.CategoryIds = null;

        var posts = await LoadFilteredOrderedAsync(facetRequest, cancellationToken);

        var authorGroups = posts
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.UserId)
            .ToList();

        var categoryGroups = posts
            .Where(p => p.CategoryId.HasValue)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.CategoryId)
            .ToList();

        var userIds = authorGroups.Select(a => a.UserId).ToList();
        var userNames = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await Context.Set<ApplicationUser>()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => u.UserName ?? u.Email ?? u.Id.ToString(),
                    cancellationToken);

        var catIds = categoryGroups.Select(c => c.CategoryId).ToList();
        var catNames = catIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await Context.Set<CategoryModel>()
                .Where(c => catIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return new PostCalendarFacetsResponse
        {
            Authors = authorGroups.Select(a => new PostCalendarAuthorFacet
            {
                UserId = a.UserId,
                Name = userNames.TryGetValue(a.UserId, out var n) ? n : a.UserId.ToString(),
                Count = a.Count
            }).ToList(),
            Categories = categoryGroups.Select(c => new PostCalendarCategoryFacet
            {
                CategoryId = c.CategoryId,
                Name = catNames.TryGetValue(c.CategoryId, out var n) ? n : c.CategoryId.ToString(),
                Count = c.Count
            }).ToList()
        };
    }

    /// <summary>
    /// Dựng query lọc dùng chung: thời gian nửa khoảng mở + Statuses + kênh/nhóm + Category +
    /// Authors + Keyword. PostTypes lọc sau khi nạp media (Classify).
    /// </summary>
    private async Task<IQueryable<PostModel>> BuildCalendarFilterQueryAsync(
        PostCalendarRequest request,
        CancellationToken cancellationToken)
    {
        var from = request.FromUtc;
        var to = request.ToUtc;

        var query = QueryActive().Where(x =>
            (x.ScheduledPublishAt != null
                && x.ScheduledPublishAt >= from && x.ScheduledPublishAt < to)
            || (x.PublishedAt != null
                && x.PublishedAt >= from && x.PublishedAt < to));

        if (request.Statuses is { Count: > 0 })
            query = query.Where(x => request.Statuses.Contains(x.Status));

        var channelIds = new HashSet<Guid>();
        var hasChannelIds = request.SocialChannelIds is { Count: > 0 };
        var hasGroupIds = request.ChannelGroupIds is { Count: > 0 };

        if (hasChannelIds)
        {
            foreach (var id in request.SocialChannelIds!)
                channelIds.Add(id);
        }

        if (hasGroupIds)
        {
            var fromGroups = await _channelGroups.ResolveChannelIdsAsync(
                request.ChannelGroupIds, cancellationToken);
            foreach (var id in fromGroups)
                channelIds.Add(id);

            // Chỉ nhóm (không kênh lẻ) mà nhóm đã xoá / không kênh → không có bài.
            if (!hasChannelIds && fromGroups.Count == 0)
                return query.Where(_ => false);
        }

        if (channelIds.Count > 0)
            query = query.Where(x => channelIds.Contains(x.SocialChannelId));

        if (request.CategoryIds is { Count: > 0 })
            query = query.Where(x =>
                x.CategoryId != null && request.CategoryIds.Contains(x.CategoryId.Value));

        if (request.Authors is { Count: > 0 })
            query = query.Where(x => request.Authors.Contains(x.UserId));

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(x =>
                x.Title.ToLower().Contains(keyword)
                || (x.Content != null && x.Content.ToLower().Contains(keyword)));
        }

        return query;
    }

    private async Task<List<PostModel>> LoadFilteredOrderedAsync(
        PostCalendarRequest request,
        CancellationToken cancellationToken)
    {
        var query = await BuildCalendarFilterQueryAsync(request, cancellationToken);
        var items = await query
            .OrderBy(x => x.ScheduledPublishAt ?? x.PublishedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (request.PostTypes is not { Count: > 0 })
            return items;

        var wanted = request.PostTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            return items;

        var classified = await ClassifyPostsAsync(items, cancellationToken);
        return items.Where(p =>
        {
            if (!classified.TryGetValue(p.Id, out var type)) return false;
            return wanted.Contains(type);
        }).ToList();
    }

    private async Task<Dictionary<Guid, string>> ClassifyPostsAsync(
        List<PostModel> posts,
        CancellationToken cancellationToken)
    {
        if (posts.Count == 0) return [];

        var postIds = posts.Select(p => p.Id).ToList();
        var channelIds = posts.Select(p => p.SocialChannelId).Distinct().ToList();

        var platforms = await Context.Set<SocialChannelModel>()
            .Where(c => channelIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Platform, cancellationToken);

        var mediaByPost = await LoadMediaSignalsByPostAsync(postIds, cancellationToken);

        var result = new Dictionary<Guid, string>(posts.Count);
        foreach (var post in posts)
        {
            platforms.TryGetValue(post.SocialChannelId, out var platform);
            var signals = mediaByPost.GetValueOrDefault(post.Id) ?? [];
            result[post.Id] = PostTypeClassifier.Classify(
                post.NewsArticleId, platform, post.ExtraJson, signals);
        }

        return result;
    }

    private async Task<Dictionary<Guid, List<PostTypeClassifier.MediaSignal>>> LoadMediaSignalsByPostAsync(
        List<Guid> postIds,
        CancellationToken cancellationToken)
    {
        if (postIds.Count == 0) return [];

        var links = await Context.Set<PostMediaModel>()
            .Where(pm => !pm.IsDeleted && postIds.Contains(pm.PostId))
            .OrderBy(pm => pm.SortOrder)
            .ThenBy(pm => pm.Id)
            .Select(pm => new { pm.PostId, pm.MediaId, pm.MediaRole, pm.SortOrder })
            .ToListAsync(cancellationToken);

        var mediaIds = links.Select(l => l.MediaId).Distinct().ToList();
        var assets = mediaIds.Count == 0
            ? new Dictionary<Guid, MediaAssetModel>()
            : await Context.Set<MediaAssetModel>()
                .Where(a => !a.IsDeleted && mediaIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, cancellationToken);

        var map = new Dictionary<Guid, List<PostTypeClassifier.MediaSignal>>();
        foreach (var link in links)
        {
            if (!assets.TryGetValue(link.MediaId, out var asset)) continue;
            if (!map.TryGetValue(link.PostId, out var list))
            {
                list = [];
                map[link.PostId] = list;
            }

            list.Add(new PostTypeClassifier.MediaSignal(
                asset.MimeType, asset.Source, asset.OriginalFileName, asset.AltText));
        }

        return map;
    }

    private async Task<List<PostResponse>> MapCalendarResponsesAsync(
        List<PostModel> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0) return [];

        var names = await LoadTemplateNamesAsync(items, cancellationToken);
        var postIds = items.Select(i => i.Id).ToList();
        var channelIds = items.Select(i => i.SocialChannelId).Distinct().ToList();
        var categoryIds = items
            .Where(i => i.CategoryId.HasValue)
            .Select(i => i.CategoryId!.Value)
            .Distinct()
            .ToList();
        var userIds = items.Select(i => i.UserId).Distinct().ToList();

        var channels = await Context.Set<SocialChannelModel>()
            .Where(c => channelIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var categories = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await Context.Set<CategoryModel>()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var users = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await Context.Set<ApplicationUser>()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => u.UserName ?? u.Email ?? u.Id.ToString(),
                    cancellationToken);

        var links = await Context.Set<PostMediaModel>()
            .Where(pm => !pm.IsDeleted && postIds.Contains(pm.PostId))
            .OrderBy(pm => pm.SortOrder)
            .ThenBy(pm => pm.Id)
            .ToListAsync(cancellationToken);

        var mediaIds = links.Select(l => l.MediaId).Distinct().ToList();
        var assets = mediaIds.Count == 0
            ? new Dictionary<Guid, MediaAssetModel>()
            : await Context.Set<MediaAssetModel>()
                .Where(a => !a.IsDeleted && mediaIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, cancellationToken);

        var linksByPost = links.GroupBy(l => l.PostId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<PostResponse>(items.Count);
        foreach (var entity in items)
        {
            var response = ToResponse(entity, ResolveTemplateName(entity, names));

            if (channels.TryGetValue(entity.SocialChannelId, out var channel))
            {
                response.ChannelName = channel.PageName;
                response.Platform = channel.Platform;
            }

            if (entity.CategoryId is Guid catId
                && categories.TryGetValue(catId, out var catName))
                response.CategoryName = catName;

            if (users.TryGetValue(entity.UserId, out var author))
                response.AuthorName = author;

            var postLinks = linksByPost.GetValueOrDefault(entity.Id) ?? [];
            var signals = new List<PostTypeClassifier.MediaSignal>();
            string? thumb = null;
            string? coverImage = null;
            string? firstImage = null;

            foreach (var link in postLinks)
            {
                if (!assets.TryGetValue(link.MediaId, out var asset)) continue;
                signals.Add(new PostTypeClassifier.MediaSignal(
                    asset.MimeType, asset.Source, asset.OriginalFileName, asset.AltText));

                if (!PostTypeClassifier.IsImage(asset.MimeType)
                    || string.IsNullOrWhiteSpace(asset.PublicUrl))
                    continue;

                firstImage ??= asset.PublicUrl;
                if (link.MediaRole is MediaRole.Cover or MediaRole.Primary)
                    coverImage ??= asset.PublicUrl;
            }

            thumb = coverImage ?? firstImage;
            response.MediaCount = signals.Count;
            response.ThumbnailUrl = thumb;
            response.PostType = PostTypeClassifier.Classify(
                entity.NewsArticleId, response.Platform, entity.ExtraJson, signals);
            result.Add(response);
        }

        return result;
    }

    private static PostCalendarRequest CloneCalendarRequest(PostCalendarRequest source) => new()
    {
        FromUtc = source.FromUtc,
        ToUtc = source.ToUtc,
        Statuses = source.Statuses,
        SocialChannelIds = source.SocialChannelIds,
        ChannelGroupIds = source.ChannelGroupIds,
        CategoryIds = source.CategoryIds,
        Authors = source.Authors,
        PostTypes = source.PostTypes,
        Keyword = source.Keyword,
        Index = source.Index,
        Size = source.Size
    };

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
            ImageCount = request.ImageCount,
            UserId = GetCurrentUserId(),
            Status = PostStatus.Draft
        };

        if (!string.IsNullOrWhiteSpace(request.Objective))
            entity.ExtraJson = System.Text.Json.JsonSerializer.Serialize(
                new { input = new { objective = request.Objective.Trim() } });

        return await base.CreateAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Draft từ ảnh Media người dùng chọn. Không gắn template và không đưa vào hàng đợi AI.
    /// </summary>
    public async Task<PostModel> CreateUserSelectedDraftAsync(
        string title,
        string content,
        Guid socialChannelId,
        Guid? categoryId,
        Guid? batchId,
        CancellationToken ct = default)
    {
        var entity = new PostModel
        {
            Title = title,
            Content = content,
            SocialChannelId = socialChannelId,
            CategoryId = categoryId,
            GenerationFlow = GenerationFlow.UserSelectedMedia,
            BatchId = batchId,
            UserId = GetCurrentUserId(),
            Status = PostStatus.Draft,
        };
        return await base.CreateAsync(entity, ct);
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
                    ImageCount = request.ImageCount,
                    BatchId = batchId,
                    UserId = userId,
                    Status = PostStatus.Queued,
                    ExtraJson = BuildBulkCreateExtraJson(it.Objective, request.GenerateAsReels)
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

    /// <summary>
    /// Fan-out ý tưởng × kênh thành bài Queued với GenerationFlow.ChungChiGallery.
    /// Không gắn text/image template — pipeline lấy ảnh nguyên trạng từ thư mục chung_chi của Page.
    /// </summary>
    public async Task<BulkCreateResult> BulkCreateChungChiAsync(
        BulkCreateChungChiRequest request,
        CancellationToken ct = default)
    {
        var items = (request.Items ?? []).Where(i => !string.IsNullOrWhiteSpace(i.Idea)).ToList();
        var channels = (request.ChannelIds ?? []).Where(c => c != Guid.Empty).Distinct().ToList();
        if (items.Count == 0) throw new ArgumentException("Danh sách ý tưởng trống");
        if (channels.Count == 0) throw new ArgumentException("Phải chọn ít nhất một kênh đăng");

        await _mediaFolders.EnsureChungChiPagesEligibleAsync(channels, ct);

        var mode = Enum.IsDefined(request.Mode) ? request.Mode : ChungChiSelectionMode.Random;
        var randomCount = request.RandomCount is int n && n >= 1 ? n : 1;

        var batchId = Guid.NewGuid();
        var userId = GetCurrentUserId();
        var extraJson = BuildChungChiExtraJson(mode);
        var posts = new List<PostModel>();
        foreach (var ch in channels)
            foreach (var it in items)
            {
                posts.Add(new PostModel
                {
                    Title = it.Idea.Trim(),
                    Content = it.Idea.Trim(),
                    SocialChannelId = ch,
                    GenerationFlow = GenerationFlow.ChungChiGallery,
                    ImageCount = mode == ChungChiSelectionMode.Random ? randomCount : null,
                    BatchId = batchId,
                    UserId = userId,
                    Status = PostStatus.Queued,
                    ExtraJson = extraJson
                });
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
                ImageCount = request.ImageCount,
                BatchId = batchId,
                UserId = userId,
                Status = PostStatus.Queued,
                ExtraJson = BuildImportExtraJson(row.Objective, row.ScheduledAtLocal, timezone, request.GenerateAsReels),
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

    private static string? BuildImportExtraJson(
        string? objective, string? scheduledAtLocal, string timezone, bool reelsRequested = false)
    {
        var hasObjective = !string.IsNullOrWhiteSpace(objective);
        var hasSchedule = !string.IsNullOrWhiteSpace(scheduledAtLocal);
        if (!hasObjective && !hasSchedule && !reelsRequested) return null;

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
        if (reelsRequested)
            root["reelsRequested"] = true;
        return System.Text.Json.JsonSerializer.Serialize(root);
    }

    /// <summary>ExtraJson lúc bulk-create (ideas × channels) — objective (nếu có) + cờ reelsRequested
    /// (PostGenerationWorker đọc lại sau khi sinh ảnh xong để tự convert sang video).</summary>
    private static string? BuildBulkCreateExtraJson(string? objective, bool reelsRequested)
    {
        var hasObjective = !string.IsNullOrWhiteSpace(objective);
        if (!hasObjective && !reelsRequested) return null;

        var root = new Dictionary<string, object?>();
        if (hasObjective)
            root["input"] = new Dictionary<string, object?> { ["objective"] = objective!.Trim() };
        if (reelsRequested)
            root["reelsRequested"] = true;
        return System.Text.Json.JsonSerializer.Serialize(root);
    }

    private static string BuildChungChiExtraJson(ChungChiSelectionMode mode)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["chungChi"] = new Dictionary<string, object?> { ["mode"] = (int)mode }
        });

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
        int? imageCount = null,
        bool generateAsReels = false,
        Guid? newsArticleId = null,
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
                ImageCount = imageCount,
                BatchId = batchId,
                NewsArticleId = newsArticleId,
                UserId = userId,
                Status = PostStatus.Queued
            };
            // Mỗi kênh một góc nhìn khác nhau. Không giao góc thì AI viết tiêu đề 60 ký tự
            // gần như y hệt cho mọi page — đo thực tế chỉ khác đúng một cụm từ.
            post.ExtraJson = BuildFanOutExtraJson(
                objective,
                sourceArticle is null ? null
                    : sourceArticle with { Angle = HeadlineAngles.ForIndex(channelIndex, sourceArticle.Title) },
                generateAsReels);
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
    private static string? BuildFanOutExtraJson(
        string? objective, SourceArticleBrief? sourceArticle, bool reelsRequested = false)
    {
        var root = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(objective))
            root["input"] = new Dictionary<string, object?> { ["objective"] = objective.Trim() };
        if (sourceArticle is not null)
            root[SourceArticleHelper.ExtraJsonKey] = SourceArticleHelper.ToJsonBlock(sourceArticle);
        if (reelsRequested)
            root["reelsRequested"] = true;
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
        // Bỏ qua cả bài đã đăng (Published) — bài thật vẫn còn sống trên Facebook/TikTok/..., xoá bản ghi
        // hệ thống chỉ làm mất lịch sử/số liệu mà không có cách khôi phục qua UI.
        query = query.Where(x => x.Status != PostStatus.Scheduled && x.Status != PostStatus.Published);

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
