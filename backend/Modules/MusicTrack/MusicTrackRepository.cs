using Backend.Data;
using Backend.Shared;
using Backend.Shared.Repositories;
using Backend.Shared.Storage;

namespace Backend.Modules.MusicTrack;

public class MusicTrackRepository : GenericRepository<MusicTrackModel>
{
    public MusicTrackRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<MusicTrackModel> CreateFromUploadAsync(
        FileSaveResult saveResult, string? displayName, CancellationToken ct = default)
    {
        var entity = new MusicTrackModel
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? (saveResult.OriginalFileName ?? Path.GetFileName(saveResult.StorageKey))
                : displayName.Trim(),
            FileName = Path.GetFileName(saveResult.StorageKey),
            StoragePath = saveResult.StorageKey,
            MimeType = saveResult.ContentType,
            FileSize = saveResult.SizeBytes,
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<MusicTrackModel> CreateAsync(
        CreateMusicTrackRequest request, CancellationToken ct = default)
    {
        var entity = new MusicTrackModel
        {
            DisplayName = request.DisplayName.Trim(),
            FileName = request.FileName.Trim(),
            StoragePath = request.StoragePath.Trim(),
            MimeType = request.MimeType.Trim(),
            FileSize = request.FileSize,
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<MusicTrackModel?> UpdateAsync(
        Guid id, UpdateMusicTrackRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            entity.DisplayName = request.DisplayName.Trim();

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public static MusicTrackResponse ToResponse(MusicTrackModel e) => new()
    {
        Id = e.Id,
        DisplayName = e.DisplayName,
        FileName = e.FileName,
        PreviewUrl = MusicTrackUrls.Preview(e.Id),
        MimeType = e.MimeType,
        FileSize = e.FileSize,
        CreatedAt = e.CreatedAt,
    };
}
