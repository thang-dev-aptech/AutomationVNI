using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.Post;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment.Enums;
using Backend.Shared;
using Backend.Shared.Meta;
using Backend.Shared.Repositories;
using Backend.Shared.SocialComment;
using Backend.Shared.SocialPublish;
using Backend.Shared.Threads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.SocialComment;

public class SocialCommentService(
    AppDbContext db,
    IEnumerable<ISocialCommentProvider> providers,
    IUserContext userContext,
    IOptions<MetaOAuthOptions> metaOptions,
    IOptions<ThreadsOAuthOptions> threadsOptions,
    IOptions<SocialPublishOptions> publishOptions,
    ILogger<SocialCommentService> logger)
{
    private ISocialCommentProvider ResolveProvider(SocialPlatform platform)
        => providers.FirstOrDefault(p => p.Platform == platform)
           ?? throw new InvalidOperationException($"No comment provider for {platform}");

    public SocialCommentCapabilities GetCapabilities(SocialPlatform platform)
        => ResolveProvider(platform).Capabilities;

    public async Task<PagedResult<SocialCommentResponse>> FilterInboxAsync(
        SocialCommentFilterRequest request, CancellationToken ct = default)
    {
        var query = db.SocialComments.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsDeletedOnPlatform && !x.IsFromPage);

        if (request.Platform.HasValue)
            query = query.Where(x => x.Platform == request.Platform.Value);
        if (request.SocialChannelId.HasValue)
            query = query.Where(x => x.SocialChannelId == request.SocialChannelId.Value);
        if (request.SocialPostId.HasValue)
            query = query.Where(x => x.SocialPostId == request.SocialPostId.Value);
        if (request.InboxStatus.HasValue)
            query = query.Where(x => x.InboxStatus == request.InboxStatus.Value);
        if (request.UnrepliedOnly == true)
            query = query.Where(x => x.InboxStatus == CommentInboxStatus.New
                                     || x.InboxStatus == CommentInboxStatus.InProgress);
        if (request.IsHidden.HasValue)
            query = query.Where(x => x.IsHidden == request.IsHidden.Value);
        if (request.IsPending.HasValue)
            query = query.Where(x => x.IsPending == request.IsPending.Value);
        if (request.From.HasValue)
            query = query.Where(x => (x.CommentedAt ?? x.CreatedAt) >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(x => (x.CommentedAt ?? x.CreatedAt) <= request.To.Value);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x =>
                (x.Message != null && x.Message.Contains(kw))
                || (x.AuthorName != null && x.AuthorName.Contains(kw))
                || (x.AuthorUsername != null && x.AuthorUsername.Contains(kw)));
        }

        // Inbox list = top-level only; replies load in detail.
        query = query.Where(x => x.ParentCommentId == null);

        var total = await query.CountAsync(ct);
        var index = Math.Max(1, request.Index);
        var size = Math.Clamp(request.Size, 1, 100);
        var items = await query
            .OrderByDescending(x => x.CommentedAt ?? x.CreatedAt)
            .Skip((index - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var channelIds = items.Select(x => x.SocialChannelId).Distinct().ToList();
        var postIds = items.Select(x => x.SocialPostId).Distinct().ToList();
        var channels = await db.SocialChannels.AsNoTracking()
            .Where(x => channelIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var posts = await db.SocialPosts.AsNoTracking()
            .Where(x => postIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        return new PagedResult<SocialCommentResponse>
        {
            Items = items.Select(c => ToResponse(
                c,
                channels.GetValueOrDefault(c.SocialChannelId)?.PageName,
                posts.GetValueOrDefault(c.SocialPostId))).ToList(),
            Total = total,
            Index = index,
            Size = size
        };
    }

    public async Task<InboxSummaryResponse> GetSummaryAsync(CancellationToken ct = default)
    {
        var baseQuery = db.SocialComments.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsDeletedOnPlatform && !x.IsFromPage && x.ParentCommentId == null);

        return new InboxSummaryResponse
        {
            Total = await baseQuery.CountAsync(ct),
            NewCount = await baseQuery.CountAsync(x => x.InboxStatus == CommentInboxStatus.New, ct),
            InProgress = await baseQuery.CountAsync(x => x.InboxStatus == CommentInboxStatus.InProgress, ct),
            Unreplied = await baseQuery.CountAsync(
                x => x.InboxStatus == CommentInboxStatus.New || x.InboxStatus == CommentInboxStatus.InProgress, ct),
            Hidden = await baseQuery.CountAsync(x => x.IsHidden, ct),
            Pending = await baseQuery.CountAsync(x => x.IsPending, ct)
        };
    }

    public async Task<SocialCommentResponse?> GetThreadAsync(Guid id, CancellationToken ct = default)
    {
        var root = await db.SocialComments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (root is null) return null;

        // Walk up to top-level
        while (root.ParentCommentId.HasValue)
        {
            var parent = await db.SocialComments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == root.ParentCommentId.Value && !x.IsDeleted, ct);
            if (parent is null) break;
            root = parent;
        }

        var all = await db.SocialComments.AsNoTracking()
            .Where(x => x.SocialPostId == root.SocialPostId && !x.IsDeleted)
            .OrderBy(x => x.CommentedAt ?? x.CreatedAt)
            .ToListAsync(ct);

        var channel = await db.SocialChannels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == root.SocialChannelId, ct);
        var post = await db.SocialPosts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == root.SocialPostId, ct);

        var byParent = all
            .Where(x => x.ParentCommentId.HasValue)
            .GroupBy(x => x.ParentCommentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        SocialCommentResponse Build(SocialCommentModel c)
        {
            var dto = ToResponse(c, channel?.PageName, post);
            if (byParent.TryGetValue(c.Id, out var kids))
                dto.Replies = kids.Select(Build).ToList();
            return dto;
        }

        return Build(root);
    }

    public async Task<List<CommentActionLogResponse>> GetActionLogsAsync(Guid commentId, CancellationToken ct = default)
        => await db.CommentActionLogs.AsNoTracking()
            .Where(x => x.SocialCommentId == commentId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .Select(x => new CommentActionLogResponse
            {
                Id = x.Id,
                ActionType = x.ActionType,
                ActorUserName = x.ActorUserName,
                Success = x.Success,
                ErrorMessage = x.ErrorMessage,
                ExternalResultId = x.ExternalResultId,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(ct);

    public async Task<SyncCommentsResult> SyncAsync(SyncCommentsRequest request, CancellationToken ct = default)
    {
        var result = new SyncCommentsResult();
        var channelsQuery = db.SocialChannels.Where(x => !x.IsDeleted && x.IsActive
            && (x.Platform == SocialPlatform.Facebook || x.Platform == SocialPlatform.Threads));
        if (request.SocialChannelId.HasValue)
            channelsQuery = channelsQuery.Where(x => x.Id == request.SocialChannelId.Value);

        var channels = await channelsQuery.ToListAsync(ct);
        var maxPosts = request.Mode == SocialCommentSyncMode.Full ? 200 : 40;
        var maxCommentPages = request.Mode == SocialCommentSyncMode.Full ? 10 : 3;

        foreach (var channel in channels)
        {
            try
            {
                var (posts, comments) = await SyncChannelAsync(channel, maxPosts, maxCommentPages, ct);
                result.ChannelsProcessed++;
                result.PostsUpserted += posts;
                result.CommentsUpserted += comments;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sync failed for channel {ChannelId}", channel.Id);
                result.Errors.Add($"{channel.PageName}: {ex.Message}");
            }
        }

        return result;
    }

    public async Task<(int Posts, int Comments)> SyncChannelAsync(
        SocialChannelModel channel, int maxPosts, int maxCommentPages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channel.AccessToken) || string.IsNullOrWhiteSpace(channel.ExternalPageId))
            throw new InvalidOperationException("Channel thiếu token hoặc ExternalPageId");

        var provider = ResolveProvider(channel.Platform);
        var postsUpserted = 0;
        var commentsUpserted = 0;
        string? cursor = null;
        var fetched = 0;

        while (fetched < maxPosts)
        {
            var pageSize = Math.Min(25, maxPosts - fetched);
            var (posts, next) = await provider.ListPostsAsync(
                channel.ExternalPageId, channel.AccessToken, cursor, pageSize, ct);
            if (posts.Count == 0) break;

            foreach (var p in posts)
            {
                var socialPost = await UpsertPostAsync(channel, p, ct);
                postsUpserted++;
                commentsUpserted += await SyncPostCommentsAsync(channel, socialPost, provider, maxCommentPages, ct);
            }

            fetched += posts.Count;
            cursor = next;
            if (string.IsNullOrWhiteSpace(cursor)) break;
        }

        return (postsUpserted, commentsUpserted);
    }

    private async Task<int> SyncPostCommentsAsync(
        SocialChannelModel channel,
        SocialPostModel socialPost,
        ISocialCommentProvider provider,
        int maxPages,
        CancellationToken ct)
    {
        var upserted = 0;
        string? cursor = null;
        for (var page = 0; page < maxPages; page++)
        {
            var (comments, next) = await provider.ListCommentsAsync(
                socialPost.ExternalPostId, channel.AccessToken, cursor, 50, ct);
            if (comments.Count == 0) break;

            foreach (var c in comments)
            {
                await UpsertCommentAsync(channel, socialPost, c, ct);
                upserted++;
            }

            cursor = next;
            if (string.IsNullOrWhiteSpace(cursor)) break;
        }

        socialPost.CommentCount = await db.SocialComments
            .CountAsync(x => x.SocialPostId == socialPost.Id && !x.IsDeleted && !x.IsDeletedOnPlatform && !x.IsFromPage, ct);
        socialPost.LastCommentAt = await db.SocialComments
            .Where(x => x.SocialPostId == socialPost.Id && !x.IsDeleted)
            .Select(x => (DateTime?)(x.CommentedAt ?? x.CreatedAt))
            .OrderByDescending(x => x)
            .FirstOrDefaultAsync(ct);
        socialPost.LastSyncedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return upserted;
    }

    public async Task<SocialPostModel> UpsertPostAsync(
        SocialChannelModel channel, ProviderPostDto dto, CancellationToken ct)
    {
        var entity = await db.SocialPosts
            .FirstOrDefaultAsync(x => x.SocialChannelId == channel.Id
                                      && x.ExternalPostId == dto.ExternalPostId
                                      && !x.IsDeleted, ct);
        if (entity is null)
        {
            entity = new SocialPostModel
            {
                Id = Guid.NewGuid(),
                SocialChannelId = channel.Id,
                Platform = channel.Platform,
                ExternalPostId = dto.ExternalPostId,
                CreatedAt = DateTime.UtcNow
            };
            db.SocialPosts.Add(entity);
        }

        entity.Message = dto.Message;
        entity.PermalinkUrl = dto.PermalinkUrl;
        entity.PostedAt = dto.PostedAt;
        entity.LastSyncedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        if (!entity.LocalPostId.HasValue)
        {
            var local = await db.Posts.AsNoTracking()
                .Where(x => !x.IsDeleted && x.ExternalPostId == dto.ExternalPostId)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(ct);
            entity.LocalPostId = local;
        }

        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<SocialCommentModel> UpsertCommentAsync(
        SocialChannelModel channel,
        SocialPostModel socialPost,
        ProviderCommentDto dto,
        CancellationToken ct)
    {
        var entity = await db.SocialComments
            .FirstOrDefaultAsync(x => x.SocialChannelId == channel.Id
                                      && x.ExternalCommentId == dto.ExternalCommentId
                                      && !x.IsDeleted, ct);
        var isNew = entity is null;
        if (entity is null)
        {
            entity = new SocialCommentModel
            {
                Id = Guid.NewGuid(),
                SocialChannelId = channel.Id,
                SocialPostId = socialPost.Id,
                Platform = channel.Platform,
                ExternalCommentId = dto.ExternalCommentId,
                InboxStatus = CommentInboxStatus.New,
                CreatedAt = DateTime.UtcNow
            };
            db.SocialComments.Add(entity);
        }

        entity.Message = dto.Message;
        entity.AuthorExternalId = dto.AuthorExternalId;
        entity.AuthorName = dto.AuthorName;
        entity.AuthorUsername = dto.AuthorUsername;
        entity.PermalinkUrl = dto.PermalinkUrl;
        entity.CommentedAt = dto.CommentedAt;
        entity.IsHidden = dto.IsHidden;
        entity.IsFromPage = dto.IsFromPage;
        entity.IsPending = dto.IsPending;
        entity.LikeCount = dto.LikeCount;
        entity.ReplyCount = dto.ReplyCount;
        entity.ParentExternalCommentId = dto.ParentExternalCommentId;
        entity.IsDeletedOnPlatform = false;
        entity.LastSyncedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(dto.ParentExternalCommentId))
        {
            var parent = await db.SocialComments
                .FirstOrDefaultAsync(x => x.SocialChannelId == channel.Id
                                          && x.ExternalCommentId == dto.ParentExternalCommentId
                                          && !x.IsDeleted, ct);
            entity.ParentCommentId = parent?.Id;
        }
        else
        {
            entity.ParentCommentId = null;
        }

        // Reply từ page → đánh dấu parent đã trả lời
        if (dto.IsFromPage && entity.ParentCommentId.HasValue)
        {
            var parent = await db.SocialComments
                .FirstOrDefaultAsync(x => x.Id == entity.ParentCommentId.Value, ct);
            if (parent is not null && parent.InboxStatus is CommentInboxStatus.New or CommentInboxStatus.InProgress)
            {
                parent.InboxStatus = CommentInboxStatus.Replied;
                parent.RepliedAt ??= DateTime.UtcNow;
            }
        }

        if (isNew && dto.IsFromPage)
            entity.InboxStatus = CommentInboxStatus.Replied;

        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<SocialCommentResponse> ReplyAsync(Guid id, ReplyCommentRequest request, CancellationToken ct)
    {
        var comment = await GetCommentOrThrow(id, ct);
        var channel = await GetChannelOrThrow(comment.SocialChannelId, ct);
        var provider = ResolveProvider(comment.Platform);
        if (!provider.Capabilities.CanReply)
            throw new InvalidOperationException("Nền tảng này không hỗ trợ trả lời comment");

        var result = await provider.ReplyAsync(
            comment.ExternalCommentId,
            channel.AccessToken,
            request.Message,
            channel.ExternalPageId,
            ct);

        await LogActionAsync(comment.Id, CommentActionType.Reply, result, request.Message, ct);

        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "Trả lời thất bại");

        comment.InboxStatus = CommentInboxStatus.Replied;
        comment.RepliedAt = DateTime.UtcNow;
        comment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // Refresh replies for this post
        var post = await db.SocialPosts.FirstAsync(x => x.Id == comment.SocialPostId, ct);
        await SyncPostCommentsAsync(channel, post, provider, 2, ct);

        return (await GetThreadAsync(comment.Id, ct))!;
    }

    public async Task<SocialCommentResponse> HideAsync(Guid id, bool hide, CancellationToken ct)
    {
        var comment = await GetCommentOrThrow(id, ct);
        var channel = await GetChannelOrThrow(comment.SocialChannelId, ct);
        var provider = ResolveProvider(comment.Platform);
        if (hide && !provider.Capabilities.CanHide)
            throw new InvalidOperationException("Nền tảng không hỗ trợ ẩn comment");
        if (!hide && !provider.Capabilities.CanUnhide)
            throw new InvalidOperationException("Nền tảng không hỗ trợ hiện comment");

        var result = await provider.HideAsync(comment.ExternalCommentId, channel.AccessToken, hide, ct);
        await LogActionAsync(comment.Id, hide ? CommentActionType.Hide : CommentActionType.Unhide, result, null, ct);
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "Thao tác ẩn/hiện thất bại");

        comment.IsHidden = hide;
        comment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await GetThreadAsync(comment.Id, ct))!;
    }

    public async Task<SocialCommentResponse> DeleteAsync(Guid id, CancellationToken ct)
    {
        var comment = await GetCommentOrThrow(id, ct);
        var channel = await GetChannelOrThrow(comment.SocialChannelId, ct);
        var provider = ResolveProvider(comment.Platform);
        if (!provider.Capabilities.CanDelete)
            throw new InvalidOperationException("Nền tảng không hỗ trợ xóa comment");

        var result = await provider.DeleteAsync(comment.ExternalCommentId, channel.AccessToken, ct);
        await LogActionAsync(comment.Id, CommentActionType.Delete, result, null, ct);
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "Xóa comment thất bại");

        comment.IsDeletedOnPlatform = true;
        comment.InboxStatus = CommentInboxStatus.Deleted;
        comment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await GetThreadAsync(comment.Id, ct))!;
    }

    public async Task<SocialCommentResponse> ManagePendingAsync(Guid id, bool approve, CancellationToken ct)
    {
        var comment = await GetCommentOrThrow(id, ct);
        var channel = await GetChannelOrThrow(comment.SocialChannelId, ct);
        var provider = ResolveProvider(comment.Platform);
        if (!provider.Capabilities.CanManagePending)
            throw new InvalidOperationException("Nền tảng không hỗ trợ pending replies");

        var result = await provider.ManagePendingAsync(comment.ExternalCommentId, channel.AccessToken, approve, ct);
        await LogActionAsync(comment.Id,
            approve ? CommentActionType.ApprovePending : CommentActionType.IgnorePending,
            result, null, ct);
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "Duyệt pending thất bại");

        comment.IsPending = false;
        if (!approve) comment.InboxStatus = CommentInboxStatus.Ignored;
        comment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await GetThreadAsync(comment.Id, ct))!;
    }

    public async Task<SocialCommentResponse> SetStatusAsync(Guid id, CommentInboxStatus status, CancellationToken ct)
    {
        var comment = await GetCommentOrThrow(id, ct);
        comment.InboxStatus = status;
        comment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await LogActionAsync(comment.Id, CommentActionType.SetStatus,
            ProviderActionResult.Ok(), status.ToString(), ct);
        return (await GetThreadAsync(comment.Id, ct))!;
    }

    public async Task<SocialCommentResponse> AssignAsync(Guid id, string? assignedTo, CancellationToken ct)
    {
        var comment = await GetCommentOrThrow(id, ct);
        comment.AssignedTo = string.IsNullOrWhiteSpace(assignedTo) ? null : assignedTo.Trim();
        if (comment.InboxStatus == CommentInboxStatus.New)
            comment.InboxStatus = CommentInboxStatus.InProgress;
        comment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await LogActionAsync(comment.Id, CommentActionType.Assign,
            ProviderActionResult.Ok(), comment.AssignedTo, ct);
        return (await GetThreadAsync(comment.Id, ct))!;
    }

    public async Task<SocialCommentResponse> AddNoteAsync(Guid id, string note, CancellationToken ct)
    {
        var comment = await GetCommentOrThrow(id, ct);
        comment.InternalNote = note?.Trim();
        comment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await LogActionAsync(comment.Id, CommentActionType.AddNote,
            ProviderActionResult.Ok(), comment.InternalNote, ct);
        return (await GetThreadAsync(comment.Id, ct))!;
    }

    public async Task<bool> EnqueueWebhookAsync(
        SocialPlatform platform, string eventKey, string? objectId, string? verb, string? item,
        string payloadJson, CancellationToken ct)
    {
        var exists = await db.WebhookEvents.AnyAsync(x => x.EventKey == eventKey && !x.IsDeleted, ct);
        if (exists) return false;

        db.WebhookEvents.Add(new WebhookEventModel
        {
            Id = Guid.NewGuid(),
            Platform = platform,
            EventKey = eventKey,
            ObjectId = objectId,
            Verb = verb,
            Item = item,
            PayloadJson = payloadJson,
            Status = WebhookEventStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ProcessPendingWebhooksAsync(int batchSize, CancellationToken ct)
    {
        var events = await db.WebhookEvents
            .Where(x => !x.IsDeleted && x.Status == WebhookEventStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .Take(Math.Max(1, batchSize))
            .ToListAsync(ct);

        var processed = 0;
        foreach (var ev in events)
        {
            ev.Status = WebhookEventStatus.Processing;
            ev.AttemptCount++;
            await db.SaveChangesAsync(ct);

            try
            {
                await HydrateWebhookEventAsync(ev, ct);
                ev.Status = WebhookEventStatus.Completed;
                ev.ProcessedAt = DateTime.UtcNow;
                ev.ErrorMessage = null;
                processed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Webhook hydrate failed for {EventKey}", ev.EventKey);
                ev.Status = WebhookEventStatus.Failed;
                ev.ErrorMessage = ex.Message.Length > 900 ? ex.Message[..900] : ex.Message;
            }

            await db.SaveChangesAsync(ct);
        }

        return processed;
    }

    private async Task HydrateWebhookEventAsync(WebhookEventModel ev, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(ev.PayloadJson);
        var root = doc.RootElement;

        if (ev.Platform == SocialPlatform.Facebook)
        {
            // Accept full page webhook OR a single stored change object.
            var changes = new List<(string? PageId, JsonElement Value)>();
            if (root.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    var pageId = GetString(entry, "id");
                    if (!entry.TryGetProperty("changes", out var changeArr)) continue;
                    foreach (var change in changeArr.EnumerateArray())
                    {
                        if (change.TryGetProperty("value", out var value))
                            changes.Add((pageId, value));
                    }
                }
            }
            else if (root.TryGetProperty("value", out var singleValue))
            {
                changes.Add((null, singleValue));
            }
            else if (root.TryGetProperty("item", out _))
            {
                // Payload already is the value object
                changes.Add((null, root));
            }

            foreach (var (pageIdHint, value) in changes)
            {
                var item = GetString(value, "item");
                if (!string.Equals(item, "comment", StringComparison.OrdinalIgnoreCase)) continue;

                var verb = GetString(value, "verb") ?? ev.Verb;
                var commentId = GetString(value, "comment_id");
                var postId = GetString(value, "post_id") ?? GetNestedId(value, "post");
                if (string.IsNullOrWhiteSpace(commentId)) continue;

                var pageId = pageIdHint
                             ?? (postId?.Contains('_') == true ? postId.Split('_')[0] : null);
                var channel = await FindChannelAsync(SocialPlatform.Facebook, pageId, ct);
                if (channel is null && !string.IsNullOrWhiteSpace(pageId))
                    continue;
                // If page id unknown, try match by existing social post / any FB channel later
                if (channel is null)
                {
                    channel = await db.SocialChannels
                        .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive
                                                  && x.Platform == SocialPlatform.Facebook
                                                  && x.AccessToken != null && x.AccessToken != "", ct);
                }
                if (channel is null) continue;

                if (string.Equals(verb, "remove", StringComparison.OrdinalIgnoreCase))
                {
                    var existing = await db.SocialComments
                        .FirstOrDefaultAsync(x => x.ExternalCommentId == commentId && !x.IsDeleted, ct);
                    if (existing is not null)
                    {
                        existing.IsDeletedOnPlatform = true;
                        existing.InboxStatus = CommentInboxStatus.Deleted;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(postId)) continue;
                var provider = ResolveProvider(SocialPlatform.Facebook);
                var socialPost = await UpsertPostAsync(channel, new ProviderPostDto { ExternalPostId = postId }, ct);
                var commentDto = await provider.GetCommentAsync(commentId, channel.AccessToken, ct);
                if (commentDto is null)
                {
                    commentDto = new ProviderCommentDto
                    {
                        ExternalCommentId = commentId,
                        ParentExternalCommentId = GetString(value, "parent_id"),
                        Message = GetString(value, "message"),
                        CommentedAt = value.TryGetProperty("created_time", out var ctEl)
                                      && ctEl.TryGetInt64(out var unix)
                            ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime
                            : DateTime.UtcNow,
                        AuthorExternalId = GetNestedId(value, "from"),
                        AuthorName = value.TryGetProperty("from", out var from)
                                     && from.TryGetProperty("name", out var name)
                            ? name.GetString()
                            : null
                    };
                }

                await UpsertCommentAsync(channel, socialPost, commentDto, ct);
            }
        }
        else if (ev.Platform == SocialPlatform.Threads)
        {
            // Threads replies webhook values contain media id of the reply
            var replyId = ev.ObjectId
                          ?? GetString(root, "id")
                          ?? (root.TryGetProperty("value", out var v) ? GetString(v, "id") : null);
            if (string.IsNullOrWhiteSpace(replyId)) return;

            // Find any Threads channel and try hydrate (token is user-scoped)
            var channels = await db.SocialChannels
                .Where(x => !x.IsDeleted && x.IsActive && x.Platform == SocialPlatform.Threads)
                .ToListAsync(ct);
            foreach (var channel in channels)
            {
                var provider = ResolveProvider(SocialPlatform.Threads);
                var commentDto = await provider.GetCommentAsync(replyId, channel.AccessToken, ct);
                if (commentDto is null) continue;

                var rootPostId = commentDto.ParentExternalCommentId;
                // Prefer root_post if present in payload
                if (root.TryGetProperty("value", out var val)
                    && val.TryGetProperty("root_post", out var rootPost)
                    && rootPost.TryGetProperty("id", out var rootIdEl))
                {
                    rootPostId = rootIdEl.GetString() ?? rootPostId;
                }

                if (string.IsNullOrWhiteSpace(rootPostId))
                    rootPostId = replyId; // fallback: treat as top-level

                var socialPost = await UpsertPostAsync(channel, new ProviderPostDto { ExternalPostId = rootPostId }, ct);
                await UpsertCommentAsync(channel, socialPost, commentDto, ct);
                break;
            }
        }
    }

    public async Task SubscribeFacebookPagesAsync(CancellationToken ct = default)
    {
        var pages = await db.SocialChannels
            .Where(x => !x.IsDeleted && x.IsActive && x.Platform == SocialPlatform.Facebook)
            .ToListAsync(ct);
        var fb = publishOptions.Value.Facebook;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.AccessToken) || string.IsNullOrWhiteSpace(page.ExternalPageId))
                continue;

            var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(page.ExternalPageId)}/subscribed_apps";
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["subscribed_fields"] = "feed,messages,messaging_postbacks,message_deliveries,message_reads",
                ["access_token"] = page.AccessToken
            });
            try
            {
                using var response = await client.PostAsync(url, content, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    logger.LogWarning("Subscribe page {PageId} failed: {Body}", page.ExternalPageId, body);
                else
                    logger.LogInformation("Subscribed Facebook page {PageId} to feed and Messenger webhooks", page.ExternalPageId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Subscribe page {PageId} error", page.ExternalPageId);
            }
        }
    }

    public bool VerifyMetaSignature(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;
        var expected = signatureHeader["sha256=".Length..].Trim();
        var secret = metaOptions.Value.AppSecret;
        if (string.IsNullOrWhiteSpace(secret)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expected.ToLowerInvariant()));
    }

    public bool VerifyThreadsSignature(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;
        var expected = signatureHeader["sha256=".Length..].Trim();
        var secret = threadsOptions.Value.AppSecret;
        if (string.IsNullOrWhiteSpace(secret)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expected.ToLowerInvariant()));
    }

    private async Task LogActionAsync(
        Guid commentId, CommentActionType type, ProviderActionResult result, string? payload, CancellationToken ct)
    {
        db.CommentActionLogs.Add(new CommentActionLogModel
        {
            Id = Guid.NewGuid(),
            SocialCommentId = commentId,
            ActionType = type,
            ActorUserId = userContext.GetCurrentUserId(),
            ActorUserName = userContext.GetCurrentUserName(),
            PayloadJson = payload,
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            ExternalResultId = result.ExternalId,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task<SocialCommentModel> GetCommentOrThrow(Guid id, CancellationToken ct)
        => await db.SocialComments.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
           ?? throw new KeyNotFoundException("Comment không tồn tại");

    private async Task<SocialChannelModel> GetChannelOrThrow(Guid id, CancellationToken ct)
        => await db.SocialChannels.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
           ?? throw new KeyNotFoundException("Kênh không tồn tại");

    private async Task<SocialChannelModel?> FindChannelAsync(
        SocialPlatform platform, string? externalPageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalPageId)) return null;
        return await db.SocialChannels
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive
                                      && x.Platform == platform
                                      && x.ExternalPageId == externalPageId, ct);
    }

    private SocialCommentResponse ToResponse(
        SocialCommentModel c, string? channelName, SocialPostModel? post)
    {
        var caps = GetCapabilities(c.Platform);
        return new SocialCommentResponse
        {
            Id = c.Id,
            SocialChannelId = c.SocialChannelId,
            ChannelName = channelName,
            SocialPostId = c.SocialPostId,
            PostMessage = post?.Message,
            PostPermalinkUrl = post?.PermalinkUrl,
            LocalPostId = post?.LocalPostId,
            Platform = c.Platform,
            ExternalCommentId = c.ExternalCommentId,
            ParentExternalCommentId = c.ParentExternalCommentId,
            ParentCommentId = c.ParentCommentId,
            AuthorExternalId = c.AuthorExternalId,
            AuthorName = c.AuthorName,
            AuthorUsername = c.AuthorUsername,
            Message = c.Message,
            PermalinkUrl = c.PermalinkUrl,
            CommentedAt = c.CommentedAt,
            IsHidden = c.IsHidden,
            IsFromPage = c.IsFromPage,
            IsPending = c.IsPending,
            IsDeletedOnPlatform = c.IsDeletedOnPlatform,
            LikeCount = c.LikeCount,
            ReplyCount = c.ReplyCount,
            InboxStatus = c.InboxStatus,
            AssignedTo = c.AssignedTo,
            InternalNote = c.InternalNote,
            RepliedAt = c.RepliedAt,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            Capabilities = caps
        };
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string? GetNestedId(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var nested)) return null;
        if (nested.ValueKind == JsonValueKind.String) return nested.GetString();
        if (nested.ValueKind == JsonValueKind.Object) return GetString(nested, "id");
        return null;
    }
}
