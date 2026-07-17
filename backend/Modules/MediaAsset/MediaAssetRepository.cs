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
        FileSaveResult saveResult, Guid? categoryId = null, string? altText = null, CancellationToken ct = default)
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
            AltText = request.AltText?.Trim(),
            Description = request.Description?.Trim(),
            Tags = request.Tags,
            Width = request.Width,
            Height = request.Height
        };

        return await base.CreateAsync(entity, ct);
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
        AltText = e.AltText,
        Description = e.Description,
        Tags = e.Tags,
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
