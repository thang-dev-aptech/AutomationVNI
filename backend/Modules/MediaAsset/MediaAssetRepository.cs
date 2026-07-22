using System.Text.Json;
using Backend.Data;
using Backend.Modules.MediaAsset.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Backend.Shared.Storage;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.MediaAsset;

public class MediaAssetRepository : GenericRepository<MediaAssetModel>
{
    public MediaAssetRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    /// <summary>Chuẩn hoá danh sách loại bài → JSON array Guid (null nếu rỗng, để coi là "dùng chung").</summary>
    public static string? SerializeCategoryIds(IEnumerable<Guid>? ids)
    {
        var list = (ids ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }

    /// <summary>Đọc JSON array Guid từ cột CategoryIds; hỏng/null → rỗng.</summary>
    public static List<Guid> ParseCategoryIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task<PagedResult<MediaAssetResponse>> FilterAsync(
        MediaAssetFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x =>
                x.FileName.Contains(kw) ||
                (x.OriginalFileName != null && x.OriginalFileName.Contains(kw)) ||
                (x.AltText != null && x.AltText.Contains(kw)) ||
                (x.Tags != null && x.Tags.Contains(kw)));
        }

        if (request.Source.HasValue)
            query = query.Where(x => x.Source == request.Source.Value);

        if (request.CategoryId.HasValue)
            query = query.Where(x => x.CategoryId == request.CategoryId.Value);

        if (request.AppliesToCategoryId is Guid appliesTo && appliesTo != Guid.Empty)
        {
            // Ảnh áp dụng loại bài này (CategoryIds JSON chứa guid). So khớp chuỗi guid "D" (không dấu ngoặc).
            var needle = appliesTo.ToString();
            query = query.Where(x => x.CategoryIds != null && x.CategoryIds.Contains(needle));
        }

        if (request.Unassigned == true)
            query = query.Where(x => x.FolderId == null);
        else if (request.FolderId.HasValue)
            query = query.Where(x => x.FolderId == request.FolderId.Value);

        if (!string.IsNullOrWhiteSpace(request.MimeType))
            query = query.Where(x => x.MimeType.StartsWith(request.MimeType.Trim()));

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<MediaAssetResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<MediaAssetModel> CreateFromUploadAsync(
        FileSaveResult saveResult, Guid? categoryId = null, string? altText = null,
        Guid? folderId = null, IEnumerable<Guid>? categoryIds = null, CancellationToken ct = default)
    {
        var entity = new MediaAssetModel
        {
            FileName = Path.GetFileName(saveResult.StorageKey),
            OriginalFileName = saveResult.OriginalFileName,
            StoragePath = saveResult.StorageKey,
            MimeType = saveResult.ContentType,
            FileSize = saveResult.SizeBytes,
            Source = MediaSource.Upload,
            CategoryId = categoryId,
            FolderId = folderId,
            CategoryIds = SerializeCategoryIds(categoryIds),
            AltText = altText?.Trim()
        };

        entity = await base.CreateAsync(entity, ct);
        entity.PublicUrl = MediaAssetUrls.Preview(entity.Id);
        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<MediaAssetModel> CreateAsync(
        CreateMediaAssetRequest request, CancellationToken ct = default)
    {
        var entity = new MediaAssetModel
        {
            FileName = request.FileName.Trim(),
            OriginalFileName = request.OriginalFileName?.Trim(),
            StoragePath = request.StoragePath.Trim(),
            PublicUrl = request.PublicUrl?.Trim(),
            MimeType = request.MimeType.Trim(),
            FileSize = request.FileSize,
            Source = request.Source,
            CategoryId = request.CategoryId,
            FolderId = request.FolderId,
            CategoryIds = SerializeCategoryIds(request.CategoryIds),
            AltText = request.AltText?.Trim(),
            Description = request.Description?.Trim(),
            Tags = request.Tags,
            Width = request.Width,
            Height = request.Height
        };

        return await base.CreateAsync(entity, ct);
    }

    /// <summary>Kéo-thả: chuyển nhiều ảnh vào 1 thư mục (null = "Chưa phân loại"). Trả số ảnh đã chuyển.</summary>
    public async Task<int> MoveAsync(
        IEnumerable<Guid> ids, Guid? folderId, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return 0;

        if (folderId.HasValue)
        {
            var folderExists = await Context.Set<Backend.Modules.MediaFolder.MediaFolderModel>()
                .AnyAsync(x => x.Id == folderId.Value && !x.IsDeleted, ct);
            if (!folderExists)
                throw new InvalidOperationException("Thư mục đích không tồn tại.");
        }

        var assets = await QueryActive().Where(x => idList.Contains(x.Id)).ToListAsync(ct);
        foreach (var a in assets)
        {
            a.FolderId = folderId;
            ApplyUpdateAudit(a);
        }
        await Context.SaveChangesAsync(ct);
        return assets.Count;
    }

    public async Task SetPreviewUrlAsync(MediaAssetModel entity, CancellationToken ct = default)
    {
        entity.PublicUrl = MediaAssetUrls.Preview(entity.Id);
        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
    }

    public async Task<MediaAssetModel?> UpdateAsync(
        Guid id, UpdateMediaAssetRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.AltText is not null) entity.AltText = request.AltText.Trim();
        if (request.Description is not null) entity.Description = request.Description.Trim();
        if (request.Tags is not null) entity.Tags = request.Tags;
        if (request.PublicUrl is not null) entity.PublicUrl = request.PublicUrl.Trim();
        if (request.CategoryId.HasValue) entity.CategoryId = request.CategoryId;
        if (request.CategoryIds is not null) entity.CategoryIds = SerializeCategoryIds(request.CategoryIds);

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public static MediaAssetResponse ToResponse(MediaAssetModel e) => new()
    {
        Id = e.Id,
        FileName = e.FileName,
        OriginalFileName = e.OriginalFileName,
        PublicUrl = e.PublicUrl ?? MediaAssetUrls.Preview(e.Id),
        PreviewUrl = MediaAssetUrls.Preview(e.Id),
        DownloadUrl = MediaAssetUrls.Download(e.Id),
        MimeType = e.MimeType,
        FileSize = e.FileSize,
        Source = e.Source,
        CategoryId = e.CategoryId,
        FolderId = e.FolderId,
        CategoryIds = ParseCategoryIds(e.CategoryIds),
        AltText = e.AltText,
        Description = e.Description,
        Tags = e.Tags,
        Keywords = MediaIntelligenceService.ParseKeywords(e.Tags),
        Width = e.Width,
        Height = e.Height,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}

public class PostMediaRepository : GenericRepository<PostMediaModel>
{
    public PostMediaRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<PostMediaResponse>> FilterAsync(
        PostMediaFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (request.PostId.HasValue)
            query = query.Where(x => x.PostId == request.PostId.Value);

        if (request.MediaId.HasValue)
            query = query.Where(x => x.MediaId == request.MediaId.Value);

        if (request.MediaRole.HasValue)
            query = query.Where(x => x.MediaRole == request.MediaRole.Value);

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<PostMediaResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<List<PostMediaModel>> GetByPostAsync(Guid postId, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.PostId == postId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

    public async Task<PostMediaModel> CreateAsync(
        CreatePostMediaRequest request, CancellationToken ct = default)
    {
        var entity = new PostMediaModel
        {
            PostId = request.PostId,
            MediaId = request.MediaId,
            MediaRole = request.MediaRole,
            SortOrder = request.SortOrder
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<PostMediaModel?> UpdateAsync(
        Guid id, UpdatePostMediaRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.MediaRole.HasValue) entity.MediaRole = request.MediaRole.Value;
        if (request.SortOrder.HasValue) entity.SortOrder = request.SortOrder.Value;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<PostMediaModel> ReplaceCoverAsync(
        Guid postId, Guid newMediaId, CancellationToken ct = default)
    {
        var existing = await QueryActive()
            .Where(x => x.PostId == postId && x.MediaRole == MediaRole.Cover)
            .OrderBy(x => x.SortOrder)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            existing.MediaId = newMediaId;
            existing.SortOrder = 0;
            ApplyUpdateAudit(existing);
            await Context.SaveChangesAsync(ct);
            return existing;
        }

        return await CreateAsync(new CreatePostMediaRequest
        {
            PostId = postId,
            MediaId = newMediaId,
            MediaRole = MediaRole.Cover,
            SortOrder = 0
        }, ct);
    }

    public static PostMediaResponse ToResponse(PostMediaModel e) => new()
    {
        Id = e.Id,
        PostId = e.PostId,
        MediaId = e.MediaId,
        MediaRole = e.MediaRole,
        SortOrder = e.SortOrder,
        CreatedAt = e.CreatedAt
    };
}
