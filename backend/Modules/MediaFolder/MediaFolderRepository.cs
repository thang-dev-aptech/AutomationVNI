using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialConnection;
using Backend.Shared;
using Backend.Shared.Repositories;
using Backend.Shared.Text;
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

        await EnsureSocialChannelAccessAsync(request.SocialChannelId, ct);

        if (request.ParentFolderId.HasValue)
        {
            var parent = await QueryActive()
                .FirstOrDefaultAsync(x =>
                    x.Id == request.ParentFolderId.Value &&
                    x.SocialChannelId == request.SocialChannelId, ct);
            if (parent is null)
                throw new KeyNotFoundException("Thư mục cha không tồn tại.");
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

    /// <summary>
    /// Breadcrumb từ gốc Page đến folder đích (MEDIA-02). Folder sai Page hoặc không tồn tại → KeyNotFound.
    /// </summary>
    public async Task<MediaFolderBreadcrumbResponse> GetBreadcrumbAsync(
        GetMediaFolderBreadcrumbRequest request, CancellationToken ct = default)
    {
        if (request.SocialChannelId == Guid.Empty)
            throw new ArgumentException("SocialChannelId không được để trống.");

        if (request.FolderId == Guid.Empty)
            throw new ArgumentException("FolderId không được để trống.");

        await EnsureSocialChannelAccessAsync(request.SocialChannelId, ct);

        var pageFolders = await QueryActive()
            .Where(x => x.SocialChannelId == request.SocialChannelId)
            .ToListAsync(ct);

        var byId = pageFolders.ToDictionary(x => x.Id);
        if (!byId.TryGetValue(request.FolderId, out var target))
            throw new KeyNotFoundException("Không tìm thấy thư mục.");

        var chain = new List<MediaFolderModel>();
        var cursor = target;
        var visited = new HashSet<Guid>();

        while (true)
        {
            if (!visited.Add(cursor.Id))
                throw new KeyNotFoundException("Không tìm thấy thư mục.");

            if (cursor.SocialChannelId != request.SocialChannelId)
                throw new KeyNotFoundException("Không tìm thấy thư mục.");

            chain.Add(cursor);

            if (!cursor.ParentFolderId.HasValue)
                break;

            if (!byId.TryGetValue(cursor.ParentFolderId.Value, out var parent))
                throw new KeyNotFoundException("Không tìm thấy thư mục.");

            cursor = parent;
        }

        chain.Reverse();

        return new MediaFolderBreadcrumbResponse
        {
            Ancestors = chain.Select(f => new MediaFolderBreadcrumbItem
            {
                Id = f.Id,
                Name = f.Name
            }).ToList()
        };
    }

    /// <summary>
    /// Tìm folder trong một Page; hỗ trợ tên có/không dấu; trả full path và counts (MEDIA-02).
    /// </summary>
    public async Task<PagedResult<MediaFolderSearchResultItem>> SearchFoldersAsync(
        SearchMediaFoldersRequest request, CancellationToken ct = default)
    {
        if (request.SocialChannelId == Guid.Empty)
            throw new ArgumentException("SocialChannelId không được để trống.");

        await EnsureSocialChannelAccessAsync(request.SocialChannelId, ct);

        var keyword = request.Keyword?.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            var emptyIndex = request.Index < 1 ? 1 : request.Index;
            var emptySize = request.Size < 1 ? 20 : (request.Size > 100 ? 100 : request.Size);
            return new PagedResult<MediaFolderSearchResultItem>
            {
                Items = [],
                Total = 0,
                Index = emptyIndex,
                Size = emptySize
            };
        }

        var pageFolders = await QueryActive()
            .Where(x => x.SocialChannelId == request.SocialChannelId)
            .ToListAsync(ct);

        var byId = pageFolders.ToDictionary(x => x.Id);
        // Chỉ trả folder có ancestor chain nguyên vẹn trong Page (MEDIA-02).
        // Parent thiếu / soft-deleted / thuộc Page khác → cùng semantics breadcrumb not-found: không lộ trong kết quả.
        var matches = pageFolders
            .Where(f => FolderNameMatchesKeyword(f.Name, keyword) && HasIntactPageAncestorChain(f, byId))
            .ToList();

        var isDesc = string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        var sortBy = request.SortBy?.Trim().ToLowerInvariant();

        IEnumerable<MediaFolderModel> ordered = sortBy switch
        {
            "createdat" => isDesc
                ? matches.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                : matches.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "updatedat" => isDesc
                ? matches.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).ThenBy(x => x.Id)
                : matches.OrderBy(x => x.UpdatedAt ?? x.CreatedAt).ThenBy(x => x.Id),
            "sortorder" => isDesc
                ? matches.OrderByDescending(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id)
                : matches.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id),
            _ => isDesc
                ? matches.OrderByDescending(x => x.Name).ThenBy(x => x.Id)
                : matches.OrderBy(x => x.Name).ThenBy(x => x.Id),
        };

        var safeIndex = request.Index < 1 ? 1 : request.Index;
        var safeSize = request.Size < 1 ? 20 : (request.Size > 100 ? 100 : request.Size);

        var sorted = ordered.ToList();
        var total = sorted.Count;
        var pageItems = sorted
            .Skip((safeIndex - 1) * safeSize)
            .Take(safeSize)
            .ToList();

        if (pageItems.Count == 0)
        {
            return new PagedResult<MediaFolderSearchResultItem>
            {
                Items = [],
                Total = total,
                Index = safeIndex,
                Size = safeSize
            };
        }

        var folderIds = pageItems.Select(x => x.Id).ToList();
        var (directAssetCounts, childFolderCounts) =
            await LoadDirectCountsAsync(request.SocialChannelId, folderIds, ct);

        var responseItems = pageItems.Select(f =>
        {
            var childCount = childFolderCounts.GetValueOrDefault(f.Id, 0);
            var assetCount = directAssetCounts.GetValueOrDefault(f.Id, 0);
            return new MediaFolderSearchResultItem
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
                FullPath = BuildFullPath(f.Id, byId),
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            };
        }).ToList();

        return new PagedResult<MediaFolderSearchResultItem>
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

    /// <summary>
    /// MEDIA-06: tạo 1 folder gốc cùng tên ở nhiều Page (vd 100 Page → 100 folder "Campaign X").
    /// Best-effort per Page — Page này lỗi (tên trống, Page không tồn tại/không có quyền, v.v.)
    /// không chặn các Page khác; mỗi Page tạo/lưu độc lập (không dùng transaction chung).
    /// </summary>
    public async Task<CreateMediaFolderAcrossPagesResponse> CreateAcrossPagesAsync(
        CreateMediaFolderAcrossPagesRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Tên thư mục không được để trống.");

        if (request.SocialChannelIds == null || request.SocialChannelIds.Count == 0)
            throw new ArgumentException("Danh sách Page không được để trống.");

        const int maxPages = 200;
        var pageIds = request.SocialChannelIds.Distinct().ToList();
        if (pageIds.Count > maxPages)
            throw new ArgumentException($"Số lượng Page vượt quá giới hạn cho phép (tối đa {maxPages}).");

        var results = new List<CreateMediaFolderAcrossPagesResultItem>();
        foreach (var pageId in pageIds)
        {
            try
            {
                await EnsureSocialChannelAccessAsync(pageId, ct);

                var entity = await CreateAsync(new CreateMediaFolderRequest
                {
                    Name = request.Name,
                    Description = request.Description,
                    SocialChannelId = pageId,
                    ParentFolderId = null,
                }, ct);

                results.Add(new CreateMediaFolderAcrossPagesResultItem
                {
                    SocialChannelId = pageId,
                    Success = true,
                    FolderId = entity.Id,
                });
            }
            catch (Exception ex)
            {
                results.Add(new CreateMediaFolderAcrossPagesResultItem
                {
                    SocialChannelId = pageId,
                    Success = false,
                    ErrorMessage = ex.Message,
                });
            }
        }

        return new CreateMediaFolderAcrossPagesResponse
        {
            TotalRequested = results.Count,
            TotalSucceeded = results.Count(r => r.Success),
            TotalFailed = results.Count(r => !r.Success),
            Results = results,
        };
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

    /// <summary>
    /// Tạo thư mục hàng loạt theo Page (SocialChannelId) và optional ParentFolderId (MEDIA-03).
    /// Hỗ trợ hierarchy thông qua clientRef/parentRef, preview/validate-only, cycle & duplicate check,
    /// giới hạn batch (200)/depth (10)/name (200), và transaction nguyên tử (rollback nếu có lỗi).
    /// </summary>
    public async Task<BulkCreateMediaFolderResponse> BulkCreateAsync(
        BulkCreateMediaFolderRequest request, CancellationToken ct = default)
    {
        if (request.SocialChannelId == Guid.Empty)
            throw new ArgumentException("SocialChannelId không được để trống.");

        await EnsureSocialChannelAccessAsync(request.SocialChannelId, ct);

        if (request.Folders == null || request.Folders.Count == 0)
            throw new ArgumentException("Danh sách thư mục không được để trống.");

        const int maxBatchSize = 200;
        if (request.Folders.Count > maxBatchSize)
            throw new ArgumentException($"Số lượng thư mục vượt quá giới hạn cho phép (tối đa {maxBatchSize}).");

        int baseDepth = 0;
        if (request.ParentFolderId.HasValue)
        {
            var baseParent = await QueryActive()
                .FirstOrDefaultAsync(x => x.Id == request.ParentFolderId.Value, ct);
            if (baseParent is null)
                throw new KeyNotFoundException("Thư mục cha gốc không tồn tại.");

            if (baseParent.SocialChannelId != request.SocialChannelId)
                throw new ArgumentException("Thư mục cha không thuộc Page yêu cầu.");

            baseDepth = 1;
            var cursor = baseParent.ParentFolderId;
            while (cursor.HasValue)
            {
                baseDepth++;
                var p = await QueryActive().FirstOrDefaultAsync(x => x.Id == cursor.Value, ct);
                if (p is null) break;
                cursor = p.ParentFolderId;
            }
        }

        var clientRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Folders)
        {
            if (string.IsNullOrWhiteSpace(item.ClientRef))
                throw new ArgumentException("clientRef không được để trống.");

            var trimmedRef = item.ClientRef.Trim();
            if (!clientRefs.Add(trimmedRef))
                throw new ArgumentException($"clientRef '{trimmedRef}' bị trùng lặp trong batch.");

            if (string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException($"Tên thư mục của node '{trimmedRef}' không được để trống.");

            if (item.Name.Trim().Length > 200)
                throw new ArgumentException($"Tên thư mục '{item.Name.Trim()}' vượt quá độ dài tối đa 200 ký tự.");

            if (!string.IsNullOrWhiteSpace(item.ParentRef))
            {
                var trimmedParentRef = item.ParentRef.Trim();
                if (string.Equals(trimmedRef, trimmedParentRef, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Thư mục '{trimmedRef}' không thể tự làm cha của chính nó.");
            }
        }

        foreach (var item in request.Folders)
        {
            if (!string.IsNullOrWhiteSpace(item.ParentRef))
            {
                var trimmedParentRef = item.ParentRef.Trim();
                if (!clientRefs.Contains(trimmedParentRef))
                    throw new ArgumentException($"parentRef '{trimmedParentRef}' của node '{item.ClientRef.Trim()}' không tồn tại trong batch.");
            }
        }

        var itemMap = request.Folders.ToDictionary(x => x.ClientRef.Trim(), x => x, StringComparer.OrdinalIgnoreCase);
        var childrenMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cRef in clientRefs)
        {
            childrenMap[cRef] = [];
            inDegree[cRef] = 0;
        }

        var topLevelNodes = new List<string>();
        foreach (var item in request.Folders)
        {
            var cRef = item.ClientRef.Trim();
            if (!string.IsNullOrWhiteSpace(item.ParentRef))
            {
                var pRef = item.ParentRef.Trim();
                childrenMap[pRef].Add(cRef);
                inDegree[cRef]++;
            }
            else
            {
                topLevelNodes.Add(cRef);
            }
        }

        var queue = new Queue<string>(topLevelNodes);
        var topoOrder = new List<string>();
        var depthMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var top in topLevelNodes)
        {
            depthMap[top] = baseDepth + 1;
        }

        const int maxDepth = 10;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            topoOrder.Add(current);

            var currDepth = depthMap[current];
            if (currDepth > maxDepth)
                throw new ArgumentException($"Độ sâu thư mục tại node '{current}' vượt quá giới hạn cho phép (tối đa {maxDepth} cấp).");

            foreach (var childRef in childrenMap[current])
            {
                depthMap[childRef] = currDepth + 1;
                inDegree[childRef]--;
                if (inDegree[childRef] == 0)
                {
                    queue.Enqueue(childRef);
                }
            }
        }

        if (topoOrder.Count < clientRefs.Count)
        {
            throw new ArgumentException("Phát hiện vòng lặp (cycle) trong cấu trúc thư mục của batch.");
        }

        var groupedByParent = request.Folders
            .GroupBy(x => x.ParentRef?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedByParent)
        {
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in group)
            {
                var name = item.Name.Trim();
                if (!seenNames.Add(name))
                {
                    if (request.DuplicatePolicy == BulkDuplicatePolicy.Error)
                    {
                        throw new ArgumentException($"Tên thư mục '{name}' bị trùng lặp dưới cùng thư mục cha trong batch.");
                    }
                }
            }
        }

        // ValidateOnly và create thật dùng chung lập kế hoạch hierarchy + duplicate DB
        // (Error/Skip) để preview phản ánh đúng clientRef/parentRef và không báo hợp lệ sai.
        if (request.ValidateOnly)
        {
            var preview = await MaterializeBulkBatchAsync(
                request,
                itemMap,
                topoOrder,
                depthMap,
                persist: false,
                ct);
            return BuildBulkResponse(request, preview.Items, preview.SkippedCount, validateOnly: true);
        }

        using var tx = await Context.Database.BeginTransactionAsync(ct);
        try
        {
            var created = await MaterializeBulkBatchAsync(
                request,
                itemMap,
                topoOrder,
                depthMap,
                persist: true,
                ct);
            await tx.CommitAsync(ct);
            return BuildBulkResponse(request, created.Items, created.SkippedCount, validateOnly: false);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private sealed record BulkBatchMaterialization(
        List<BulkCreatedMediaFolderItem> Items,
        int SkippedCount);

    private static BulkCreateMediaFolderResponse BuildBulkResponse(
        BulkCreateMediaFolderRequest request,
        List<BulkCreatedMediaFolderItem> items,
        int skippedCount,
        bool validateOnly)
        => new()
        {
            Success = true,
            ValidateOnly = validateOnly,
            TotalRequested = request.Folders.Count,
            TotalCreated = items.Count(x => !x.IsSkipped),
            TotalSkipped = skippedCount,
            Folders = items,
            Errors = []
        };

    /// <summary>
    /// Áp dụng cùng semantics hierarchy/duplicate cho preview và create.
    /// persist=false: không Add/SaveChanges; persist=true: ghi từng node trong transaction của caller.
    /// </summary>
    private async Task<BulkBatchMaterialization> MaterializeBulkBatchAsync(
        BulkCreateMediaFolderRequest request,
        IReadOnlyDictionary<string, BulkCreateMediaFolderItem> itemMap,
        IReadOnlyList<string> topoOrder,
        IReadOnlyDictionary<string, int> depthMap,
        bool persist,
        CancellationToken ct)
    {
        var responseItems = new List<BulkCreatedMediaFolderItem>();
        var idMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        // Overlay tên đã lập kế hoạch trong batch (cần cho ValidateOnly vì không ghi DB,
        // và giữ Skip in-batch nhất quán với create thật nhìn thấy entity vừa Add).
        var plannedNames = new Dictionary<(Guid? ParentId, string Name), (Guid Id, string DisplayName, Guid? ParentFolderId, Guid? SocialChannelId)>();
        var skippedCount = 0;

        foreach (var cRef in topoOrder)
        {
            var item = itemMap[cRef];
            var name = item.Name.Trim();
            var nameKey = name.ToLowerInvariant();

            Guid? actualParentId;
            if (!string.IsNullOrWhiteSpace(item.ParentRef))
            {
                var pRef = item.ParentRef.Trim();
                if (!idMap.TryGetValue(pRef, out var mappedParentId))
                    throw new ArgumentException($"parentRef '{pRef}' của node '{cRef}' chưa được resolve trong batch.");
                actualParentId = mappedParentId;
            }
            else
            {
                actualParentId = request.ParentFolderId;
            }

            var planKey = (actualParentId, nameKey);
            Guid? existingId = null;
            string? existingName = null;
            Guid? existingParentId = null;
            Guid? existingSocialChannelId = null;

            if (plannedNames.TryGetValue(planKey, out var planned))
            {
                existingId = planned.Id;
                existingName = planned.DisplayName;
                existingParentId = planned.ParentFolderId;
                existingSocialChannelId = planned.SocialChannelId;
            }
            else
            {
                var existingInDb = await QueryActive()
                    .FirstOrDefaultAsync(x =>
                        x.SocialChannelId == request.SocialChannelId &&
                        x.ParentFolderId == actualParentId &&
                        x.Name.ToLower() == nameKey, ct);
                if (existingInDb != null)
                {
                    existingId = existingInDb.Id;
                    existingName = existingInDb.Name;
                    existingParentId = existingInDb.ParentFolderId;
                    existingSocialChannelId = existingInDb.SocialChannelId;
                }
            }

            if (existingId.HasValue)
            {
                if (request.DuplicatePolicy == BulkDuplicatePolicy.Error)
                    throw new ArgumentException($"Thư mục '{name}' đã tồn tại dưới cùng thư mục cha.");

                if (request.DuplicatePolicy == BulkDuplicatePolicy.Skip)
                {
                    idMap[cRef] = existingId.Value;
                    skippedCount++;
                    responseItems.Add(new BulkCreatedMediaFolderItem
                    {
                        ClientRef = cRef,
                        Id = existingId.Value,
                        Name = existingName ?? name,
                        ParentFolderId = existingParentId,
                        SocialChannelId = existingSocialChannelId ?? request.SocialChannelId,
                        Depth = depthMap[cRef],
                        IsSkipped = true
                    });
                    continue;
                }
            }

            var entityId = Guid.NewGuid();
            if (persist)
            {
                var entity = new MediaFolderModel
                {
                    Id = entityId,
                    Name = name,
                    Description = item.Description?.Trim(),
                    ParentFolderId = actualParentId,
                    SocialChannelId = request.SocialChannelId,
                    SortOrder = item.SortOrder,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = GetCurrentUserName()
                };

                Context.Set<MediaFolderModel>().Add(entity);
                await Context.SaveChangesAsync(ct);
                entityId = entity.Id;
            }

            idMap[cRef] = entityId;
            plannedNames[planKey] = (entityId, name, actualParentId, request.SocialChannelId);
            responseItems.Add(new BulkCreatedMediaFolderItem
            {
                ClientRef = cRef,
                Id = entityId,
                Name = name,
                ParentFolderId = actualParentId,
                SocialChannelId = request.SocialChannelId,
                Depth = depthMap[cRef],
                IsSkipped = false
            });
        }

        return new BulkBatchMaterialization(responseItems, skippedCount);
    }

    private async Task EnsureSocialChannelAccessAsync(Guid socialChannelId, CancellationToken ct)
    {
        var roles = UserContext.GetCurrentUserRoles();
        if (roles.Any(role => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)))
        {
            var adminCanAccess = await Context.Set<SocialChannelModel>()
                .AnyAsync(x => x.Id == socialChannelId && !x.IsDeleted, ct);
            if (adminCanAccess) return;
        }
        else
        {
            var userName = UserContext.GetCurrentUserName()?.Trim();
            if (!string.IsNullOrWhiteSpace(userName))
            {
                var userCanAccess = await Context.Set<SocialChannelModel>()
                    .AnyAsync(x => x.Id == socialChannelId
                        && !x.IsDeleted
                        && (x.CreatedBy == userName
                            || (x.SocialConnectionId.HasValue
                                && Context.Set<SocialConnectionModel>().Any(c => c.Id == x.SocialConnectionId.Value
                                    && !c.IsDeleted
                                    && c.CreatedBy == userName))), ct);
                if (userCanAccess) return;
            }
        }

        throw new KeyNotFoundException("Page/Kênh không tồn tại.");
    }

    private async Task<(Dictionary<Guid, int> DirectAssets, Dictionary<Guid, int> ChildFolders)> LoadDirectCountsAsync(
        Guid socialChannelId, List<Guid> folderIds, CancellationToken ct)
    {
        var directAssetCounts = await Context.Set<MediaAssetModel>()
            .Where(x => !x.IsDeleted && x.FolderId.HasValue && folderIds.Contains(x.FolderId.Value))
            .GroupBy(x => x.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

        var childFolderCounts = await QueryActive()
            .Where(x => x.SocialChannelId == socialChannelId && x.ParentFolderId.HasValue && folderIds.Contains(x.ParentFolderId.Value))
            .GroupBy(x => x.ParentFolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

        return (directAssetCounts, childFolderCounts);
    }

    private static bool FolderNameMatchesKeyword(string name, string keyword)
    {
        if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedName = VietnameseTextHelper.StripDiacritics(name);
        var normalizedKeyword = VietnameseTextHelper.StripDiacritics(keyword);
        return normalizedName.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True khi mọi ancestor tới root đều thuộc map folder active của cùng Page.
    /// Parent thiếu, soft-deleted hoặc thuộc Page khác → false (đồng bộ breadcrumb not-found).
    /// </summary>
    private static bool HasIntactPageAncestorChain(
        MediaFolderModel folder,
        IReadOnlyDictionary<Guid, MediaFolderModel> foldersById)
    {
        var cursor = folder;
        var visited = new HashSet<Guid>();

        while (true)
        {
            if (!visited.Add(cursor.Id))
                return false;

            if (!cursor.ParentFolderId.HasValue)
                return true;

            if (!foldersById.TryGetValue(cursor.ParentFolderId.Value, out var parent))
                return false;

            cursor = parent;
        }
    }

    private static string BuildFullPath(Guid folderId, IReadOnlyDictionary<Guid, MediaFolderModel> foldersById)
    {
        if (!foldersById.TryGetValue(folderId, out var folder))
            return string.Empty;

        if (!HasIntactPageAncestorChain(folder, foldersById))
            return string.Empty;

        var segments = new List<string>();
        var cursor = folder;
        var visited = new HashSet<Guid>();

        while (true)
        {
            if (!visited.Add(cursor.Id))
                return string.Empty;

            segments.Add(cursor.Name);

            if (!cursor.ParentFolderId.HasValue)
                break;

            if (!foldersById.TryGetValue(cursor.ParentFolderId.Value, out var parent))
                return string.Empty;

            cursor = parent;
        }

        segments.Reverse();
        return string.Join(" / ", segments);
    }

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
