using System.Text.Json;
using Backend.Data;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Crm.Customers;

public class CrmCustomerService(AppDbContext db, IUserContext userContext)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Tự tạo/gắn khách theo khớp chính xác (Platform, SocialChannelId, ExternalId).
    /// KHÔNG gộp khác Page / khác ExternalId / chỉ trùng tên hoặc số.
    /// </summary>
    public async Task<Guid> EnsureLinkedAsync(
        SocialPlatform platform,
        Guid socialChannelId,
        string externalId,
        string? displayName,
        CrmIdentitySource source,
        string? avatarUrl = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("ExternalId bắt buộc", nameof(externalId));

        var ext = externalId.Trim();
        var existing = await db.CrmCustomerIdentities
            .FirstOrDefaultAsync(x =>
                !x.IsDeleted
                && x.Platform == platform
                && x.SocialChannelId == socialChannelId
                && x.ExternalId == ext, ct);

        if (existing is not null)
        {
            var touched = false;
            if (!string.IsNullOrWhiteSpace(displayName)
                && !string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal))
            {
                existing.DisplayName = displayName.Trim();
                touched = true;
            }

            if (!string.IsNullOrWhiteSpace(avatarUrl) && existing.AvatarUrl != avatarUrl)
            {
                existing.AvatarUrl = avatarUrl;
                touched = true;
            }

            existing.Source = source;
            if (touched)
            {
                existing.UpdatedAt = DateTime.UtcNow;
                var customer = await db.CrmCustomers
                    .FirstOrDefaultAsync(c => c.Id == existing.CrmCustomerId && !c.IsDeleted, ct);
                if (customer is not null
                    && !string.IsNullOrWhiteSpace(displayName)
                    && customer.DisplayName == existing.ExternalId)
                {
                    customer.DisplayName = displayName.Trim();
                    customer.UpdatedAt = DateTime.UtcNow;
                }
            }

            await db.SaveChangesAsync(ct);
            return existing.CrmCustomerId;
        }

        var name = string.IsNullOrWhiteSpace(displayName) ? ext : displayName.Trim();
        var customerEntity = new CrmCustomerModel
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system:identity-link"
        };
        db.CrmCustomers.Add(customerEntity);
        db.CrmCustomerIdentities.Add(new CrmCustomerIdentityModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = customerEntity.Id,
            Platform = platform,
            SocialChannelId = socialChannelId,
            ExternalId = ext,
            Source = source,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            AvatarUrl = avatarUrl,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system:identity-link"
        });
        await db.SaveChangesAsync(ct);
        return customerEntity.Id;
    }

    public async Task SuggestPhonesFromTextAsync(
        Guid customerId,
        string? text,
        Guid? sourceMessageId = null,
        Guid? sourceCommentId = null,
        CancellationToken ct = default)
    {
        if (!await db.CrmCustomers.AnyAsync(x => x.Id == customerId && !x.IsDeleted, ct))
            return;

        foreach (var (e164, raw) in VietnamesePhoneNormalizer.ExtractFromText(text))
        {
            var exists = await db.CrmCustomerPhoneSuggestions.AnyAsync(x =>
                !x.IsDeleted
                && x.CrmCustomerId == customerId
                && x.PhoneE164 == e164
                && !x.IsDismissed, ct);
            if (exists) continue;

            db.CrmCustomerPhoneSuggestions.Add(new CrmCustomerPhoneSuggestionModel
            {
                Id = Guid.NewGuid(),
                CrmCustomerId = customerId,
                PhoneE164 = e164,
                RawMatched = raw,
                SourceMessageId = sourceMessageId,
                SourceCommentId = sourceCommentId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "system:phone-extract"
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<CrmCustomerBackfillResult> BackfillAsync(CancellationToken ct = default)
    {
        var result = new CrmCustomerBackfillResult();
        var beforeCustomers = await db.CrmCustomers.CountAsync(x => !x.IsDeleted, ct);
        var beforeIdentities = await db.CrmCustomerIdentities.CountAsync(x => !x.IsDeleted, ct);

        var conversations = await db.PageConversations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ParticipantExternalId != "")
            .Select(x => new
            {
                x.SocialChannelId,
                x.ParticipantExternalId,
                x.ParticipantName,
                x.ParticipantAvatarUrl
            })
            .ToListAsync(ct);
        result.ConversationsScanned = conversations.Count;

        foreach (var c in conversations)
        {
            await EnsureLinkedAsync(
                SocialPlatform.Facebook,
                c.SocialChannelId,
                c.ParticipantExternalId,
                c.ParticipantName,
                CrmIdentitySource.Message,
                c.ParticipantAvatarUrl,
                ct);
        }

        var comments = await db.SocialComments.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && !x.IsFromPage
                        && x.AuthorExternalId != null
                        && x.AuthorExternalId != "")
            .Select(x => new
            {
                x.Platform,
                x.SocialChannelId,
                x.AuthorExternalId,
                x.AuthorName
            })
            .Distinct()
            .ToListAsync(ct);
        result.CommentsScanned = comments.Count;

        foreach (var c in comments)
        {
            await EnsureLinkedAsync(
                c.Platform,
                c.SocialChannelId,
                c.AuthorExternalId!,
                c.AuthorName,
                CrmIdentitySource.Comment,
                avatarUrl: null,
                ct);
        }

        var afterCustomers = await db.CrmCustomers.CountAsync(x => !x.IsDeleted, ct);
        var afterIdentities = await db.CrmCustomerIdentities.CountAsync(x => !x.IsDeleted, ct);
        result.CustomersCreated = Math.Max(0, afterCustomers - beforeCustomers);
        result.IdentitiesLinked = Math.Max(0, afterIdentities - beforeIdentities);
        result.AlreadyLinked = Math.Max(0, afterIdentities - result.IdentitiesLinked);

        await AddLogAsync(null, "Backfill", JsonSerializer.Serialize(result, JsonOpts), ct);
        return result;
    }

    public async Task<PagedResult<CrmCustomerListItemResponse>> FilterAsync(
        CrmCustomerFilterRequest request, CancellationToken ct = default)
    {
        var query = db.CrmCustomers.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            var phoneNorm = VietnamesePhoneNormalizer.TryNormalize(kw);
            query = query.Where(x =>
                x.DisplayName.Contains(kw)
                || (x.PhoneE164 != null && x.PhoneE164.Contains(kw))
                || (phoneNorm != null && x.PhoneE164 == phoneNorm)
                || db.CrmCustomerIdentities.Any(i =>
                    !i.IsDeleted
                    && i.CrmCustomerId == x.Id
                    && (i.ExternalId.Contains(kw)
                        || (i.DisplayName != null && i.DisplayName.Contains(kw)))));
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneE164))
        {
            var phone = VietnamesePhoneNormalizer.TryNormalize(request.PhoneE164)
                        ?? request.PhoneE164.Trim();
            query = query.Where(x => x.PhoneE164 == phone);
        }

        if (request.HasPhone == true)
            query = query.Where(x => x.PhoneE164 != null && x.PhoneE164 != "");
        else if (request.HasPhone == false)
            query = query.Where(x => x.PhoneE164 == null || x.PhoneE164 == "");

        var total = await query.CountAsync(ct);
        var index = Math.Max(1, request.Index);
        var size = Math.Clamp(request.Size, 1, 100);
        var page = await query
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Skip((index - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var ids = page.Select(x => x.Id).ToList();
        var counts = await db.CrmCustomerIdentities.AsNoTracking()
            .Where(x => !x.IsDeleted && ids.Contains(x.CrmCustomerId))
            .GroupBy(x => x.CrmCustomerId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return new PagedResult<CrmCustomerListItemResponse>
        {
            Items = page.Select(x => new CrmCustomerListItemResponse
            {
                Id = x.Id,
                DisplayName = x.DisplayName,
                PhoneE164 = x.PhoneE164,
                IdentityCount = counts.GetValueOrDefault(x.Id),
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            }).ToList(),
            Total = total,
            Index = index,
            Size = size
        };
    }

    public async Task<CrmCustomerDetailResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await db.CrmCustomers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (customer is null) return null;

        var identities = await db.CrmCustomerIdentities.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmCustomerId == id)
            .ToListAsync(ct);
        var channelIds = identities.Select(x => x.SocialChannelId).Distinct().ToList();
        var channels = await db.SocialChannels.AsNoTracking()
            .Where(x => channelIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.PageName, ct);

        var suggestions = await db.CrmCustomerPhoneSuggestions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmCustomerId == id && !x.IsDismissed)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        var tagIds = await db.CrmCustomerTagLinks.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmCustomerId == id)
            .Select(x => x.CrmTagId)
            .ToListAsync(ct);

        var noteCount = await db.CrmCustomerNotes.CountAsync(x => !x.IsDeleted && x.CrmCustomerId == id, ct);
        var reminderCount = await db.CrmCustomerReminders.CountAsync(
            x => !x.IsDeleted && x.CrmCustomerId == id, ct);

        return new CrmCustomerDetailResponse
        {
            Id = customer.Id,
            DisplayName = customer.DisplayName,
            PhoneE164 = customer.PhoneE164,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt,
            Identities = identities.Select(i => new CrmCustomerIdentityResponse
            {
                Id = i.Id,
                CrmCustomerId = i.CrmCustomerId,
                Platform = i.Platform,
                SocialChannelId = i.SocialChannelId,
                ChannelName = channels.GetValueOrDefault(i.SocialChannelId),
                ExternalId = i.ExternalId,
                Source = i.Source,
                DisplayName = i.DisplayName,
                AvatarUrl = i.AvatarUrl
            }).ToList(),
            PhoneSuggestions = suggestions.Select(s => new CrmCustomerPhoneSuggestionResponse
            {
                Id = s.Id,
                PhoneE164 = s.PhoneE164,
                RawMatched = s.RawMatched,
                SourceMessageId = s.SourceMessageId,
                SourceCommentId = s.SourceCommentId
            }).ToList(),
            TagIds = tagIds,
            NoteCount = noteCount,
            ReminderCount = reminderCount
        };
    }

    public async Task<IReadOnlyList<CrmMergeSuggestionResponse>> ListMergeSuggestionsAsync(
        CancellationToken ct = default)
    {
        var customers = await db.CrmCustomers.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new { x.Id, x.DisplayName, x.PhoneE164 })
            .ToListAsync(ct);

        var suggestions = new List<CrmMergeSuggestionResponse>();
        var seenPairs = new HashSet<string>(StringComparer.Ordinal);

        void AddPair(Guid a, string aName, Guid b, string bName, string reason, string? phone, string? name)
        {
            if (a == b) return;
            var key = a.CompareTo(b) < 0 ? $"{a}:{b}:{reason}" : $"{b}:{a}:{reason}";
            if (!seenPairs.Add(key)) return;
            var (leftId, leftName, rightId, rightName) = a.CompareTo(b) < 0
                ? (a, aName, b, bName)
                : (b, bName, a, aName);
            suggestions.Add(new CrmMergeSuggestionResponse
            {
                CustomerAId = leftId,
                CustomerAName = leftName,
                CustomerBId = rightId,
                CustomerBName = rightName,
                Reason = reason,
                SharedPhoneE164 = phone,
                SharedDisplayName = name
            });
        }

        var byPhone = customers
            .Where(x => !string.IsNullOrWhiteSpace(x.PhoneE164))
            .GroupBy(x => x.PhoneE164!, StringComparer.Ordinal);
        foreach (var g in byPhone)
        {
            var list = g.ToList();
            for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
                AddPair(list[i].Id, list[i].DisplayName, list[j].Id, list[j].DisplayName,
                    "same-phone", g.Key, null);
        }

        var byName = customers
            .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
            .GroupBy(x => NormalizeName(x.DisplayName));
        foreach (var g in byName)
        {
            if (string.IsNullOrEmpty(g.Key)) continue;
            var list = g.ToList();
            if (list.Count < 2) continue;
            for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
                AddPair(list[i].Id, list[i].DisplayName, list[j].Id, list[j].DisplayName,
                    "same-name", null, list[i].DisplayName);
        }

        // Cùng ExternalId ở Page khác → gợi ý (không tự gộp). Load memory — SQLite không APPLY.
        var identities = await db.CrmCustomerIdentities.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new { x.Platform, x.ExternalId, x.SocialChannelId, x.CrmCustomerId })
            .ToListAsync(ct);

        var nameById = customers.ToDictionary(x => x.Id, x => x.DisplayName);
        foreach (var g in identities.GroupBy(x => new { x.Platform, x.ExternalId }))
        {
            if (g.Select(x => x.SocialChannelId).Distinct().Count() <= 1) continue;
            var ids = g.Select(x => x.CrmCustomerId).Where(nameById.ContainsKey).Distinct().ToList();
            for (var i = 0; i < ids.Count; i++)
            for (var j = i + 1; j < ids.Count; j++)
                AddPair(ids[i], nameById[ids[i]], ids[j], nameById[ids[j]],
                    "same-external-id-different-page", null, null);
        }

        return suggestions;
    }

    public async Task<CrmCustomerMergeResult> MergeAsync(
        Guid keptCustomerId, Guid sourceCustomerId, CancellationToken ct = default)
    {
        if (keptCustomerId == sourceCustomerId)
            throw new InvalidOperationException("Không thể gộp khách với chính mình");

        var kept = await db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == keptCustomerId && !x.IsDeleted, ct)
                   ?? throw new KeyNotFoundException("Khách giữ lại không tồn tại");
        var source = await db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == sourceCustomerId && !x.IsDeleted, ct)
                     ?? throw new KeyNotFoundException("Khách nguồn không tồn tại");

        var identityIds = await db.CrmCustomerIdentities
            .Where(x => !x.IsDeleted && x.CrmCustomerId == sourceCustomerId)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var noteIds = await db.CrmCustomerNotes
            .Where(x => !x.IsDeleted && x.CrmCustomerId == sourceCustomerId)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var reminderIds = await db.CrmCustomerReminders
            .Where(x => !x.IsDeleted && x.CrmCustomerId == sourceCustomerId)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var tagLinkIds = await db.CrmCustomerTagLinks
            .Where(x => !x.IsDeleted && x.CrmCustomerId == sourceCustomerId)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var suggestionIds = await db.CrmCustomerPhoneSuggestions
            .Where(x => !x.IsDeleted && x.CrmCustomerId == sourceCustomerId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var snapshot = new MergeSnapshot
        {
            SourceCustomerId = source.Id,
            SourceDisplayName = source.DisplayName,
            SourcePhoneE164 = source.PhoneE164,
            SourceCreatedAt = source.CreatedAt,
            SourceCreatedBy = source.CreatedBy,
            KeptPhoneBefore = kept.PhoneE164,
            IdentityIds = identityIds,
            NoteIds = noteIds,
            ReminderIds = reminderIds,
            TagLinkIds = tagLinkIds,
            PhoneSuggestionIds = suggestionIds
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.CrmCustomerIdentities
            .Where(x => identityIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CrmCustomerId, keptCustomerId)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

        await db.CrmCustomerNotes
            .Where(x => noteIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CrmCustomerId, keptCustomerId)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

        await db.CrmCustomerReminders
            .Where(x => reminderIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CrmCustomerId, keptCustomerId)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

        // Tag: bỏ link trùng tag đã có trên kept
        var keptTagIds = await db.CrmCustomerTagLinks
            .Where(x => !x.IsDeleted && x.CrmCustomerId == keptCustomerId)
            .Select(x => x.CrmTagId)
            .ToListAsync(ct);
        var sourceTags = await db.CrmCustomerTagLinks
            .Where(x => tagLinkIds.Contains(x.Id))
            .ToListAsync(ct);
        foreach (var link in sourceTags)
        {
            if (keptTagIds.Contains(link.CrmTagId))
            {
                link.IsDeleted = true;
                link.DeletedAt = DateTime.UtcNow;
                link.DeletedBy = userContext.GetCurrentUserName();
            }
            else
            {
                link.CrmCustomerId = keptCustomerId;
                link.UpdatedAt = DateTime.UtcNow;
            }
        }

        var sourceSuggestions = await db.CrmCustomerPhoneSuggestions
            .Where(x => suggestionIds.Contains(x.Id))
            .ToListAsync(ct);
        var keptSuggestionPhones = await db.CrmCustomerPhoneSuggestions
            .Where(x => !x.IsDeleted && x.CrmCustomerId == keptCustomerId)
            .Select(x => x.PhoneE164)
            .ToListAsync(ct);
        foreach (var s in sourceSuggestions)
        {
            if (keptSuggestionPhones.Contains(s.PhoneE164) || kept.PhoneE164 == s.PhoneE164)
            {
                s.IsDeleted = true;
                s.DeletedAt = DateTime.UtcNow;
            }
            else
            {
                s.CrmCustomerId = keptCustomerId;
                s.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (string.IsNullOrWhiteSpace(kept.PhoneE164) && !string.IsNullOrWhiteSpace(source.PhoneE164))
            kept.PhoneE164 = source.PhoneE164;

        source.IsDeleted = true;
        source.DeletedAt = DateTime.UtcNow;
        source.DeletedBy = userContext.GetCurrentUserName();
        source.UpdatedAt = DateTime.UtcNow;
        kept.UpdatedAt = DateTime.UtcNow;
        kept.UpdatedBy = userContext.GetCurrentUserName();

        var mergeRecord = new CrmCustomerMergeRecordModel
        {
            Id = Guid.NewGuid(),
            KeptCustomerId = keptCustomerId,
            MergedCustomerId = sourceCustomerId,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOpts),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userContext.GetCurrentUserName()
        };
        db.CrmCustomerMergeRecords.Add(mergeRecord);

        await db.SaveChangesAsync(ct);
        await AddLogAsync(keptCustomerId, "Merge", mergeRecord.SnapshotJson, ct);
        await tx.CommitAsync(ct);

        return new CrmCustomerMergeResult
        {
            MergeRecordId = mergeRecord.Id,
            KeptCustomerId = keptCustomerId,
            MergedCustomerId = sourceCustomerId
        };
    }

    public async Task SplitAsync(Guid mergeRecordId, CancellationToken ct = default)
    {
        var record = await db.CrmCustomerMergeRecords
            .FirstOrDefaultAsync(x => x.Id == mergeRecordId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Bản ghi gộp không tồn tại");
        if (record.IsUndone)
            throw new InvalidOperationException("Gộp này đã được tách lại");

        var snapshot = JsonSerializer.Deserialize<MergeSnapshot>(record.SnapshotJson, JsonOpts)
                       ?? throw new InvalidOperationException("Snapshot gộp không hợp lệ");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var source = await db.CrmCustomers
            .FirstOrDefaultAsync(x => x.Id == snapshot.SourceCustomerId, ct);
        if (source is null)
        {
            source = new CrmCustomerModel
            {
                Id = snapshot.SourceCustomerId,
                DisplayName = snapshot.SourceDisplayName,
                PhoneE164 = snapshot.SourcePhoneE164,
                CreatedAt = snapshot.SourceCreatedAt,
                CreatedBy = snapshot.SourceCreatedBy
            };
            db.CrmCustomers.Add(source);
        }
        else
        {
            source.IsDeleted = false;
            source.DeletedAt = null;
            source.DeletedBy = null;
            source.DisplayName = snapshot.SourceDisplayName;
            source.PhoneE164 = snapshot.SourcePhoneE164;
            source.UpdatedAt = DateTime.UtcNow;
        }

        var kept = await db.CrmCustomers
            .FirstOrDefaultAsync(x => x.Id == record.KeptCustomerId && !x.IsDeleted, ct);
        if (kept is not null)
            kept.PhoneE164 = snapshot.KeptPhoneBefore;

        await db.CrmCustomerIdentities
            .Where(x => snapshot.IdentityIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CrmCustomerId, snapshot.SourceCustomerId)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow)
                .SetProperty(x => x.IsDeleted, false), ct);

        await db.CrmCustomerNotes
            .Where(x => snapshot.NoteIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CrmCustomerId, snapshot.SourceCustomerId)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow)
                .SetProperty(x => x.IsDeleted, false), ct);

        await db.CrmCustomerReminders
            .Where(x => snapshot.ReminderIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CrmCustomerId, snapshot.SourceCustomerId)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow)
                .SetProperty(x => x.IsDeleted, false), ct);

        await db.CrmCustomerTagLinks
            .Where(x => snapshot.TagLinkIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CrmCustomerId, snapshot.SourceCustomerId)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow)
                .SetProperty(x => x.IsDeleted, false), ct);

        await db.CrmCustomerPhoneSuggestions
            .Where(x => snapshot.PhoneSuggestionIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CrmCustomerId, snapshot.SourceCustomerId)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow)
                .SetProperty(x => x.IsDeleted, false), ct);

        record.IsUndone = true;
        record.UpdatedAt = DateTime.UtcNow;
        record.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await AddLogAsync(record.KeptCustomerId, "Split", record.SnapshotJson, ct);
        await tx.CommitAsync(ct);
    }

    public async Task ConfirmPhoneAsync(Guid customerId, ConfirmPhoneRequest request, CancellationToken ct = default)
    {
        var customer = await db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == customerId && !x.IsDeleted, ct)
                       ?? throw new KeyNotFoundException("Khách không tồn tại");

        string e164;
        if (request.SuggestionId.HasValue)
        {
            var suggestion = await db.CrmCustomerPhoneSuggestions.FirstOrDefaultAsync(x =>
                x.Id == request.SuggestionId.Value
                && x.CrmCustomerId == customerId
                && !x.IsDeleted
                && !x.IsDismissed, ct)
                ?? throw new KeyNotFoundException("Gợi ý số không tồn tại");
            e164 = suggestion.PhoneE164;
            suggestion.IsDismissed = true;
            suggestion.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            e164 = VietnamesePhoneNormalizer.TryNormalize(request.PhoneE164)
                   ?? throw new InvalidOperationException("Số điện thoại không hợp lệ (cần di động VN)");
        }

        customer.PhoneE164 = e164;
        customer.UpdatedAt = DateTime.UtcNow;
        customer.UpdatedBy = userContext.GetCurrentUserName();

        // Đóng các gợi ý cùng số
        var dupes = await db.CrmCustomerPhoneSuggestions
            .Where(x => !x.IsDeleted && x.CrmCustomerId == customerId && x.PhoneE164 == e164)
            .ToListAsync(ct);
        foreach (var d in dupes)
        {
            d.IsDismissed = true;
            d.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await AddLogAsync(customerId, "ConfirmPhone", e164, ct);
    }

    public async Task AttachTagAsync(Guid customerId, Guid tagId, CancellationToken ct = default)
    {
        if (!await db.CrmCustomers.AnyAsync(x => x.Id == customerId && !x.IsDeleted, ct))
            throw new KeyNotFoundException("Khách không tồn tại");
        if (!await db.CrmTags.AnyAsync(x => x.Id == tagId && !x.IsDeleted, ct))
            throw new KeyNotFoundException("Tag không tồn tại");

        var exists = await db.CrmCustomerTagLinks.AnyAsync(x =>
            !x.IsDeleted && x.CrmCustomerId == customerId && x.CrmTagId == tagId, ct);
        if (exists) return;

        db.CrmCustomerTagLinks.Add(new CrmCustomerTagLinkModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = customerId,
            CrmTagId = tagId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userContext.GetCurrentUserName()
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task AddLogAsync(Guid? customerId, string action, string? payload, CancellationToken ct = default)
    {
        db.CrmCustomerActionLogs.Add(new CrmCustomerActionLogModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = customerId,
            ActionType = action,
            ActorUserId = userContext.GetCurrentUserId(),
            ActorUserName = userContext.GetCurrentUserName(),
            PayloadJson = payload,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userContext.GetCurrentUserName()
        });
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeName(string name)
        => string.Join(' ', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private sealed class MergeSnapshot
    {
        public Guid SourceCustomerId { get; set; }
        public string SourceDisplayName { get; set; } = string.Empty;
        public string? SourcePhoneE164 { get; set; }
        public DateTime SourceCreatedAt { get; set; }
        public string? SourceCreatedBy { get; set; }
        public string? KeptPhoneBefore { get; set; }
        public List<Guid> IdentityIds { get; set; } = [];
        public List<Guid> NoteIds { get; set; } = [];
        public List<Guid> ReminderIds { get; set; } = [];
        public List<Guid> TagLinkIds { get; set; } = [];
        public List<Guid> PhoneSuggestionIds { get; set; } = [];
    }
}
