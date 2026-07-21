using Backend.Data;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.PageContext;

public class PageContextRepository : GenericRepository<PageContextModel>
{
    public PageContextRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<PageContextResponse>> FilterAsync(
        PageContextFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x => x.BrandName.Contains(kw));
        }

        if (request.SocialChannelId.HasValue)
            query = query.Where(x => x.SocialChannelId == request.SocialChannelId.Value);

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<PageContextResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<Dictionary<Guid, PageContextModel>> GetMapByChannelsAsync(
        IEnumerable<Guid> channelIds, CancellationToken ct = default)
    {
        var ids = channelIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, PageContextModel>();

        var list = await QueryActive()
            .Where(x => ids.Contains(x.SocialChannelId))
            .ToListAsync(ct);
        return list
            .GroupBy(x => x.SocialChannelId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>
    /// Page đủ cấu hình để bỏ chọn danh mục khi tạo bài:
    /// có default template id hoặc prompt text inline.
    /// </summary>
    public static bool HasTemplateReady(PageContextModel? pc)
        => pc is not null && (
            pc.DefaultTextTemplateId.HasValue
            || pc.DefaultImageTemplateId.HasValue
            || !string.IsNullOrWhiteSpace(pc.PromptTemplateText));

    public static (Guid? TextId, Guid? ImageId) ResolveDefaultTemplateIds(PageContextModel? pc)
    {
        if (pc is null) return (null, null);
        var text = pc.DefaultTextTemplateId ?? pc.DefaultImageTemplateId;
        var image = pc.DefaultImageTemplateId ?? pc.DefaultTextTemplateId;
        return (text, image);
    }

    public async Task<PageContextModel?> GetByChannelAsync(Guid socialChannelId, CancellationToken ct = default)
        => await QueryActive().FirstOrDefaultAsync(x => x.SocialChannelId == socialChannelId, ct);

    public async Task<PageContextModel> CreateAsync(
        CreatePageContextRequest request, CancellationToken ct = default)
    {
        var channelExists = await Context.SocialChannels
            .AnyAsync(x => x.Id == request.SocialChannelId && !x.IsDeleted, ct);
        if (!channelExists)
            throw new ArgumentException("Kênh mạng xã hội không tồn tại");

        var contextExists = await QueryActive()
            .AnyAsync(x => x.SocialChannelId == request.SocialChannelId, ct);
        if (contextExists)
            throw new ArgumentException("Page này đã có Page Context. Hãy cập nhật context hiện tại thay vì tạo thêm.");

        var entity = new PageContextModel
        {
            SocialChannelId = request.SocialChannelId,
            BrandName = request.BrandName.Trim(),
            ToneOfVoice = request.ToneOfVoice?.Trim(),
            LogoMediaId = request.LogoMediaId,
            CtaText = request.CtaText?.Trim(),
            CtaUrl = request.CtaUrl?.Trim(),
            DefaultHashtags = request.DefaultHashtags,
            PromptTemplateText = request.PromptTemplateText,
            PromptTemplateImage = request.PromptTemplateImage,
            DefaultTextTemplateId = request.DefaultTextTemplateId,
            DefaultImageTemplateId = request.DefaultImageTemplateId
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<PageContextModel?> UpdateAsync(
        Guid id, UpdatePageContextRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.BrandName is not null) entity.BrandName = request.BrandName.Trim();
        if (request.ToneOfVoice is not null) entity.ToneOfVoice = request.ToneOfVoice.Trim();
        if (request.LogoMediaId.HasValue) entity.LogoMediaId = request.LogoMediaId;
        if (request.CtaText is not null) entity.CtaText = request.CtaText.Trim();
        if (request.CtaUrl is not null) entity.CtaUrl = request.CtaUrl.Trim();
        if (request.DefaultHashtags is not null) entity.DefaultHashtags = request.DefaultHashtags;
        if (request.PromptTemplateText is not null) entity.PromptTemplateText = request.PromptTemplateText;
        if (request.PromptTemplateImage is not null) entity.PromptTemplateImage = request.PromptTemplateImage;
        // Guid.Empty = xoá default; giá trị khác = đặt; null = giữ nguyên.
        if (request.DefaultTextTemplateId.HasValue)
            entity.DefaultTextTemplateId = request.DefaultTextTemplateId.Value == Guid.Empty ? null : request.DefaultTextTemplateId;
        if (request.DefaultImageTemplateId.HasValue)
            entity.DefaultImageTemplateId = request.DefaultImageTemplateId.Value == Guid.Empty ? null : request.DefaultImageTemplateId;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public static PageContextResponse ToResponse(PageContextModel e) => new()
    {
        Id = e.Id,
        SocialChannelId = e.SocialChannelId,
        BrandName = e.BrandName,
        ToneOfVoice = e.ToneOfVoice,
        LogoMediaId = e.LogoMediaId,
        CtaText = e.CtaText,
        CtaUrl = e.CtaUrl,
        DefaultHashtags = e.DefaultHashtags,
        PromptTemplateText = e.PromptTemplateText,
        PromptTemplateImage = e.PromptTemplateImage,
        DefaultTextTemplateId = e.DefaultTextTemplateId,
        DefaultImageTemplateId = e.DefaultImageTemplateId,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
