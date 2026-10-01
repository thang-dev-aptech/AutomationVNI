using Backend.Modules.MediaCaption;
using System.Text.Json;
using Backend.Data;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.MediaFolder;
using Backend.Shared;
using Backend.Shared.Repositories;
using Backend.Shared.Storage;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.MediaAsset;

public class MediaAssetRepository : GenericRepository<MediaAssetModel>
{
    private readonly MediaFolderRepository _folders;
    private readonly IFileStorageService _fileStorage;

    public MediaAssetRepository(AppDbContext context, IUserContext userContext, IFileStorageService fileStorage)
        : this(context, userContext, new MediaFolderRepository(context, userContext), fileStorage)
    {
    }

    public MediaAssetRepository(
        AppDbContext context,
        IUserContext userContext,
        MediaFolderRepository folders,
        IFileStorageService fileStorage)
        : base(context, userContext)
    {
        _folders = folders;
        _fileStorage = fileStorage;
    }

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

        // Join Page (SocialChannelId) qua FolderId theo lô — cần cho điều hướng "mở đúng Page"
        // từ kết quả tìm kiếm toàn cục, không round-trip DB cho từng ảnh.
        var folderIds = paged.Items
            .Where(x => x.FolderId.HasValue)
            .Select(x => x.FolderId!.Value)
            .Distinct()
            .ToList();
        var folderChannelMap = folderIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await Context.Set<MediaFolderModel>()
                .Where(f => folderIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.SocialChannelId, ct);

        var queuedIds = await MediaCaptionJobService.GetQueuedAssetIdsAsync(
            Context, paged.Items.Select(x => x.Id), ct);

        return new PagedResult<MediaAssetResponse>
        {
            Items = paged.Items
                .Select(x => ToResponse(
                    x,
                    x.FolderId.HasValue ? folderChannelMap.GetValueOrDefault(x.FolderId.Value) : null,
                    queuedIds.Contains(x.Id)))
                .ToList(),
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

    /// <summary>Khoá idempotency cho nhập file Google Drive (GDRIVE-01) — chặn import trùng cùng một file.</summary>
    public async Task<bool> ExistsByGoogleDriveFileIdAsync(string fileId, CancellationToken ct = default)
        => await Context.Set<MediaAssetModel>()
            .AnyAsync(x => !x.IsDeleted && x.GoogleDriveFileId == fileId, ct);

    public async Task<MediaAssetModel?> FindByGoogleDriveFileIdAsync(
        string fileId, CancellationToken ct = default)
        => await Context.Set<MediaAssetModel>()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.GoogleDriveFileId == fileId, ct);

    /// <summary>
    /// Nhập file từ Google Drive. FolderId = MediaFolder đã map theo cha Drive thật
    /// (GDRIVE-05) — caller tra bản đồ ánh xạ rồi truyền vào.
    /// OriginalFileName lấy từ <paramref name="file"/>.Name (tên thật trên Drive).
    /// </summary>
    public async Task<MediaAssetModel> CreateFromGoogleDriveAsync(
        byte[] data, GoogleDriveFileInfo file, Guid folderId, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(file.Name);
        var saveResult = await _fileStorage.SaveBytesAsync(data, "google-drive", extension, file.MimeType, ct);

        var entity = new MediaAssetModel
        {
            FileName = Path.GetFileName(saveResult.StorageKey),
            OriginalFileName = file.Name,
            StoragePath = saveResult.StorageKey,
            MimeType = saveResult.ContentType,
            FileSize = saveResult.SizeBytes,
            Source = MediaSource.GoogleDrive,
            FolderId = folderId,
            GoogleDriveFileId = file.FileId,
        };

        entity = await base.CreateAsync(entity, ct);
        entity.PublicUrl = MediaAssetUrls.Preview(entity.Id);
        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>
    /// GDRIVE-05: cập nhật FolderId / OriginalFileName khi file đã import đổi cha hoặc tên trên Drive.
    /// </summary>
    public async Task UpdateGoogleDrivePlacementAsync(
        MediaAssetModel entity, Guid folderId, string originalFileName, CancellationToken ct = default)
    {
        var changed = false;
        if (entity.FolderId != folderId)
        {
            entity.FolderId = folderId;
            changed = true;
        }

        var name = originalFileName?.Trim() ?? string.Empty;
        if (!string.Equals(entity.OriginalFileName, name, StringComparison.Ordinal))
        {
            entity.OriginalFileName = name;
            changed = true;
        }

        if (!changed) return;
        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
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
            await EnsureDestinationFolderWritableAsync(folderId.Value, ct);

        var assets = await QueryActive().Where(x => idList.Contains(x.Id)).ToListAsync(ct);
        foreach (var a in assets)
        {
            a.FolderId = folderId;
            ApplyUpdateAudit(a);
        }
        await Context.SaveChangesAsync(ct);
        return assets.Count;
    }

    /// <summary>
    /// Folder đích phải tồn tại; nếu gắn Page thì Page phải nằm trong QueryWritableChannels
    /// (qua GetWritablePagesAsync). Channel ngoài quyền trả cùng thông báo không lộ metadata.
    /// Folder chưa gắn Page giữ hành vi cũ — chỉ kiểm tra tồn tại — để không phá regression move.
    /// </summary>
    private async Task EnsureDestinationFolderWritableAsync(Guid folderId, CancellationToken ct)
    {
        var folder = await Context.Set<MediaFolderModel>()
            .FirstOrDefaultAsync(x => x.Id == folderId && !x.IsDeleted, ct);
        if (folder is null)
            throw new InvalidOperationException("Thư mục đích không tồn tại.");

        if (folder.SocialChannelId is not Guid channelId)
            return;

        var canWrite = (await _folders.GetWritablePagesAsync(ct: ct)).Any(p => p.Id == channelId);
        if (!canWrite)
            throw new KeyNotFoundException("Page/Kênh không tồn tại.");
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

        // Khoá caption TRƯỚC khi áp bất kỳ field nào: cả request bị từ chối, không ghi nửa chừng.
        if (request.Caption is not null && await IsCaptionQueuedAsync(id, ct))
            throw new CaptionQueuedException();

        if (request.AltText is not null) entity.AltText = request.AltText.Trim();
        if (request.Description is not null) entity.Description = request.Description.Trim();
        if (request.Tags is not null) entity.Tags = request.Tags;
        if (request.PublicUrl is not null) entity.PublicUrl = request.PublicUrl.Trim();
        if (request.Caption is not null) entity.Caption = request.Caption.Trim();
        if (request.CategoryId.HasValue) entity.CategoryId = request.CategoryId;
        if (request.CategoryIds is not null) entity.CategoryIds = SerializeCategoryIds(request.CategoryIds);

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public Task<bool> IsCaptionQueuedAsync(Guid id, CancellationToken ct = default)
        => MediaCaptionJobService.IsAssetQueuedAsync(Context, id, ct);

    public static MediaAssetResponse ToResponse(
        MediaAssetModel e, Guid? socialChannelId = null, bool captionQueued = false) => new()
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
        SocialChannelId = socialChannelId,
        CategoryIds = ParseCategoryIds(e.CategoryIds),
        AltText = e.AltText,
        Description = e.Description,
        Tags = e.Tags,
        Keywords = MediaIntelligenceService.ParseKeywords(e.Tags),
        Caption = e.Caption,
        CaptionQueued = captionQueued,
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
            Items = await AttachAssetsAsync(paged.Items, ct),
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

    /// <summary>
    /// Link + thông tin ảnh trong 1 lần gọi. Client render thẳng từ đây, không phụ thuộc
    /// danh sách media toàn cục (thứ hay cache cũ ngay sau khi AI sinh ảnh).
    /// </summary>
    public async Task<List<PostMediaResponse>> GetByPostWithAssetsAsync(
        Guid postId, CancellationToken ct = default)
        => await AttachAssetsAsync(await GetByPostAsync(postId, ct), ct);

    private async Task<List<PostMediaResponse>> AttachAssetsAsync(
        IReadOnlyList<PostMediaModel> links, CancellationToken ct)
    {
        var responses = links.Select(ToResponse).ToList();
        if (responses.Count == 0) return responses;

        var mediaIds = responses.Select(r => r.MediaId).Distinct().ToList();
        var assets = await Context.Set<MediaAssetModel>()
            .AsNoTracking()
            .Where(a => mediaIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var response in responses)
        {
            if (!assets.TryGetValue(response.MediaId, out var asset)) continue;
            response.PublicUrl = asset.PublicUrl ?? MediaAssetUrls.Preview(asset.Id);
            response.PreviewUrl = MediaAssetUrls.Preview(asset.Id);
            response.MimeType = asset.MimeType;
            response.FileName = asset.FileName;
            response.OriginalFileName = asset.OriginalFileName;
            response.AltText = asset.AltText;
        }

        return responses;
    }

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

    /// <summary>
    /// Xoá mềm toàn bộ PostMedia của post theo các MediaRole chỉ định — dùng khi ReelsRender ghép
    /// xong video: N khung hình Attachment/Cover cũ không còn cần nữa, publish Reels cần đúng 1
    /// media item (video) để FacebookPagePublishService route đúng nhánh, không lẫn với multi-photo.
    /// </summary>
    public async Task SoftDeleteAllForPostAsync(
        Guid postId, IReadOnlyCollection<MediaRole> roles, CancellationToken ct = default)
    {
        var rows = await QueryActive()
            .Where(x => x.PostId == postId && roles.Contains(x.MediaRole))
            .ToListAsync(ct);
        foreach (var row in rows)
            ApplySoftDeleteAudit(row);
        if (rows.Count > 0)
            await Context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ghi đè toàn bộ ảnh nguồn (MediaRole.TemplateSource) của 1 post — dùng cho Template nhiều ảnh
    /// và Reels (AI chọn tự động hoặc người dùng chỉnh tay đều gọi qua đây). Xoá mềm hàng cũ, tạo
    /// hàng mới theo đúng thứ tự mediaIds (SortOrder = vị trí trong mảng = thứ tự khung hình).
    /// </summary>
    public async Task<List<PostMediaModel>> ReplaceTemplateSourcesAsync(
        Guid postId, List<Guid> mediaIds, CancellationToken ct = default)
    {
        var existing = await QueryActive()
            .Where(x => x.PostId == postId && x.MediaRole == MediaRole.TemplateSource)
            .ToListAsync(ct);
        foreach (var row in existing)
            ApplySoftDeleteAudit(row);
        if (existing.Count > 0)
            await Context.SaveChangesAsync(ct);

        var created = new List<PostMediaModel>();
        for (var i = 0; i < mediaIds.Count; i++)
        {
            created.Add(await CreateAsync(new CreatePostMediaRequest
            {
                PostId = postId,
                MediaId = mediaIds[i],
                MediaRole = MediaRole.TemplateSource,
                SortOrder = i
            }, ct));
        }

        return created;
    }

    /// <summary>
    /// Ghi đè gallery Cover + Attachment của 1 post — dùng cho ChungChiGallery (ảnh cuối/as-is).
    /// Ảnh đầu = Cover, các ảnh sau = Attachment. Không dùng MediaRole.TemplateSource (đó là nguồn
    /// overlay của Template). Xoá mềm Cover/Attachment cũ rồi tạo lại theo thứ tự mediaIds.
    /// </summary>
    public async Task<List<PostMediaModel>> ReplaceGalleryAsync(
        Guid postId, List<Guid> mediaIds, CancellationToken ct = default)
    {
        var existing = await QueryActive()
            .Where(x => x.PostId == postId
                && (x.MediaRole == MediaRole.Cover || x.MediaRole == MediaRole.Attachment))
            .ToListAsync(ct);
        foreach (var row in existing)
            ApplySoftDeleteAudit(row);
        if (existing.Count > 0)
            await Context.SaveChangesAsync(ct);

        var created = new List<PostMediaModel>();
        for (var i = 0; i < mediaIds.Count; i++)
        {
            created.Add(await CreateAsync(new CreatePostMediaRequest
            {
                PostId = postId,
                MediaId = mediaIds[i],
                MediaRole = i == 0 ? MediaRole.Cover : MediaRole.Attachment,
                SortOrder = i
            }, ct));
        }

        return created;
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
