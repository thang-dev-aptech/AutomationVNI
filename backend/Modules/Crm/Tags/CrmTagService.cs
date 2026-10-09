using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialComment;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Crm.Tags;

public partial class CrmTagService(AppDbContext db, IUserContext userContext)
{
    private static readonly Regex HexColor = HexColorRegex();

    public async Task<IReadOnlyList<CrmTagResponse>> ListAsync(CancellationToken ct = default)
    {
        var tags = await db.CrmTags.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        return tags.Select(ToResponse).ToList();
    }

    public async Task<CrmTagResponse> CreateAsync(CreateCrmTagRequest request, CancellationToken ct = default)
    {
        var name = NormalizeName(request.Name);
        var color = NormalizeColor(request.Color);
        await EnsureNameUniqueAsync(name, excludeId: null, ct);

        var entity = new CrmTagModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            Color = color,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userContext.GetCurrentUserName()
        };
        db.CrmTags.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<CrmTagResponse?> UpdateAsync(
        Guid id, UpdateCrmTagRequest request, CancellationToken ct = default)
    {
        var entity = await db.CrmTags.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (entity is null) return null;

        var name = NormalizeName(request.Name);
        var color = NormalizeColor(request.Color);
        await EnsureNameUniqueAsync(name, excludeId: id, ct);

        entity.Name = name;
        entity.Color = color;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.CrmTags.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userContext.GetCurrentUserName();
        entity.UpdatedAt = DateTime.UtcNow;

        var links = await db.CrmTagLinks
            .Where(x => x.CrmTagId == id && !x.IsDeleted)
            .ToListAsync(ct);
        foreach (var link in links)
        {
            link.IsDeleted = true;
            link.DeletedAt = DateTime.UtcNow;
            link.DeletedBy = userContext.GetCurrentUserName();
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task AttachAsync(Guid tagId, CrmTagTargetRequest request, CancellationToken ct = default)
    {
        await EnsureTagExistsAsync(tagId, ct);
        await EnsureTargetExistsAsync(request.TargetType, request.TargetId, ct);

        var exists = await db.CrmTagLinks.AnyAsync(x =>
            !x.IsDeleted
            && x.CrmTagId == tagId
            && x.TargetType == request.TargetType
            && x.TargetId == request.TargetId, ct);
        if (exists) return;

        db.CrmTagLinks.Add(new CrmTagLinkModel
        {
            Id = Guid.NewGuid(),
            CrmTagId = tagId,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userContext.GetCurrentUserName()
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task DetachAsync(Guid tagId, CrmTagTargetRequest request, CancellationToken ct = default)
    {
        var link = await db.CrmTagLinks.FirstOrDefaultAsync(x =>
            !x.IsDeleted
            && x.CrmTagId == tagId
            && x.TargetType == request.TargetType
            && x.TargetId == request.TargetId, ct);
        if (link is null) return;

        link.IsDeleted = true;
        link.DeletedAt = DateTime.UtcNow;
        link.DeletedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureTagExistsAsync(Guid tagId, CancellationToken ct)
    {
        var ok = await db.CrmTags.AnyAsync(x => x.Id == tagId && !x.IsDeleted, ct);
        if (!ok) throw new KeyNotFoundException("Tag không tồn tại");
    }

    private async Task EnsureTargetExistsAsync(
        CrmTagTargetType targetType, Guid targetId, CancellationToken ct)
    {
        var ok = targetType switch
        {
            CrmTagTargetType.PageConversation =>
                await db.PageConversations.AnyAsync(x => x.Id == targetId && !x.IsDeleted, ct),
            CrmTagTargetType.SocialComment =>
                await db.SocialComments.AnyAsync(x => x.Id == targetId && !x.IsDeleted, ct),
            _ => false
        };
        if (!ok) throw new KeyNotFoundException("Đối tượng gắn tag không tồn tại");
    }

    private async Task EnsureNameUniqueAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var clash = await db.CrmTags.AnyAsync(x =>
            !x.IsDeleted
            && x.Name == name
            && (!excludeId.HasValue || x.Id != excludeId.Value), ct);
        if (clash)
            throw new InvalidOperationException("Tên tag đã tồn tại");
    }

    private static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Tên tag không được để trống");
        if (trimmed.Length > 100)
            throw new InvalidOperationException("Tên tag tối đa 100 ký tự");
        return trimmed;
    }

    private static string NormalizeColor(string? color)
    {
        var trimmed = (color ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return "#607D8B";
        if (!HexColor.IsMatch(trimmed))
            throw new InvalidOperationException("Màu tag phải là mã hex (#RGB hoặc #RRGGBB)");
        return trimmed.ToUpperInvariant();
    }

    private static CrmTagResponse ToResponse(CrmTagModel e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Color = e.Color,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };

    [GeneratedRegex("^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();
}
