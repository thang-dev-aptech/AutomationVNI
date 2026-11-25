using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Campaign.FullMedia;

public class MediaVisionService(
    AppDbContext db,
    IFileStorageService storage,
    MediaIntelligenceService intelligence,
    IOptions<FullMediaPipelineOptions> options,
    ILogger<MediaVisionService> logger) : IMediaVisionDescriber
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal const string SystemPrompt =
        "Bạn là trợ lý mô tả ảnh cho chiến dịch mạng xã hội Việt Nam. "
        + "CHỈ trả JSON đúng schema, mọi chuỗi bằng tiếng Việt, không markdown.";

    internal const string UserPrompt =
        "Mô tả ảnh theo JSON: "
        + "{\"topic\":\"chủ đề\",\"audience\":\"đối tượng\",\"tone\":\"tông cảm xúc\","
        + "\"setting\":\"bối cảnh\",\"textInImage\":\"chữ đọc được trong ảnh hoặc rỗng\","
        + "\"keywords\":[\"3-8 từ khoá ngắn\"],\"confidence\":0.0,\"unclear\":false}. "
        + "confidence từ 0 đến 1. unclear=true nếu ảnh mờ/không hiểu nội dung.";

    public async Task<VisionDescription> DescribeAsync(
        Guid mediaAssetId, VisionPass pass, CancellationToken ct = default)
    {
        EnsurePassModelsIndependentIfEnabled(pass);

        var media = await LoadMediaAsync(mediaAssetId, ct);
        // Luôn đọc + băm nội dung ảnh (kể cả khi có cache) để phát hiện FileHash đổi.
        // An toàn nhờ MaxImageBytes; chấp nhận chi phí I/O để cache không dùng nhầm nội dung cũ.
        var (bytes, mime) = await ReadImageBytesAsync(media, ct);
        var fileHash = ComputeFileHash(bytes);

        var cached = await FindCacheAsync(mediaAssetId, fileHash, pass, ct);
        if (cached is not null)
            return FromProfile(cached);

        var model = ResolvePassModel(pass);
        return await CallAndCacheAsync(mediaAssetId, fileHash, pass, bytes, mime, model, ct);
    }

    public async Task<VisionOutcome> GetUsableAsync(
        Guid mediaAssetId, VisionPass pass, CancellationToken ct = default)
    {
        MediaAssetModel media;
        VisionDescription description;
        try
        {
            media = await LoadMediaAsync(mediaAssetId, ct);
            description = await DescribeAsync(mediaAssetId, pass, ct);
        }
        catch (VisionImageRejectedException ex)
        {
            return new VisionOutcome(VisionUsability.Unusable, null, ex.ReasonCode);
        }
        catch (VisionConfigurationException ex)
        {
            logger.LogWarning(ex, "Vision configuration rejected for media {MediaId}: {Reason}",
                mediaAssetId, ex.ReasonCode);
            return new VisionOutcome(VisionUsability.Failed, null, ex.ReasonCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Vision describe failed for media {MediaId}", mediaAssetId);
            return new VisionOutcome(VisionUsability.Failed, null, "describe_failed");
        }

        var opts = options.Value;
        var attemptsLeft = Math.Max(0, opts.MaxVisionFallbackAttempts);
        var fallbackModel = opts.VisionFallbackModel?.Trim();

        while (true)
        {
            var needsRetry = description.Unclear
                || description.Confidence < opts.MinVisionConfidence
                || !TagConsistency.IsConsistent(description, media);

            if (!needsRetry)
                return new VisionOutcome(VisionUsability.Usable, description);

            // F1(a): mô tả đã lưu chính là kết quả VisionFallbackModel → không gọi AI dự phòng lại.
            var alreadyUsedFallback = !string.IsNullOrWhiteSpace(fallbackModel)
                && string.Equals(description.Model, fallbackModel, StringComparison.Ordinal);

            if (attemptsLeft <= 0
                || string.IsNullOrWhiteSpace(fallbackModel)
                || alreadyUsedFallback)
            {
                var reason = description.Unclear || description.Confidence < opts.MinVisionConfidence
                    ? "unclear_or_low_confidence"
                    : "tag_contradiction";
                return new VisionOutcome(VisionUsability.Unusable, description, reason);
            }

            attemptsLeft--;
            try
            {
                var (bytes, mime) = await ReadImageBytesAsync(media, ct);
                var fileHash = ComputeFileHash(bytes);
                description = await CallAndCacheAsync(
                    mediaAssetId, fileHash, pass, bytes, mime, fallbackModel, ct);
            }
            catch (VisionImageRejectedException ex)
            {
                return new VisionOutcome(VisionUsability.Unusable, description, ex.ReasonCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Vision fallback failed for media {MediaId}", mediaAssetId);
                return new VisionOutcome(VisionUsability.Failed, description, "fallback_failed");
            }
        }
    }

    private void EnsurePassModelsIndependentIfEnabled(VisionPass pass)
    {
        var opts = options.Value;
        if (!opts.Enabled || pass != VisionPass.B) return;

        var effectiveA = intelligence.ResolveVisionModelName(ResolvePassModel(VisionPass.A));
        var effectiveB = intelligence.ResolveVisionModelName(ResolvePassModel(VisionPass.B));
        if (string.Equals(effectiveA, effectiveB, StringComparison.Ordinal))
        {
            throw new VisionConfigurationException(
                "vision_models_not_independent",
                $"VisionModel A và B phải khác nhau khi FullMediaPipeline.Enabled=true (hiện cùng '{effectiveA}').");
        }
    }

    private async Task<VisionDescription> CallAndCacheAsync(
        Guid mediaAssetId,
        string fileHash,
        VisionPass pass,
        byte[] bytes,
        string mime,
        string? model,
        CancellationToken ct)
    {
        var content = await intelligence.CallStructuredVisionAsync(
            bytes, mime, SystemPrompt, UserPrompt, model, ct);
        var description = ParseDescription(content, model ?? string.Empty);
        await UpsertCacheAsync(mediaAssetId, fileHash, pass, description, ct);
        return description;
    }

    private string? ResolvePassModel(VisionPass pass)
    {
        var opts = options.Value;
        return pass == VisionPass.B
            ? FirstNonEmpty(opts.VisionModelB, opts.VisionModelA)
            : FirstNonEmpty(opts.VisionModelA, opts.VisionModelB);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private async Task<MediaAssetModel> LoadMediaAsync(Guid mediaAssetId, CancellationToken ct)
        => await db.MediaAssets.AsNoTracking()
               .FirstOrDefaultAsync(x => x.Id == mediaAssetId && !x.IsDeleted, ct)
           ?? throw new KeyNotFoundException("Không tìm thấy media");

    private async Task<(byte[] Bytes, string Mime)> ReadImageBytesAsync(
        MediaAssetModel media, CancellationToken ct)
    {
        if (!media.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new VisionImageRejectedException("not_an_image", "Vision chỉ hỗ trợ file ảnh");
        if (string.IsNullOrWhiteSpace(media.StoragePath) || !await storage.ExistsAsync(media.StoragePath, ct))
            throw new ArgumentException("File ảnh không tồn tại trên storage");

        var maxBytes = options.Value.MaxImageBytes;
        await using var stream = await storage.OpenReadAsync(media.StoragePath, ct);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new VisionImageRejectedException(
                    "image_too_large", $"Ảnh vượt giới hạn {maxBytes} bytes");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return (memory.ToArray(), media.MimeType);
    }

    private Task<MediaVisionProfileModel?> FindCacheAsync(
        Guid mediaAssetId, string fileHash, VisionPass pass, CancellationToken ct)
        => db.MediaVisionProfiles.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.MediaAssetId == mediaAssetId
                     && x.FileHash == fileHash
                     && x.Pass == pass
                     && !x.IsDeleted,
                ct);

    private async Task UpsertCacheAsync(
        Guid mediaAssetId,
        string fileHash,
        VisionPass pass,
        VisionDescription description,
        CancellationToken ct)
    {
        var existing = await db.MediaVisionProfiles
            .FirstOrDefaultAsync(
                x => x.MediaAssetId == mediaAssetId
                     && x.FileHash == fileHash
                     && x.Pass == pass
                     && !x.IsDeleted,
                ct);

        var json = JsonSerializer.Serialize(description, JsonOptions);
        if (existing is null)
        {
            db.MediaVisionProfiles.Add(new MediaVisionProfileModel
            {
                Id = Guid.NewGuid(),
                MediaAssetId = mediaAssetId,
                FileHash = fileHash,
                Pass = pass,
                DescriptionJson = json,
                Confidence = description.Confidence,
                Unclear = description.Unclear,
                Model = description.Model,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.DescriptionJson = json;
            existing.Confidence = description.Confidence;
            existing.Unclear = description.Unclear;
            existing.Model = description.Model;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private static VisionDescription FromProfile(MediaVisionProfileModel profile)
    {
        var parsed = JsonSerializer.Deserialize<VisionDescriptionDto>(profile.DescriptionJson, JsonOptions)
                     ?? throw new InvalidOperationException("Cache mô tả Vision không hợp lệ");
        return parsed.ToDescription(profile.Model);
    }

    private static VisionDescription ParseDescription(string content, string model)
    {
        var text = StripJsonFence(content);
        var parsed = JsonSerializer.Deserialize<VisionDescriptionDto>(text, JsonOptions)
                     ?? throw new InvalidOperationException("AI không trả mô tả Vision hợp lệ");
        return parsed.ToDescription(model);
    }

    internal static string ComputeFileHash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string StripJsonFence(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith("```")) return text;
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^```(?:json)?\s*", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*```$", "");
        return text.Trim();
    }

    private sealed class VisionDescriptionDto
    {
        public string? Topic { get; set; }
        public string? Audience { get; set; }
        public string? Tone { get; set; }
        public string? Setting { get; set; }
        public string? TextInImage { get; set; }
        public List<string>? Keywords { get; set; }
        public double Confidence { get; set; }
        public bool Unclear { get; set; }

        public VisionDescription ToDescription(string model) => new(
            Topic: Topic?.Trim() ?? string.Empty,
            Audience: Audience?.Trim() ?? string.Empty,
            Tone: Tone?.Trim() ?? string.Empty,
            Setting: Setting?.Trim() ?? string.Empty,
            TextInImage: TextInImage?.Trim() ?? string.Empty,
            Keywords: (Keywords ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList(),
            Confidence: Confidence,
            Unclear: Unclear,
            Model: model);
    }
}
