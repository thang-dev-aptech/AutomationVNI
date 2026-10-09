using System.Text.Json;
using Backend.Data;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Crm.Tags;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialChannel.Enums;
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
    CrmCustomerCareService customerCare,
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

    /// <summary>
    /// Hồ sơ khách panel phải. Chỉ đọc; khớp identity chính xác (Platform, SocialChannelId, ExternalId).
    /// Không gọi EnsureLinked / SaveChanges.
    /// </summary>
    public async Task<CrmInboxCustomerPanelResponse?> GetCustomerPanelAsync(
        CrmInboxItemKind kind, Guid id, CancellationToken ct = default)
    {
        return kind switch
        {
            CrmInboxItemKind.Message => await GetMessageCustomerPanelAsync(id, ct),
            CrmInboxItemKind.Comment => await GetCommentCustomerPanelAsync(id, ct),
            _ => null
        };
    }

    private async Task<CrmInboxCustomerPanelResponse?> GetMessageCustomerPanelAsync(
        Guid conversationId, CancellationToken ct)
    {
        var conversation = await db.PageConversations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == conversationId && !x.IsDeleted, ct);
        if (conversation is null) return null;

        var channel = await db.SocialChannels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == conversation.SocialChannelId, ct);
        if (channel is null) return null;

        // Platform identity = channel.Platform (cùng EnsureLinkedAsync / LinkConversationCustomerAsync).
        var platform = channel.Platform;
        var externalId = conversation.ParticipantExternalId?.Trim() ?? string.Empty;

        var messages = await db.PageMessages.AsNoTracking()
            .Where(x => !x.IsDeleted && x.PageConversationId == conversationId)
            .OrderBy(x => x.SentAt ?? x.CreatedAt)
            .Select(x => new { x.AttachmentsJson, x.SentAt, x.CreatedAt })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var closesAt = conversation.LastCustomerMessageAt?.Add(MessageReplyWindow);
        var firstAt = messages.Count > 0
            ? messages.Min(m => m.SentAt ?? m.CreatedAt)
            : conversation.CreatedAt;
        var lastAt = messages.Count > 0
            ? messages.Max(m => m.SentAt ?? m.CreatedAt)
            : conversation.LastMessageAt ?? conversation.LastCustomerMessageAt ?? conversation.CreatedAt;

        var panel = new CrmInboxCustomerPanelResponse
        {
            Participant = new CrmInboxParticipantResponse
            {
                DisplayName = conversation.ParticipantName,
                ExternalId = string.IsNullOrEmpty(externalId) ? null : externalId,
                AvatarUrl = conversation.ParticipantAvatarUrl,
                ChannelName = channel.PageName,
                Platform = platform
            },
            Stats = new CrmInboxConversationStatsResponse
            {
                MessageCount = messages.Count,
                CommentCount = null,
                FirstInteractionAt = firstAt,
                LastInteractionAt = lastAt,
                IsReplyWindowOpen = closesAt > now,
                ReplyWindowClosesAt = closesAt,
                AssignedUserId = conversation.AssignedUserId,
                AssignedTo = conversation.AssignedTo,
                InboxStatus = (int)conversation.InboxStatus
            },
            Media = ExtractMedia(messages.Select(m => (m.AttachmentsJson, m.SentAt ?? m.CreatedAt)), max: 30)
        };

        await AttachCustomerAsync(panel, platform, conversation.SocialChannelId, externalId, ct);
        return panel;
    }

    private async Task<CrmInboxCustomerPanelResponse?> GetCommentCustomerPanelAsync(
        Guid commentId, CancellationToken ct)
    {
        var comment = await db.SocialComments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == commentId && !x.IsDeleted, ct);
        if (comment is null) return null;

        // Đi lên gốc (top-level) giống GetThreadAsync.
        var root = comment;
        while (root.ParentCommentId.HasValue)
        {
            var parent = await db.SocialComments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == root.ParentCommentId.Value && !x.IsDeleted, ct);
            if (parent is null) break;
            root = parent;
        }

        var channel = await db.SocialChannels.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == root.SocialChannelId, ct);
        if (channel is null) return null;

        var platform = channel.Platform;
        var externalId = root.AuthorExternalId?.Trim() ?? string.Empty;

        var onPost = await db.SocialComments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.SocialPostId == root.SocialPostId)
            .Select(x => new { x.Id, x.ParentCommentId, x.CommentedAt, x.CreatedAt })
            .ToListAsync(ct);

        var threadIds = CollectThreadIds(root.Id, onPost.Select(x => (x.Id, x.ParentCommentId)));
        var threadTimes = onPost
            .Where(x => threadIds.Contains(x.Id))
            .Select(x => x.CommentedAt ?? x.CreatedAt)
            .OrderBy(x => x)
            .ToList();

        var panel = new CrmInboxCustomerPanelResponse
        {
            Participant = new CrmInboxParticipantResponse
            {
                DisplayName = root.AuthorName ?? root.AuthorUsername,
                ExternalId = string.IsNullOrEmpty(externalId) ? null : externalId,
                AvatarUrl = null,
                ChannelName = channel.PageName,
                Platform = platform
            },
            Stats = new CrmInboxConversationStatsResponse
            {
                MessageCount = null,
                CommentCount = threadIds.Count,
                FirstInteractionAt = threadTimes.Count > 0 ? threadTimes[0] : null,
                LastInteractionAt = threadTimes.Count > 0 ? threadTimes[^1] : null,
                IsReplyWindowOpen = null,
                ReplyWindowClosesAt = null,
                AssignedUserId = root.AssignedUserId,
                AssignedTo = root.AssignedTo,
                InboxStatus = (int)root.InboxStatus
            },
            // SocialComment không có field attachment — trả [].
            Media = []
        };

        await AttachCustomerAsync(panel, platform, root.SocialChannelId, externalId, ct);
        return panel;
    }

    private async Task AttachCustomerAsync(
        CrmInboxCustomerPanelResponse panel,
        SocialPlatform platform,
        Guid socialChannelId,
        string externalId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            panel.Linked = false;
            panel.Customer = null;
            panel.Activities = [];
            return;
        }

        // Khớp CHÍNH XÁC 3 trường — không theo DisplayName / Phone.
        var identity = await db.CrmCustomerIdentities.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                !x.IsDeleted
                && x.Platform == platform
                && x.SocialChannelId == socialChannelId
                && x.ExternalId == externalId, ct);

        if (identity is null)
        {
            panel.Linked = false;
            panel.Customer = null;
            panel.Activities = [];
            return;
        }

        var customer = await db.CrmCustomers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == identity.CrmCustomerId && !x.IsDeleted, ct);
        if (customer is null)
        {
            panel.Linked = false;
            panel.Customer = null;
            panel.Activities = [];
            return;
        }

        var identities = await db.CrmCustomerIdentities.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmCustomerId == customer.Id)
            .ToListAsync(ct);
        var channelIds = identities.Select(x => x.SocialChannelId).Distinct().ToList();
        var channelNames = await db.SocialChannels.AsNoTracking()
            .Where(x => channelIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.PageName, ct);

        var tagIds = await db.CrmCustomerTagLinks.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmCustomerId == customer.Id)
            .Select(x => x.CrmTagId)
            .ToListAsync(ct);
        var noteCount = await db.CrmCustomerNotes.AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.CrmCustomerId == customer.Id, ct);
        var reminderCount = await db.CrmCustomerReminders.AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.CrmCustomerId == customer.Id, ct);

        panel.Linked = true;
        panel.Customer = new CrmInboxLinkedCustomerResponse
        {
            Id = customer.Id,
            DisplayName = customer.DisplayName,
            PhoneE164 = customer.PhoneE164,
            Email = null,
            TagIds = tagIds,
            NoteCount = noteCount,
            ReminderCount = reminderCount,
            Identities = identities.Select(i => new CrmInboxCustomerIdentityResponse
            {
                Platform = i.Platform,
                SocialChannelId = i.SocialChannelId,
                ChannelName = channelNames.GetValueOrDefault(i.SocialChannelId),
                ExternalId = i.ExternalId,
                DisplayName = i.DisplayName,
                AvatarUrl = i.AvatarUrl
            }).ToList()
        };

        // Tái dùng timeline care (read-only), lấy 20 mục gần nhất.
        var timeline = await customerCare.GetTimelineAsync(customer.Id, ct);
        panel.Activities = timeline.Take(20).ToList();
    }

    private static HashSet<Guid> CollectThreadIds(
        Guid rootId,
        IEnumerable<(Guid Id, Guid? ParentCommentId)> rows)
    {
        var byParent = rows
            .Where(x => x.ParentCommentId.HasValue)
            .GroupBy(x => x.ParentCommentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!byParent.TryGetValue(current, out var kids)) continue;
            foreach (var kid in kids)
            {
                if (result.Add(kid))
                    queue.Enqueue(kid);
            }
        }

        return result;
    }

    /// <summary>
    /// Parse AttachmentsJson Facebook Graph (data[].image_data/file_url/video_data/mime_type).
    /// </summary>
    private static List<CrmInboxMediaItemResponse> ExtractMedia(
        IEnumerable<(string? AttachmentsJson, DateTime SentAt)> messages,
        int max)
    {
        var media = new List<CrmInboxMediaItemResponse>();
        foreach (var (json, sentAt) in messages)
        {
            if (media.Count >= max) break;
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var items = root.ValueKind == JsonValueKind.Object
                            && root.TryGetProperty("data", out var data)
                    ? data
                    : root.ValueKind == JsonValueKind.Array
                        ? root
                        : default;
                if (items.ValueKind != JsonValueKind.Array) continue;

                foreach (var item in items.EnumerateArray())
                {
                    if (media.Count >= max) break;
                    var url = GetAttachmentUrl(item);
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    var type = GetAttachmentType(item, url);
                    if (type is not ("image" or "video")) continue;
                    media.Add(new CrmInboxMediaItemResponse
                    {
                        Url = url,
                        Type = type,
                        SentAt = sentAt
                    });
                }
            }
            catch (JsonException)
            {
                // Bỏ qua JSON lỗi — không crash panel.
            }
        }

        return media;
    }

    private static string? GetAttachmentUrl(JsonElement item)
    {
        if (item.TryGetProperty("image_data", out var imageData)
            && imageData.TryGetProperty("url", out var imageUrl)
            && imageUrl.ValueKind == JsonValueKind.String)
            return imageUrl.GetString();

        if (item.TryGetProperty("video_data", out var videoData)
            && videoData.TryGetProperty("url", out var videoUrl)
            && videoUrl.ValueKind == JsonValueKind.String)
            return videoUrl.GetString();

        if (item.TryGetProperty("file_url", out var fileUrl)
            && fileUrl.ValueKind == JsonValueKind.String)
            return fileUrl.GetString();

        if (item.TryGetProperty("payload", out var payload)
            && payload.TryGetProperty("url", out var payloadUrl)
            && payloadUrl.ValueKind == JsonValueKind.String)
            return payloadUrl.GetString();

        if (item.TryGetProperty("url", out var url)
            && url.ValueKind == JsonValueKind.String)
            return url.GetString();

        return null;
    }

    private static string GetAttachmentType(JsonElement item, string url)
    {
        if (item.TryGetProperty("type", out var typeEl)
            && typeEl.ValueKind == JsonValueKind.String)
        {
            var t = typeEl.GetString()?.Trim().ToLowerInvariant();
            if (t is "image" or "video") return t;
            if (t?.Contains("image", StringComparison.Ordinal) == true) return "image";
            if (t?.Contains("video", StringComparison.Ordinal) == true) return "video";
        }

        if (item.TryGetProperty("mime_type", out var mimeEl)
            && mimeEl.ValueKind == JsonValueKind.String)
        {
            var mime = mimeEl.GetString()?.ToLowerInvariant() ?? "";
            if (mime.StartsWith("image/", StringComparison.Ordinal)) return "image";
            if (mime.StartsWith("video/", StringComparison.Ordinal)) return "video";
        }

        if (item.TryGetProperty("image_data", out _)) return "image";
        if (item.TryGetProperty("video_data", out _)) return "video";

        var lower = url.ToLowerInvariant();
        if (lower.Contains(".mp4", StringComparison.Ordinal)
            || lower.Contains(".mov", StringComparison.Ordinal)
            || lower.Contains("video", StringComparison.Ordinal))
            return "video";
        return "image";
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
        var channelRows = await db.SocialChannels.AsNoTracking()
            .Where(x => channelIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PageName, x.Platform })
            .ToListAsync(ct);
        var channels = channelRows.ToDictionary(x => x.Id, x => x.PageName);
        var platforms = channelRows.ToDictionary(x => x.Id, x => (SocialPlatform?)x.Platform);

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
                    Platform = platforms.GetValueOrDefault(msg.SocialChannelId),
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
                    Platform = platforms.GetValueOrDefault(cmt.SocialChannelId),
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
