using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.MediaFolder;

public class MediaFolderRepository : GenericRepository<MediaFolderModel>
{
    public MediaFolderRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<MediaFolderResponse>> FilterAsync(
        MediaFolderFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x => x.Name.Contains(kw));
        }

        if (request.ParentFolderId.HasValue)
            query = query.Where(x => x.ParentFolderId == request.ParentFolderId.Value);

        query = query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name);

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        var items = new List<MediaFolderResponse>();
        foreach (var f in paged.Items)
            items.Add(await ToResponseWithCountsAsync(f, ct));

        return new PagedResult<MediaFolderResponse>
        {
            Items = items,
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    /// <summary>Toàn bộ cây folder (không phân trang) — cho sidebar tree, kèm số ảnh + có con.</summary>
    public async Task<List<MediaFolderResponse>> GetTreeAsync(CancellationToken ct = default)
    {
        var folders = await QueryActive()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToListAsync(ct);

        var assetCounts = await Context.Set<MediaAssetModel>()
            .Where(x => !x.IsDeleted && x.FolderId != null)
            .GroupBy(x => x.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

        var parentsWithChildren = folders
            .Where(x => x.ParentFolderId.HasValue)
            .Select(x => x.ParentFolderId!.Value)
            .ToHashSet();

        return folders.Select(f => new MediaFolderResponse
        {
            Id = f.Id,
            Name = f.Name,
            Description = f.Description,
            ParentFolderId = f.ParentFolderId,
            SortOrder = f.SortOrder,
            AssetCount = assetCounts.TryGetValue(f.Id, out var c) ? c : 0,
            HasChildren = parentsWithChildren.Contains(f.Id),
            CreatedAt = f.CreatedAt
        }).ToList();
    }

    public async Task<MediaFolderModel> CreateAsync(
        CreateMediaFolderRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Tên thư mục không được để trống.");

        if (request.ParentFolderId.HasValue)
            await EnsureFolderExistsAsync(request.ParentFolderId.Value, ct);

        var entity = new MediaFolderModel
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ParentFolderId = request.ParentFolderId,
            SortOrder = request.SortOrder
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<MediaFolderModel?> UpdateAsync(
        Guid id, UpdateMediaFolderRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.Name is not null) entity.Name = request.Name.Trim();
        if (request.Description is not null) entity.Description = request.Description.Trim();
        if (request.SortOrder.HasValue) entity.SortOrder = request.SortOrder.Value;

        // ParentFolderId: form luôn gửi kèm (rỗng = đưa về gốc). Đổi cha thì validate tồn tại + chống lặp cây.
        if (request.ParentFolderId != entity.ParentFolderId)
        {
            if (request.ParentFolderId.HasValue)
            {
                await EnsureFolderExistsAsync(request.ParentFolderId.Value, ct);
                await EnsureNoCycleAsync(id, request.ParentFolderId.Value, ct);
            }
            entity.ParentFolderId = request.ParentFolderId;
        }

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>
    /// Xóa folder: còn thư mục con → chặn (xử lý con trước); ảnh trong folder → đưa về
    /// "Chưa phân loại" (FolderId = null) để không mồ côi.
    /// </summary>
    public override async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var hasChildren = await QueryActive().AnyAsync(x => x.ParentFolderId == id, ct);
        if (hasChildren)
            throw new InvalidOperationException("Thư mục còn thư mục con — hãy xử lý thư mục con trước khi xóa.");

        var assets = await Context.Set<MediaAssetModel>()
            .Where(x => !x.IsDeleted && x.FolderId == id)
            .ToListAsync(ct);
        foreach (var a in assets)
        {
            a.FolderId = null;
            a.UpdatedAt = DateTime.UtcNow;
            a.UpdatedBy = GetCurrentUserName();
        }

        return await base.SoftDeleteAsync(id, ct);
    }

    public static MediaFolderResponse ToResponse(MediaFolderModel f) => new()
    {
        Id = f.Id,
        Name = f.Name,
        Description = f.Description,
        ParentFolderId = f.ParentFolderId,
        SortOrder = f.SortOrder,
        AssetCount = 0,
        HasChildren = false,
        CreatedAt = f.CreatedAt
    };

    private async Task<MediaFolderResponse> ToResponseWithCountsAsync(
        MediaFolderModel f, CancellationToken ct)
    {
        var assetCount = await Context.Set<MediaAssetModel>()
            .CountAsync(x => !x.IsDeleted && x.FolderId == f.Id, ct);
        var hasChildren = await QueryActive().AnyAsync(x => x.ParentFolderId == f.Id, ct);

        var response = ToResponse(f);
        response.AssetCount = assetCount;
        response.HasChildren = hasChildren;
        return response;
    }

    private async Task EnsureFolderExistsAsync(Guid folderId, CancellationToken ct)
    {
        var exists = await QueryActive().AnyAsync(x => x.Id == folderId, ct);
        if (!exists)
            throw new InvalidOperationException("Thư mục cha không tồn tại.");
    }

    /// <summary>Đi ngược lên gốc từ parent đề xuất; gặp lại chính folder → lặp cây, từ chối.</summary>
    private async Task EnsureNoCycleAsync(Guid folderId, Guid newParentId, CancellationToken ct)
    {
        var cursor = (Guid?)newParentId;
        while (cursor.HasValue)
        {
            if (cursor.Value == folderId)
                throw new InvalidOperationException(
                    "Không thể đặt thư mục vào chính nó hoặc thư mục con của nó.");

            var parent = await QueryActive().FirstOrDefaultAsync(x => x.Id == cursor.Value, ct);
            if (parent is null) break;
            cursor = parent.ParentFolderId;
        }
    }
}
