using Backend.Data;
using Backend.Modules.Crm.Tags;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialComment;
using Backend.Modules.SocialComment.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Crm.Inbox;

public class CrmInboxService(
    AppDbContext db,
    PageMessageService pageMessageService,
    SocialCommentService socialCommentService,
    IUserContext userContext)
{
    private static readonly TimeSpan MessageReplyWindow = TimeSpan.FromHours(24);

    public async Task<PagedResult<CrmInboxListItemResponse>> FilterAsync(
        CrmInboxFilterRequest request,
        CancellationToken ct = default)
    {
        var index = Math.Max(1, request.Index);
        var size = Math.Clamp(request.Size, 1, 100);
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        var mineId = userContext.GetCurrentUserId();

        Guid? assignedFilter = request.AssignedUserId;
        if (request.AssignedMine == true)
            assignedFilter = mineId;

        var keys = new List<InboxKey>();

        if (request.Kind is null or CrmInboxItemKind.Message)
        {
            keys.AddRange(await QueryMessageKeysAsync(
                request, assignedFilter, keyword, ct));
        }

        if (request.Kind is null or CrmInboxItemKind.Comment)
        {
            keys.AddRange(await QueryCommentKeysAsync(
                request, assignedFilter, keyword, ct));
        }

        var ordered = keys
            .OrderByDescending(x => x.SortAt)
            .ThenByDescending(x => x.Id)
            .ToList();

        var total = ordered.Count;
        var pageKeys = ordered.Skip((index - 1) * size).Take(size).ToList();
        var items = await HydrateAsync(pageKeys, ct);

        return new PagedResult<CrmInboxListItemResponse>
        {
            Items = items,
            Total = total,
            Index = index,
            Size = size
        };
    }

    public async Task<CrmInboxMessageDetailResponse?> GetMessageAsync(
        Guid id, CancellationToken ct = default)
    {
        var conversation = await pageMessageService.GetAsync(id, ct);
        if (conversation is null) return null;

        // Đồng bộ tên canReply theo decision CRM t1 / AC inbox.
        conversation.CanReply = conversation.IsReplyWindowOpen;

        return new CrmInboxMessageDetailResponse
        {
            Conversation = conversation,
            Tags = await LoadTagsAsync(CrmTagTargetType.PageConversation, id, ct),
            ReplyEndpoint = $"/api/PageMessage/{id}/send"
        };
    }

    public async Task<CrmInboxCommentDetailResponse?> GetCommentAsync(
        Guid id, CancellationToken ct = default)
    {
        var thread = await socialCommentService.GetThreadAsync(id, ct);
        if (thread is null) return null;

        return new CrmInboxCommentDetailResponse
        {
            Thread = thread,
            Tags = await LoadTagsAsync(CrmTagTargetType.SocialComment, thread.Id, ct),
            ReplyEndpoint = $"/api/SocialComment/{thread.Id}/reply"
        };
    }

    private async Task<List<InboxKey>> QueryMessageKeysAsync(
        CrmInboxFilterRequest request,
        Guid? assignedFilter,
        string? keyword,
        CancellationToken ct)
    {
        var query = db.PageConversations.AsNoTracking().Where(x => !x.IsDeleted);

        if (request.SocialChannelId.HasValue)
            query = query.Where(x => x.SocialChannelId == request.SocialChannelId.Value);
        if (request.Status.HasValue)
            query = query.Where(x => (int)x.InboxStatus == request.Status.Value);
        if (assignedFilter.HasValue)
            query = query.Where(x => x.AssignedUserId == assignedFilter.Value);
        if (request.UnassignedOnly == true)
            query = query.Where(x => x.AssignedUserId == null);
        if (request.UnreadOnly == true)
            query = query.Where(x => x.UnreadCount > 0);
        if (request.From.HasValue)
            query = query.Where(x => (x.LastCustomerMessageAt ?? x.LastMessageAt ?? x.CreatedAt) >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(x => (x.LastCustomerMessageAt ?? x.LastMessageAt ?? x.CreatedAt) <= request.To.Value);
        if (request.TagId.HasValue)
        {
            var tagId = request.TagId.Value;
            query = query.Where(x => db.CrmTagLinks.Any(l =>
                !l.IsDeleted
                && l.CrmTagId == tagId
                && l.TargetType == CrmTagTargetType.PageConversation
                && l.TargetId == x.Id));
        }

        if (keyword is not null)
        {
            query = query.Where(x =>
                (x.ParticipantName != null && x.ParticipantName.Contains(keyword))
                || x.ParticipantExternalId.Contains(keyword)
                || (x.Snippet != null && x.Snippet.Contains(keyword))
                || (x.AssignedTo != null && x.AssignedTo.Contains(keyword))
                || db.PageMessages.Any(m =>
                    !m.IsDeleted
                    && m.PageConversationId == x.Id
                    && m.Text != null
                    && m.Text.Contains(keyword)));
        }

        return await query
            .Select(x => new InboxKey(
                CrmInboxItemKind.Message,
                x.Id,
                x.LastCustomerMessageAt ?? x.LastMessageAt ?? x.UpdatedAt ?? x.CreatedAt))
            .ToListAsync(ct);
    }

    private async Task<List<InboxKey>> QueryCommentKeysAsync(
        CrmInboxFilterRequest request,
        Guid? assignedFilter,
        string? keyword,
        CancellationToken ct)
    {
        var query = db.SocialComments.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsDeletedOnPlatform && !x.IsFromPage && x.ParentCommentId == null);

        if (request.SocialChannelId.HasValue)
            query = query.Where(x => x.SocialChannelId == request.SocialChannelId.Value);
        if (request.Status.HasValue)
            query = query.Where(x => (int)x.InboxStatus == request.Status.Value);
        if (assignedFilter.HasValue)
            query = query.Where(x => x.AssignedUserId == assignedFilter.Value);
        if (request.UnassignedOnly == true)
            query = query.Where(x => x.AssignedUserId == null);
        if (request.UnreadOnly == true)
            query = query.Where(x =>
                x.InboxStatus == CommentInboxStatus.New
                || x.InboxStatus == CommentInboxStatus.InProgress);
        if (request.From.HasValue)
            query = query.Where(x => (x.CommentedAt ?? x.CreatedAt) >= request.From.Value);
        if (request.To.HasValue)
            query = query.Where(x => (x.CommentedAt ?? x.CreatedAt) <= request.To.Value);
        if (request.TagId.HasValue)
        {
            var tagId = request.TagId.Value;
            query = query.Where(x => db.CrmTagLinks.Any(l =>
                !l.IsDeleted
                && l.CrmTagId == tagId
                && l.TargetType == CrmTagTargetType.SocialComment
                && l.TargetId == x.Id));
        }

        if (keyword is not null)
        {
            query = query.Where(x =>
                (x.Message != null && x.Message.Contains(keyword))
                || (x.AuthorName != null && x.AuthorName.Contains(keyword))
                || (x.AuthorUsername != null && x.AuthorUsername.Contains(keyword))
                || (x.AuthorExternalId != null && x.AuthorExternalId.Contains(keyword))
                || (x.AssignedTo != null && x.AssignedTo.Contains(keyword)));
        }

        return await query
            .Select(x => new InboxKey(
                CrmInboxItemKind.Comment,
                x.Id,
                x.CommentedAt ?? x.UpdatedAt ?? x.CreatedAt))
            .ToListAsync(ct);
    }

    private async Task<List<CrmInboxListItemResponse>> HydrateAsync(
        List<InboxKey> pageKeys, CancellationToken ct)
    {
        if (pageKeys.Count == 0) return [];

        var messageIds = pageKeys.Where(x => x.Kind == CrmInboxItemKind.Message).Select(x => x.Id).ToList();
        var commentIds = pageKeys.Where(x => x.Kind == CrmInboxItemKind.Comment).Select(x => x.Id).ToList();

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
            .ToDictionaryAsync(x => x.Id, x => x.PageName, ct);

        var allTags = await LoadTagsForTargetsAsync(
            messageIds.Select(id => (CrmTagTargetType.PageConversation, id))
                .Concat(commentIds.Select(id => (CrmTagTargetType.SocialComment, id)))
                .ToList(),
            ct);

        var now = DateTime.UtcNow;
        var result = new List<CrmInboxListItemResponse>(pageKeys.Count);
        foreach (var key in pageKeys)
        {
            if (key.Kind == CrmInboxItemKind.Message
                && messages.TryGetValue(key.Id, out var msg))
            {
                var closesAt = msg.LastCustomerMessageAt?.Add(MessageReplyWindow);
                result.Add(new CrmInboxListItemResponse
                {
                    Kind = CrmInboxItemKind.Message,
                    Id = msg.Id,
                    SocialChannelId = msg.SocialChannelId,
                    ChannelName = channels.GetValueOrDefault(msg.SocialChannelId),
                    DisplayName = msg.ParticipantName,
                    Snippet = msg.Snippet,
                    LastCustomerActivityAt = msg.LastCustomerMessageAt ?? msg.LastMessageAt,
                    Status = (int)msg.InboxStatus,
                    AssignedUserId = msg.AssignedUserId,
                    AssignedTo = msg.AssignedTo,
                    UnreadCount = msg.UnreadCount,
                    CanReply = closesAt > now,
                    ReplyWindowClosesAt = closesAt,
                    Tags = allTags.GetValueOrDefault((CrmTagTargetType.PageConversation, msg.Id)) ?? []
                });
            }
            else if (key.Kind == CrmInboxItemKind.Comment
                     && comments.TryGetValue(key.Id, out var cmt))
            {
                result.Add(new CrmInboxListItemResponse
                {
                    Kind = CrmInboxItemKind.Comment,
                    Id = cmt.Id,
                    SocialChannelId = cmt.SocialChannelId,
                    ChannelName = channels.GetValueOrDefault(cmt.SocialChannelId),
                    DisplayName = cmt.AuthorName ?? cmt.AuthorUsername,
                    Snippet = cmt.Message,
                    LastCustomerActivityAt = cmt.CommentedAt,
                    Status = (int)cmt.InboxStatus,
                    AssignedUserId = cmt.AssignedUserId,
                    AssignedTo = cmt.AssignedTo,
                    UnreadCount = cmt.InboxStatus is CommentInboxStatus.New or CommentInboxStatus.InProgress ? 1 : 0,
                    // Bình luận trả lời qua SocialComment (không khoá cửa sổ Messenger 24h).
                    CanReply = true,
                    ReplyWindowClosesAt = null,
                    Tags = allTags.GetValueOrDefault((CrmTagTargetType.SocialComment, cmt.Id)) ?? []
                });
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<CrmTagResponse>> LoadTagsAsync(
        CrmTagTargetType targetType, Guid targetId, CancellationToken ct)
    {
        var map = await LoadTagsForTargetsAsync([(targetType, targetId)], ct);
        return map.GetValueOrDefault((targetType, targetId)) ?? [];
    }

    private async Task<Dictionary<(CrmTagTargetType, Guid), List<CrmTagResponse>>> LoadTagsForTargetsAsync(
        List<(CrmTagTargetType Type, Guid Id)> targets,
        CancellationToken ct)
    {
        var result = new Dictionary<(CrmTagTargetType, Guid), List<CrmTagResponse>>();
        if (targets.Count == 0) return result;

        var typeGroups = targets.GroupBy(x => x.Type);
        foreach (var typeGroup in typeGroups)
        {
            var targetType = typeGroup.Key;
            var ids = typeGroup.Select(x => x.Id).Distinct().ToList();
            var rows = await db.CrmTagLinks.AsNoTracking()
                .Where(link => !link.IsDeleted
                               && link.TargetType == targetType
                               && ids.Contains(link.TargetId))
                .Join(
                    db.CrmTags.AsNoTracking().Where(tag => !tag.IsDeleted),
                    link => link.CrmTagId,
                    tag => tag.Id,
                    (link, tag) => new
                    {
                        link.TargetId,
                        tag.Id,
                        tag.Name,
                        tag.Color,
                        tag.CreatedAt,
                        tag.UpdatedAt
                    })
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                var key = (targetType, row.TargetId);
                if (!result.TryGetValue(key, out var list))
                {
                    list = [];
                    result[key] = list;
                }

                list.Add(new CrmTagResponse
                {
                    Id = row.Id,
                    Name = row.Name,
                    Color = row.Color,
                    CreatedAt = row.CreatedAt,
                    UpdatedAt = row.UpdatedAt
                });
            }
        }

        return result;
    }

    private sealed record InboxKey(CrmInboxItemKind Kind, Guid Id, DateTime SortAt);
}
