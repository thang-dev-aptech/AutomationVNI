using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.SocialChannel;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.MediaFolder;

public class MediaFolderRepository : GenericRepository<MediaFolderModel>
{
    public MediaFolderRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    /// <summary>
    /// API duyệt folder một cấp theo Page (SocialChannelId) và ParentFolderId.
    /// - ParentFolderId = null: lấy danh sách thư mục gốc của Page.
    /// - ParentFolderId != null: lấy danh sách thư mục con trực tiếp (một cấp) của thư mục cha.
    /// Trả về ChildFolderCount và DirectAssetCount; tuyệt đối không tải descendants.
    /// </summary>
    public async Task<PagedResult<MediaFolderResponse>> GetChildrenAsync(
        GetMediaFolderChildrenRequest request, CancellationToken ct = default)
    {
        if (request.SocialChannelId == Guid.Empty)
            throw new ArgumentException("SocialChannelId không được để trống.");

        var channelExists = await Context.Set<SocialChannelModel>()
            .AnyAsync(x => x.Id == request.SocialChannelId && !x.IsDeleted, ct);
        if (!channelExists)
            throw new KeyNotFoundException("Page/Kênh không tồn tại.");

        if (request.ParentFolderId.HasValue)
        {
            var parent = await QueryActive()
                .FirstOrDefaultAsync(x => x.Id == request.ParentFolderId.Value, ct);
            if (parent is null)
                throw new KeyNotFoundException("Thư mục cha không tồn tại.");

            if (parent.SocialChannelId != request.SocialChannelId)
                throw new ArgumentException("Thư mục cha không thuộc Page yêu cầu.");
        }

        var query = QueryActive()
            .Where(x => x.SocialChannelId == request.SocialChannelId);

        if (request.ParentFolderId.HasValue)
            query = query.Where(x => x.ParentFolderId == request.ParentFolderId.Value);
        else
            query = query.Where(x => x.ParentFolderId == null);

        var isDesc = string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        var sortBy = request.SortBy?.Trim().ToLowerInvariant();

        query = sortBy switch
        {
            "name" => isDesc
                ? query.OrderByDescending(x => x.Name).ThenBy(x => x.Id)
                : query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "createdat" => isDesc
                ? query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "updatedat" => isDesc
                ? query.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).ThenBy(x => x.Id)
                : query.OrderBy(x => x.UpdatedAt ?? x.CreatedAt).ThenBy(x => x.Id),
            _ => isDesc
                ? query.OrderByDescending(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id)
                : query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id),
        };

        var safeIndex = request.Index < 1 ? 1 : request.Index;
        var safeSize = request.Size < 1 ? 20 : (request.Size > 100 ? 100 : request.Size);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((safeIndex - 1) * safeSize)
            .Take(safeSize)
            .ToListAsync(ct);

        if (items.Count == 0)
        {
            return new PagedResult<MediaFolderResponse>
            {
                Items = [],
                Total = total,
                Index = safeIndex,
                Size = safeSize
            };
        }

        var folderIds = items.Select(x => x.Id).ToList();

        // 1 query nhóm đếm số lượng media trực tiếp trong folder
        var directAssetCounts = await Context.Set<MediaAssetModel>()
            .Where(x => !x.IsDeleted && x.FolderId.HasValue && folderIds.Contains(x.FolderId.Value))
            .GroupBy(x => x.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

        // 1 query nhóm đếm số lượng folder con trực tiếp thuộc cùng Page
        var childFolderCounts = await QueryActive()
            .Where(x => x.SocialChannelId == request.SocialChannelId && x.ParentFolderId.HasValue && folderIds.Contains(x.ParentFolderId.Value))
            .GroupBy(x => x.ParentFolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

        var responseItems = items.Select(f =>
        {
            var childCount = childFolderCounts.GetValueOrDefault(f.Id, 0);
            var assetCount = directAssetCounts.GetValueOrDefault(f.Id, 0);
            return new MediaFolderResponse
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                ParentFolderId = f.ParentFolderId,
                SocialChannelId = f.SocialChannelId,
                SortOrder = f.SortOrder,
                ChildFolderCount = childCount,
                DirectAssetCount = assetCount,
                AssetCount = assetCount,
                HasChildren = childCount > 0,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            };
        }).ToList();

        return new PagedResult<MediaFolderResponse>
        {
            Items = responseItems,
            Total = total,
            Index = safeIndex,
            Size = safeSize
        };
    }

    public async Task<PagedResult<MediaFolderResponse>> FilterAsync(
        MediaFolderFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x => x.Name.Contains(kw));
        }

        if (request.SocialChannelId.HasValue)
            query = query.Where(x => x.SocialChannelId == request.SocialChannelId.Value);

        if (request.ParentFolderId.HasValue)
            query = query.Where(x => x.ParentFolderId == request.ParentFolderId.Value);

        var isDesc = string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        var sortBy = request.SortBy?.Trim().ToLowerInvariant();

        query = sortBy switch
        {
            "name" => isDesc
                ? query.OrderByDescending(x => x.Name).ThenBy(x => x.Id)
                : query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "createdat" => isDesc
                ? query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "updatedat" => isDesc
                ? query.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).ThenBy(x => x.Id)
                : query.OrderBy(x => x.UpdatedAt ?? x.CreatedAt).ThenBy(x => x.Id),
            _ => isDesc
                ? query.OrderByDescending(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id)
                : query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id),
        };

        var safeIndex = request.Index < 1 ? 1 : request.Index;
        var safeSize = request.Size < 1 ? 20 : (request.Size > 100 ? 100 : request.Size);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((safeIndex - 1) * safeSize)
            .Take(safeSize)
            .ToListAsync(ct);

        if (items.Count == 0)
        {
            return new PagedResult<MediaFolderResponse>
            {
                Items = [],
                Total = total,
                Index = safeIndex,
                Size = safeSize
            };
        }

        var folderIds = items.Select(x => x.Id).ToList();

        var directAssetCounts = await Context.Set<MediaAssetModel>()
            .Where(x => !x.IsDeleted && x.FolderId.HasValue && folderIds.Contains(x.FolderId.Value))
            .GroupBy(x => x.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

        var childQuery = QueryActive()
            .Where(x => x.ParentFolderId.HasValue && folderIds.Contains(x.ParentFolderId.Value));
        if (request.SocialChannelId.HasValue)
            childQuery = childQuery.Where(x => x.SocialChannelId == request.SocialChannelId.Value);

        var childFolderCounts = await childQuery
            .GroupBy(x => x.ParentFolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

        var responseItems = items.Select(f =>
        {
            var childCount = childFolderCounts.GetValueOrDefault(f.Id, 0);
            var assetCount = directAssetCounts.GetValueOrDefault(f.Id, 0);
            return new MediaFolderResponse
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                ParentFolderId = f.ParentFolderId,
                SocialChannelId = f.SocialChannelId,
                SortOrder = f.SortOrder,
                ChildFolderCount = childCount,
                DirectAssetCount = assetCount,
                AssetCount = assetCount,
                HasChildren = childCount > 0,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            };
        }).ToList();

        return new PagedResult<MediaFolderResponse>
        {
            Items = responseItems,
            Total = total,
            Index = safeIndex,
            Size = safeSize
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

        var childCounts = folders
            .Where(x => x.ParentFolderId.HasValue)
            .GroupBy(x => x.ParentFolderId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return folders.Select(f =>
        {
            var directAssets = assetCounts.TryGetValue(f.Id, out var c) ? c : 0;
            var childCount = childCounts.GetValueOrDefault(f.Id, 0);
            return new MediaFolderResponse
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                ParentFolderId = f.ParentFolderId,
                SocialChannelId = f.SocialChannelId,
                SortOrder = f.SortOrder,
                ChildFolderCount = childCount,
                DirectAssetCount = directAssets,
                AssetCount = directAssets,
                HasChildren = childCount > 0,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            };
        }).ToList();
    }

    public async Task<MediaFolderModel> CreateAsync(
        CreateMediaFolderRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Tên thư mục không được để trống.");

        if (request.ParentFolderId.HasValue)
        {
            var parent = await QueryActive()
                .FirstOrDefaultAsync(x => x.Id == request.ParentFolderId.Value, ct);
            if (parent is null)
                throw new InvalidOperationException("Thư mục cha không tồn tại.");

            if (request.SocialChannelId.HasValue && parent.SocialChannelId.HasValue && request.SocialChannelId != parent.SocialChannelId)
                throw new InvalidOperationException("Thư mục con phải thuộc cùng Page với thư mục cha.");

            request.SocialChannelId ??= parent.SocialChannelId;
        }

        var entity = new MediaFolderModel
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ParentFolderId = request.ParentFolderId,
            SocialChannelId = request.SocialChannelId,
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
        if (request.SocialChannelId.HasValue) entity.SocialChannelId = request.SocialChannelId.Value;
        if (request.SortOrder.HasValue) entity.SortOrder = request.SortOrder.Value;

        // ParentFolderId: form luôn gửi kèm (rỗng = đưa về gốc). Đổi cha thì validate tồn tại + chống lặp cây.
        if (request.ParentFolderId != entity.ParentFolderId)
        {
            if (request.ParentFolderId.HasValue)
            {
                var parent = await QueryActive()
                    .FirstOrDefaultAsync(x => x.Id == request.ParentFolderId.Value, ct);
                if (parent is null)
                    throw new InvalidOperationException("Thư mục cha không tồn tại.");

                var targetChannelId = request.SocialChannelId ?? entity.SocialChannelId;
                if (targetChannelId.HasValue && parent.SocialChannelId.HasValue && targetChannelId != parent.SocialChannelId)
                    throw new InvalidOperationException("Thư mục cha không thuộc Page yêu cầu.");

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
        SocialChannelId = f.SocialChannelId,
        SortOrder = f.SortOrder,
        ChildFolderCount = 0,
        DirectAssetCount = 0,
        AssetCount = 0,
        HasChildren = false,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt
    };

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
