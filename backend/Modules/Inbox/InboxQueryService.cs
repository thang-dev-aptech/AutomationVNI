using System.Text.Json;
using Backend.Data;
using Backend.Modules.ChannelGroup;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Modules.SocialComment.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Inbox;

/// <summary>
/// Hộp thư gộp Tin nhắn + Bình luận (api/Inbox).
/// Phạm vi dữ liệu + xoá mềm khớp PageMessageService.FilterAsync và SocialCommentService.FilterInboxAsync:
/// - Message: PageConversations !IsDeleted (không lọc theo ownership kênh).
/// - Comment: SocialComments !IsDeleted &amp;&amp; !IsDeletedOnPlatform &amp;&amp; !IsFromPage &amp;&amp; ParentCommentId == null.
/// </summary>
public class InboxQueryService(
    AppDbContext db,
    ChannelGroupRepository channelGroups,
    SocialCommentService socialCommentService)
{
    public const string ScopeRuleMessage = "!PageConversation.IsDeleted";
    public const string ScopeRuleComment =
        "!IsDeleted && !IsDeletedOnPlatform && !IsFromPage && ParentCommentId == null";

    private static readonly TimeSpan ReplyWindow = TimeSpan.FromHours(24);

    public async Task<PagedResult<InboxListItemResponse>> FilterAsync(
        InboxFilterRequest request,
        CancellationToken ct = default)
    {
        var index = Math.Max(1, request.Index);
        var size = Math.Clamp(request.Size, 1, 100);
        var takePerKind = index * size;

        var includeMessage = request.Kinds is null or { Count: 0 }
            || request.Kinds.Contains(InboxItemKind.Message);
        var includeComment = request.Kinds is null or { Count: 0 }
            || request.Kinds.Contains(InboxItemKind.Comment);

        // OpenWindowOnly chỉ áp message — khi bật, bỏ comment khỏi kết quả.
        if (request.OpenWindowOnly == true)
            includeComment = false;

        var channelIds = await ResolveChannelIdsAsync(request, ct);
        // Chỉ nhóm kênh (không kênh lẻ) mà resolve rỗng → không có kết quả.
        if (request.ChannelGroupIds is { Count: > 0 }
            && (request.SocialChannelIds is null || request.SocialChannelIds.Count == 0)
            && channelIds is { Count: 0 })
        {
            return EmptyPage(index, size);
        }

        var messageKeys = includeMessage
            ? await QueryMessageKeysAsync(request, channelIds, takePerKind, ct)
            : [];
        var commentKeys = includeComment
            ? await QueryCommentKeysAsync(request, channelIds, takePerKind, ct)
            : [];

        var messageTotal = includeMessage
            ? await CountMessagesAsync(request, channelIds, ct)
            : 0;
        var commentTotal = includeComment
            ? await CountCommentsAsync(request, channelIds, ct)
            : 0;

        var merged = messageKeys
            .Concat(commentKeys)
            .OrderByDescending(x => x.LastActivityAt)
            .ThenByDescending(x => x.Id)
            .Skip((index - 1) * size)
            .Take(size)
            .ToList();

        var items = await HydrateAsync(merged, ct);
        return new PagedResult<InboxListItemResponse>
        {
            Items = items,
            Total = messageTotal + commentTotal,
            Index = index,
            Size = size
        };
    }

    public async Task<InboxSummaryResponse> SummaryAsync(CancellationToken ct = default)
    {
        var messages = ScopedMessages();
        var comments = ScopedComments();

        var msgUnread = await messages.CountAsync(x => x.UnreadCount > 0, ct);
        var cmtUnread = await comments.CountAsync(x => x.InboxStatus == CommentInboxStatus.New, ct);
        var msgNew = await messages.CountAsync(x => x.InboxStatus == MessageInboxStatus.New, ct);
        var cmtNew = await comments.CountAsync(x => x.InboxStatus == CommentInboxStatus.New, ct);
        var msgProgress = await messages.CountAsync(x => x.InboxStatus == MessageInboxStatus.InProgress, ct);
        var cmtProgress = await comments.CountAsync(x => x.InboxStatus == CommentInboxStatus.InProgress, ct);

        return new InboxSummaryResponse
        {
            Unread = msgUnread + cmtUnread,
            NewCount = msgNew + cmtNew,
            InProgress = msgProgress + cmtProgress
        };
    }

    public async Task<InboxProfileResponse?> GetProfileAsync(
        InboxItemKind kind, Guid id, CancellationToken ct = default)
        => kind switch
        {
            InboxItemKind.Message => await GetMessageProfileAsync(id, ct),
            InboxItemKind.Comment => await GetCommentProfileAsync(id, ct),
            _ => null
        };

    // --- scope helpers (khớp PageMessage / SocialComment filter) ---

    private IQueryable<PageConversationModel> ScopedMessages()
        => db.PageConversations.AsNoTracking().Where(x => !x.IsDeleted);

    private IQueryable<SocialCommentModel> ScopedComments()
        => db.SocialComments.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsDeletedOnPlatform && !x.IsFromPage && x.ParentCommentId == null);

    private async Task<HashSet<Guid>?> ResolveChannelIdsAsync(
        InboxFilterRequest request, CancellationToken ct)
    {
        var hasChannels = request.SocialChannelIds is { Count: > 0 };
        var hasGroups = request.ChannelGroupIds is { Count: > 0 };
        if (!hasChannels && !hasGroups) return null;

        var set = new HashSet<Guid>();
        if (hasChannels)
        {
            foreach (var id in request.SocialChannelIds!)
                set.Add(id);
        }

        if (hasGroups)
        {
            var fromGroups = await channelGroups.ResolveChannelIdsAsync(request.ChannelGroupIds, ct);
            foreach (var id in fromGroups)
                set.Add(id);
        }

        return set;
    }

    private async Task<List<InboxKey>> QueryMessageKeysAsync(
        InboxFilterRequest request,
        HashSet<Guid>? channelIds,
        int take,
        CancellationToken ct)
    {
        var query = ApplyMessageFilters(ScopedMessages(), request, channelIds);
        var rows = await query
            .Select(x => new
            {
                x.Id,
                LastActivityAt = x.LastMessageAt ?? x.UpdatedAt ?? x.CreatedAt
            })
            .OrderByDescending(x => x.LastActivityAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);
        return rows
            .Select(x => new InboxKey(InboxItemKind.Message, x.Id, x.LastActivityAt))
            .ToList();
    }

    private async Task<int> CountMessagesAsync(
        InboxFilterRequest request, HashSet<Guid>? channelIds, CancellationToken ct)
        => await ApplyMessageFilters(ScopedMessages(), request, channelIds).CountAsync(ct);

    private IQueryable<PageConversationModel> ApplyMessageFilters(
        IQueryable<PageConversationModel> query,
        InboxFilterRequest request,
        HashSet<Guid>? channelIds)
    {
        if (channelIds is not null)
            query = query.Where(x => channelIds.Contains(x.SocialChannelId));

        if (request.Statuses is { Count: > 0 })
        {
            var statuses = request.Statuses
                .Where(s => Enum.IsDefined(typeof(MessageInboxStatus), s))
                .Select(s => (MessageInboxStatus)s)
                .ToList();
            if (statuses.Count > 0)
                query = query.Where(x => statuses.Contains(x.InboxStatus));
        }

        if (request.UnreadOnly == true)
            query = query.Where(x => x.UnreadCount > 0);

        if (request.AssignedUserIds is { Count: > 0 })
        {
            var ids = request.AssignedUserIds;
            query = query.Where(x => x.AssignedUserId != null && ids.Contains(x.AssignedUserId.Value));
        }

        if (request.OpenWindowOnly == true)
        {
            var cutoff = DateTime.UtcNow.Subtract(ReplyWindow);
            query = query.Where(x => x.LastCustomerMessageAt >= cutoff);
        }

        if (request.CustomerUnansweredOnly == true)
        {
            // Tin cuối do page gửi: LastPageMessageAt >= LastCustomerMessageAt
            query = query.Where(x =>
                x.LastPageMessageAt != null
                && (x.LastCustomerMessageAt == null
                    || x.LastPageMessageAt >= x.LastCustomerMessageAt));
        }

        if (request.FromUtc.HasValue)
        {
            var from = request.FromUtc.Value;
            query = query.Where(x => (x.LastMessageAt ?? x.UpdatedAt ?? x.CreatedAt) >= from);
        }

        if (request.ToUtc.HasValue)
        {
            var to = request.ToUtc.Value;
            query = query.Where(x => (x.LastMessageAt ?? x.UpdatedAt ?? x.CreatedAt) < to);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = SqliteVietnameseCollation.Lower(request.Keyword.Trim());
            query = query.Where(x =>
                (x.ParticipantName != null && AppDbContext.ViLower(x.ParticipantName).Contains(keyword))
                || AppDbContext.ViLower(x.ParticipantExternalId).Contains(keyword)
                || (x.Snippet != null && AppDbContext.ViLower(x.Snippet).Contains(keyword)));
        }

        return query;
    }

    private async Task<List<InboxKey>> QueryCommentKeysAsync(
        InboxFilterRequest request,
        HashSet<Guid>? channelIds,
        int take,
        CancellationToken ct)
    {
        var query = ApplyCommentFilters(ScopedComments(), request, channelIds);

        // LastActivityAt = max(root, latest reply) — project rồi sort/take.
        var projected = query.Select(x => new
        {
            x.Id,
            RootAt = x.CommentedAt ?? x.CreatedAt,
            ReplyMax = db.SocialComments
                .Where(r => !r.IsDeleted && r.ParentCommentId == x.Id)
                .Select(r => (DateTime?)(r.CommentedAt ?? r.CreatedAt))
                .Max()
        });

        if (request.FromUtc.HasValue || request.ToUtc.HasValue)
        {
            // Lọc theo hoạt động cuối sau khi có ReplyMax.
            var from = request.FromUtc;
            var to = request.ToUtc;
            projected = projected.Where(x =>
                (!from.HasValue || (x.ReplyMax ?? x.RootAt) >= from.Value)
                && (!to.HasValue || (x.ReplyMax ?? x.RootAt) < to.Value));
        }

        var rows = await projected
            .OrderByDescending(x => x.ReplyMax ?? x.RootAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

        return rows
            .Select(x => new InboxKey(InboxItemKind.Comment, x.Id, x.ReplyMax ?? x.RootAt))
            .ToList();
    }

    private async Task<int> CountCommentsAsync(
        InboxFilterRequest request, HashSet<Guid>? channelIds, CancellationToken ct)
    {
        var query = ApplyCommentFilters(ScopedComments(), request, channelIds);
        if (!request.FromUtc.HasValue && !request.ToUtc.HasValue)
            return await query.CountAsync(ct);

        var projected = query.Select(x => new
        {
            RootAt = x.CommentedAt ?? x.CreatedAt,
            ReplyMax = db.SocialComments
                .Where(r => !r.IsDeleted && r.ParentCommentId == x.Id)
                .Select(r => (DateTime?)(r.CommentedAt ?? r.CreatedAt))
                .Max()
        });
        var from = request.FromUtc;
        var to = request.ToUtc;
        return await projected.CountAsync(x =>
            (!from.HasValue || (x.ReplyMax ?? x.RootAt) >= from.Value)
            && (!to.HasValue || (x.ReplyMax ?? x.RootAt) < to.Value), ct);
    }

    private IQueryable<SocialCommentModel> ApplyCommentFilters(
        IQueryable<SocialCommentModel> query,
        InboxFilterRequest request,
        HashSet<Guid>? channelIds)
    {
        if (channelIds is not null)
            query = query.Where(x => channelIds.Contains(x.SocialChannelId));

        if (request.Statuses is { Count: > 0 })
        {
            var statuses = request.Statuses
                .Where(s => Enum.IsDefined(typeof(CommentInboxStatus), s))
                .Select(s => (CommentInboxStatus)s)
                .ToList();
            if (statuses.Count > 0)
                query = query.Where(x => statuses.Contains(x.InboxStatus));
        }

        if (request.UnreadOnly == true)
            query = query.Where(x => x.InboxStatus == CommentInboxStatus.New);

        if (request.AssignedUserIds is { Count: > 0 })
        {
            var ids = request.AssignedUserIds;
            query = query.Where(x => x.AssignedUserId != null && ids.Contains(x.AssignedUserId.Value));
        }

        if (request.CustomerUnansweredOnly == true)
        {
            // Page đã trả lời và là hoạt động cuối trên thread.
            query = query.Where(x =>
                db.SocialComments.Any(r =>
                    !r.IsDeleted
                    && r.ParentCommentId == x.Id
                    && r.IsFromPage
                    && (r.CommentedAt ?? r.CreatedAt)
                    >= (x.CommentedAt ?? x.CreatedAt)
                    && !db.SocialComments.Any(c =>
                        !c.IsDeleted
                        && c.ParentCommentId == x.Id
                        && !c.IsFromPage
                        && (c.CommentedAt ?? c.CreatedAt) > (r.CommentedAt ?? r.CreatedAt))));
        }

        // FromUtc/ToUtc áp trên LastActivityAt ở QueryCommentKeysAsync / CountCommentsAsync.

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = SqliteVietnameseCollation.Lower(request.Keyword.Trim());
            query = query.Where(x =>
                (x.Message != null && AppDbContext.ViLower(x.Message).Contains(keyword))
                || (x.AuthorName != null && AppDbContext.ViLower(x.AuthorName).Contains(keyword))
                || (x.AuthorUsername != null && AppDbContext.ViLower(x.AuthorUsername).Contains(keyword)));
        }

        return query;
    }

    private async Task<List<InboxListItemResponse>> HydrateAsync(
        List<InboxKey> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return [];

        var messageIds = keys.Where(k => k.Kind == InboxItemKind.Message).Select(k => k.Id).ToList();
        var commentIds = keys.Where(k => k.Kind == InboxItemKind.Comment).Select(k => k.Id).ToList();

        var messages = messageIds.Count == 0
            ? new Dictionary<Guid, PageConversationModel>()
            : await db.PageConversations.AsNoTracking()
                .Where(x => messageIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        var comments = commentIds.Count == 0
            ? new Dictionary<Guid, SocialCommentModel>()
            : await db.SocialComments.AsNoTracking()
                .Where(x => commentIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        var channelIds = messages.Values.Select(x => x.SocialChannelId)
            .Concat(comments.Values.Select(x => x.SocialChannelId))
            .Distinct()
            .ToList();
        var channels = await db.SocialChannels.AsNoTracking()
            .Where(x => channelIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var postIds = comments.Values.Select(x => x.SocialPostId).Distinct().ToList();
        var posts = postIds.Count == 0
            ? new Dictionary<Guid, SocialPostModel>()
            : await db.SocialPosts.AsNoTracking()
                .Where(x => postIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        var now = DateTime.UtcNow;
        var result = new List<InboxListItemResponse>(keys.Count);
        foreach (var key in keys)
        {
            if (key.Kind == InboxItemKind.Message && messages.TryGetValue(key.Id, out var msg))
            {
                channels.TryGetValue(msg.SocialChannelId, out var ch);
                var closesAt = msg.LastCustomerMessageAt?.Add(ReplyWindow);
                result.Add(new InboxListItemResponse
                {
                    Kind = InboxItemKind.Message,
                    Id = msg.Id,
                    SocialChannelId = msg.SocialChannelId,
                    ChannelName = ch?.PageName,
                    Platform = ch?.Platform ?? SocialPlatform.Facebook,
                    ParticipantName = msg.ParticipantName,
                    ParticipantAvatarUrl = msg.ParticipantAvatarUrl,
                    Snippet = msg.Snippet,
                    LastActivityAt = key.LastActivityAt,
                    UnreadCount = msg.UnreadCount,
                    InboxStatus = (int)msg.InboxStatus,
                    AssignedUserId = msg.AssignedUserId,
                    AssignedTo = msg.AssignedTo,
                    IsReplyWindowOpen = closesAt > now
                });
            }
            else if (key.Kind == InboxItemKind.Comment && comments.TryGetValue(key.Id, out var cmt))
            {
                channels.TryGetValue(cmt.SocialChannelId, out var ch);
                posts.TryGetValue(cmt.SocialPostId, out var post);
                result.Add(new InboxListItemResponse
                {
                    Kind = InboxItemKind.Comment,
                    Id = cmt.Id,
                    SocialChannelId = cmt.SocialChannelId,
                    ChannelName = ch?.PageName,
                    Platform = cmt.Platform,
                    ParticipantName = cmt.AuthorName ?? cmt.AuthorUsername,
                    ParticipantAvatarUrl = null,
                    Snippet = cmt.Message,
                    LastActivityAt = key.LastActivityAt,
                    UnreadCount = cmt.InboxStatus == CommentInboxStatus.New ? 1 : 0,
                    InboxStatus = (int)cmt.InboxStatus,
                    AssignedUserId = cmt.AssignedUserId,
                    AssignedTo = cmt.AssignedTo,
                    IsReplyWindowOpen = null,
                    SocialPostId = cmt.SocialPostId,
                    PermalinkUrl = cmt.PermalinkUrl ?? post?.PermalinkUrl
                });
            }
        }

        return result;
    }

    private async Task<InboxProfileResponse?> GetMessageProfileAsync(Guid id, CancellationToken ct)
    {
        var conversation = await ScopedMessages().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (conversation is null) return null;

        var channel = await db.SocialChannels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == conversation.SocialChannelId, ct);

        var messages = await db.PageMessages.AsNoTracking()
            .Where(x => x.PageConversationId == id && !x.IsDeleted)
            .OrderBy(x => x.SentAt ?? x.CreatedAt)
            .ToListAsync(ct);

        var customerMsgs = messages.Where(m => !m.IsFromPage).ToList();
        var pageMsgs = messages.Where(m => m.IsFromPage).ToList();
        var closesAt = conversation.LastCustomerMessageAt?.Add(ReplyWindow);

        var media = ExtractMedia(messages);
        var activities = await db.MessageActionLogs.AsNoTracking()
            .Where(x => x.PageConversationId == id && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .Select(x => new InboxActivityItem
            {
                Source = "message",
                ActionType = x.ActionType.ToString(),
                ActorUserName = x.ActorUserName,
                Success = x.Success,
                Detail = x.PayloadJson ?? x.ErrorMessage,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(ct);

        var other = await ScopedMessages()
            .Where(x =>
                x.SocialChannelId == conversation.SocialChannelId
                && x.ParticipantExternalId == conversation.ParticipantExternalId
                && x.Id != id)
            .OrderByDescending(x => x.LastMessageAt ?? x.UpdatedAt ?? x.CreatedAt)
            .Take(20)
            .Select(x => new InboxRelatedConversationItem
            {
                Kind = InboxItemKind.Message,
                Id = x.Id,
                ParticipantName = x.ParticipantName,
                Snippet = x.Snippet,
                LastActivityAt = x.LastMessageAt ?? x.UpdatedAt ?? x.CreatedAt,
                InboxStatus = (int)x.InboxStatus
            })
            .ToListAsync(ct);

        return new InboxProfileResponse
        {
            Kind = InboxItemKind.Message,
            Id = conversation.Id,
            ParticipantName = conversation.ParticipantName,
            ParticipantAvatarUrl = conversation.ParticipantAvatarUrl,
            ParticipantExternalId = conversation.ParticipantExternalId,
            SocialChannelId = conversation.SocialChannelId,
            ChannelName = channel?.PageName,
            Platform = channel?.Platform ?? SocialPlatform.Facebook,
            AssignedUserId = conversation.AssignedUserId,
            AssignedTo = conversation.AssignedTo,
            InternalNote = conversation.InternalNote,
            InboxStatus = (int)conversation.InboxStatus,
            Stats = new InboxInteractionStats
            {
                CustomerCount = customerMsgs.Count,
                PageCount = pageMsgs.Count,
                FirstAt = messages.Count > 0
                    ? messages.Min(m => m.SentAt ?? m.CreatedAt)
                    : conversation.CreatedAt,
                LastAt = messages.Count > 0
                    ? messages.Max(m => m.SentAt ?? m.CreatedAt)
                    : conversation.LastMessageAt
            },
            Media = media,
            Activities = activities,
            OtherConversations = other,
            IsReplyWindowOpen = closesAt > DateTime.UtcNow,
            ReplyWindowClosesAt = closesAt
        };
    }

    private async Task<InboxProfileResponse?> GetCommentProfileAsync(Guid id, CancellationToken ct)
    {
        var root = await ScopedComments().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (root is null)
        {
            // Ngoài phạm vi inbox (reply / from page / soft-deleted / deleted on platform) → 404
            return null;
        }

        var channel = await db.SocialChannels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == root.SocialChannelId, ct);
        var post = await db.SocialPosts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == root.SocialPostId, ct);

        var thread = await db.SocialComments.AsNoTracking()
            .Where(x =>
                !x.IsDeleted
                && (x.Id == root.Id || x.ParentCommentId == root.Id))
            .OrderBy(x => x.CommentedAt ?? x.CreatedAt)
            .ToListAsync(ct);

        var customer = thread.Where(c => !c.IsFromPage).ToList();
        var page = thread.Where(c => c.IsFromPage).ToList();

        var actionLogs = await socialCommentService.GetActionLogsAsync(root.Id, ct);
        var activities = actionLogs
            .Select(a => new InboxActivityItem
            {
                Source = "comment",
                ActionType = a.ActionType.ToString(),
                ActorUserName = a.ActorUserName,
                Success = a.Success,
                Detail = a.ErrorMessage ?? a.ExternalResultId,
                CreatedAt = a.CreatedAt
            })
            .ToList();

        var authorId = root.AuthorExternalId;
        var other = string.IsNullOrWhiteSpace(authorId)
            ? []
            : await ScopedComments()
                .Where(x =>
                    x.SocialChannelId == root.SocialChannelId
                    && x.AuthorExternalId == authorId
                    && x.Id != root.Id)
                .OrderByDescending(x => x.CommentedAt ?? x.CreatedAt)
                .Take(20)
                .Select(x => new InboxRelatedConversationItem
                {
                    Kind = InboxItemKind.Comment,
                    Id = x.Id,
                    ParticipantName = x.AuthorName ?? x.AuthorUsername,
                    Snippet = x.Message,
                    LastActivityAt = x.CommentedAt ?? x.CreatedAt,
                    InboxStatus = (int)x.InboxStatus
                })
                .ToListAsync(ct);

        return new InboxProfileResponse
        {
            Kind = InboxItemKind.Comment,
            Id = root.Id,
            ParticipantName = root.AuthorName ?? root.AuthorUsername,
            ParticipantAvatarUrl = null,
            ParticipantExternalId = root.AuthorExternalId,
            SocialChannelId = root.SocialChannelId,
            ChannelName = channel?.PageName,
            Platform = root.Platform,
            AssignedUserId = root.AssignedUserId,
            AssignedTo = root.AssignedTo,
            InternalNote = root.InternalNote,
            InboxStatus = (int)root.InboxStatus,
            Stats = new InboxInteractionStats
            {
                CustomerCount = customer.Count,
                PageCount = page.Count,
                FirstAt = thread.Count > 0
                    ? thread.Min(c => c.CommentedAt ?? c.CreatedAt)
                    : root.CreatedAt,
                LastAt = thread.Count > 0
                    ? thread.Max(c => c.CommentedAt ?? c.CreatedAt)
                    : root.CommentedAt
            },
            Media = [],
            Activities = activities,
            OtherConversations = other,
            SocialPostId = root.SocialPostId,
            PermalinkUrl = root.PermalinkUrl ?? post?.PermalinkUrl,
            IsReplyWindowOpen = null
        };
    }

    private static List<InboxMediaItem> ExtractMedia(List<PageMessageModel> messages)
    {
        var result = new List<InboxMediaItem>();
        foreach (var msg in messages)
        {
            if (string.IsNullOrWhiteSpace(msg.AttachmentsJson)) continue;
            try
            {
                using var doc = JsonDocument.Parse(msg.AttachmentsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var type = el.TryGetProperty("type", out var t) ? t.GetString()
                        : el.TryGetProperty("mime_type", out var m) ? m.GetString()
                        : null;
                    string? url = null;
                    if (el.TryGetProperty("url", out var u)) url = u.GetString();
                    else if (el.TryGetProperty("payload", out var payload)
                             && payload.TryGetProperty("url", out var pu))
                        url = pu.GetString();
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    result.Add(new InboxMediaItem
                    {
                        Type = type,
                        Url = url,
                        SentAt = msg.SentAt ?? msg.CreatedAt
                    });
                }
            }
            catch (JsonException)
            {
                // bỏ qua JSON hỏng
            }
        }

        return result;
    }

    private static PagedResult<InboxListItemResponse> EmptyPage(int index, int size)
        => new() { Items = [], Total = 0, Index = index, Size = size };

    private readonly record struct InboxKey(InboxItemKind Kind, Guid Id, DateTime LastActivityAt);
}
