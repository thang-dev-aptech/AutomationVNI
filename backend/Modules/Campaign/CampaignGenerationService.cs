using System.Text.Json;
using Backend.Data;
using Backend.Modules.Campaign.Enums;
using Backend.Modules.ChannelGroup;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.SocialChannel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Campaign;

public sealed record CampaignGenerationResult(int Created, int SkippedSlots, int CampaignsProcessed);

/// <summary>
/// Sinh bài cuốn chiếu ~7 ngày tới cho mọi chiến dịch Đang chạy (CAMPAIGN-01).
/// Idempotent nhờ unique (CampaignId, SocialChannelId, CampaignSlotAt).
/// </summary>
public class CampaignGenerationService(
    AppDbContext context,
    RecycleSourcePicker sourcePicker,
    TimeProvider timeProvider,
    ILogger<CampaignGenerationService> logger)
{
    public const string ScheduleTimezone = "Asia/Ho_Chi_Minh";
    private static readonly TimeSpan VnOffset = TimeSpan.FromHours(7);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Cho phép test gắn Random seed; mặc định Random.Shared.</summary>
    public Random Random { get; set; } = Random.Shared;

    public async Task<CampaignGenerationResult> GenerateDueAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var campaigns = await context.Set<CampaignModel>()
            .Where(c => !c.IsDeleted && c.Status == CampaignStatus.Running)
            .ToListAsync(ct);

        var created = 0;
        var skipped = 0;
        foreach (var campaign in campaigns)
        {
            var result = await GenerateForCampaignAsync(campaign, now, ct);
            created += result.Created;
            skipped += result.SkippedSlots;
        }

        return new CampaignGenerationResult(created, skipped, campaigns.Count);
    }

    public async Task<(int Created, int SkippedSlots)> GenerateForCampaignAsync(
        CampaignModel campaign,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        var channelIds = await ResolveRunningChannelIdsAsync(campaign.Id, ct);
        var publishTimes = DeserializeStringList(campaign.PublishTimesJson);
        var weekdays = DeserializeIntList(campaign.WeekdaysJson);
        if (channelIds.Count == 0 || publishTimes.Count == 0)
            return (0, 0);

        var slots = BuildSlots(campaign, publishTimes, weekdays, nowUtc);
        if (slots.Count == 0)
            return (0, 0);

        var warnings = new Dictionary<Guid, string>();
        var created = 0;
        var skipped = 0;
        const string actor = "campaign-worker";

        foreach (var channelId in channelIds)
        {
            var existingSlots = await context.Set<PostModel>()
                .Where(p => !p.IsDeleted
                    && p.CampaignId == campaign.Id
                    && p.SocialChannelId == channelId
                    && p.CampaignSlotAt != null
                    && p.Status != PostStatus.Cancelled)
                .Select(p => p.CampaignSlotAt!.Value)
                .ToListAsync(ct);
            var existingSet = existingSlots.ToHashSet();

            var sources = await sourcePicker.PickRandomPublishedAsync(
                channelId, take: Math.Max(slots.Count, 1), campaign.MediaType, ct);

            if (sources.Count == 0)
            {
                warnings[channelId] = campaign.MediaType == CampaignMediaType.Video
                    ? "Không có bài nguồn phù hợp (Video)"
                    : "Không có bài nguồn phù hợp (Ảnh)";
                skipped += slots.Count;
                continue;
            }

            var sourceIndex = 0;
            foreach (var slotUtc in slots)
            {
                if (existingSet.Contains(slotUtc))
                {
                    skipped++;
                    continue;
                }

                var source = sources[sourceIndex % sources.Count];
                sourceIndex++;

                var scheduledAt = ApplyJitter(slotUtc, campaign.JitterMinutes, nowUtc);
                var keepOld = campaign.ImageStrategy != CampaignImageStrategy.VectorSearch;
                var status = keepOld ? PostStatus.Scheduled : PostStatus.Queued;

                var newPost = new PostModel
                {
                    Id = Guid.NewGuid(),
                    Title = source.Title,
                    Content = source.Content,
                    CategoryId = source.CategoryId,
                    SocialChannelId = channelId,
                    TextTemplateId = source.TextTemplateId,
                    ImageTemplateId = keepOld ? source.ImageTemplateId : Guid.Empty,
                    GenerationFlow = GenerationFlow.Recycle,
                    Status = status,
                    SourcePostId = source.Id,
                    CampaignId = campaign.Id,
                    CampaignSlotAt = slotUtc,
                    UserId = Guid.Empty,
                    CreatedAt = nowUtc,
                    CreatedBy = actor,
                    ApprovedBy = keepOld ? actor : null,
                    ApprovedAt = keepOld ? nowUtc : null,
                    ScheduledPublishAt = scheduledAt,
                    ScheduleTimezone = ScheduleTimezone,
                };

                context.Add(newPost);
                if (keepOld)
                    await sourcePicker.CopyMediaKeepOldAsync(newPost.Id, source.Id, nowUtc, actor, ct);

                try
                {
                    await context.SaveChangesAsync(ct);
                    created++;
                    existingSet.Add(slotUtc);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // Worker song song / chạy lại — unique DB là ranh giới thật.
                    context.ChangeTracker.Clear();
                    skipped++;
                    logger.LogDebug(
                        ex,
                        "Campaign slot already exists campaign={CampaignId} channel={ChannelId} slot={Slot}",
                        campaign.Id, channelId, slotUtc);
                }
            }
        }

        await PersistWarningsAsync(campaign, channelIds, warnings, ct);
        return (created, skipped);
    }

    /// <summary>Kênh lẻ ∪ thành viên nhóm hiện tại chưa xoá.</summary>
    public async Task<List<Guid>> ResolveRunningChannelIdsAsync(
        Guid campaignId, CancellationToken ct = default)
    {
        var direct = await context.Set<CampaignChannelModel>()
            .Where(x => !x.IsDeleted && x.CampaignId == campaignId)
            .Select(x => x.SocialChannelId)
            .ToListAsync(ct);

        var groupIds = await context.Set<CampaignChannelGroupModel>()
            .Where(x => !x.IsDeleted && x.CampaignId == campaignId)
            .Select(x => x.ChannelGroupId)
            .ToListAsync(ct);

        List<Guid> fromGroups = [];
        if (groupIds.Count > 0)
        {
            var activeGroups = await context.Set<ChannelGroupModel>()
                .Where(g => !g.IsDeleted && groupIds.Contains(g.Id))
                .Select(g => g.Id)
                .ToListAsync(ct);
            if (activeGroups.Count > 0)
            {
                fromGroups = await context.Set<ChannelGroupMemberModel>()
                    .Where(m => !m.IsDeleted && activeGroups.Contains(m.ChannelGroupId))
                    .Select(m => m.SocialChannelId)
                    .Distinct()
                    .ToListAsync(ct);
            }
        }

        var union = direct.Concat(fromGroups).Distinct().ToList();
        if (union.Count == 0) return [];

        return await context.Set<SocialChannelModel>()
            .Where(c => !c.IsDeleted && union.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
    }

    public List<DateTime> BuildSlots(
        CampaignModel campaign,
        IReadOnlyList<string> publishTimes,
        IReadOnlyList<int> weekdays,
        DateTime nowUtc)
    {
        var vnNow = nowUtc + VnOffset;
        var vnToday = vnNow.Date;
        var horizonEnd = vnToday.AddDays(7); // exclusive

        var startDate = campaign.StartDate.Date;
        var windowStart = startDate > vnToday ? startDate : vnToday;
        var windowEnd = horizonEnd;
        if (campaign.EndDate.HasValue)
        {
            var endExclusive = campaign.EndDate.Value.Date.AddDays(1);
            if (endExclusive < windowEnd)
                windowEnd = endExclusive;
        }

        if (windowStart >= windowEnd)
            return [];

        var weekdaySet = weekdays.Count > 0
            ? weekdays.ToHashSet()
            : null;

        var slots = new List<DateTime>();
        for (var day = windowStart; day < windowEnd; day = day.AddDays(1))
        {
            if (campaign.ScheduleMode == CampaignScheduleMode.ByWeekday)
            {
                var dow = ToVnWeekday(day);
                if (weekdaySet is null || !weekdaySet.Contains(dow))
                    continue;
            }

            foreach (var timeText in publishTimes)
            {
                if (!TryParseHm(timeText, out var hour, out var minute))
                    continue;

                var slotVn = day.AddHours(hour).AddMinutes(minute);
                var slotUtc = DateTime.SpecifyKind(slotVn - VnOffset, DateTimeKind.Utc);

                // Khe đã qua (theo mốc khe, trước jitter) không sinh.
                if (slotUtc <= nowUtc)
                    continue;

                slots.Add(slotUtc);
            }
        }

        return slots;
    }

    private DateTime ApplyJitter(DateTime slotUtc, int jitterMinutes, DateTime nowUtc)
    {
        var offset = jitterMinutes > 0 ? Random.Next(-jitterMinutes, jitterMinutes + 1) : 0;
        var scheduled = slotUtc.AddMinutes(offset);
        if (scheduled <= nowUtc)
            scheduled = nowUtc.AddMinutes(1);
        return scheduled;
    }

    private async Task PersistWarningsAsync(
        CampaignModel campaign,
        IReadOnlyList<Guid> channelIds,
        Dictionary<Guid, string> warnings,
        CancellationToken ct)
    {
        // Chỉ giữ cảnh báo cho page đang chạy; page có bài nguồn thì xoá cảnh báo cũ.
        var pages = channelIds
            .Select(id => warnings.TryGetValue(id, out var msg)
                ? new CampaignPageWarning(id, msg)
                : null)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        var payload = new CampaignWarningsPayload { Pages = pages };
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        // Gắn vào ExtraJson dưới key ổn định (không đụng schema).
        var root = new Dictionary<string, JsonElement>();
        if (!string.IsNullOrWhiteSpace(campaign.ExtraJson))
        {
            try
            {
                root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    campaign.ExtraJson, JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                root = [];
            }
        }

        root["campaignWarnings"] = JsonSerializer.SerializeToElement(payload, JsonOptions);
        campaign.ExtraJson = JsonSerializer.Serialize(root, JsonOptions);
        campaign.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        campaign.UpdatedBy = "campaign-worker";
        await context.SaveChangesAsync(ct);
    }

    public static List<CampaignPageWarning> ReadWarnings(string? extraJson)
    {
        if (string.IsNullOrWhiteSpace(extraJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            if (!doc.RootElement.TryGetProperty("campaignWarnings", out var node))
                return [];
            var payload = node.Deserialize<CampaignWarningsPayload>(JsonOptions);
            return payload?.Pages ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SqliteException sqlite && sqlite.SqliteErrorCode == 19)
                return true;
            if (inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int ToVnWeekday(DateTime vnDate)
    {
        // Monday=1 … Sunday=7
        return ((int)vnDate.DayOfWeek + 6) % 7 + 1;
    }

    private static bool TryParseHm(string text, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        var parts = (text ?? string.Empty).Split(':');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out hour)
            && int.TryParse(parts[1], out minute)
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59;
    }

    private static List<int> DeserializeIntList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<int>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }
}

public sealed record CampaignPageWarning(Guid ChannelId, string Message);

internal sealed class CampaignWarningsPayload
{
    public List<CampaignPageWarning> Pages { get; set; } = [];
}
