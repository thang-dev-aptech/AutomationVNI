using System.Security.Cryptography;
using Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.ShortLink;

public class ShortLinkOptions
{
    /// <summary>
    /// Tên miền CÔNG KHAI đặt trước mã, vd "https://api.domaincuanh.com".
    /// Bỏ trống thì rút gọn bị tắt — link localhost người dùng Facebook bấm vào không ra gì,
    /// thà giữ nguyên URL gốc còn hơn đăng một link chết.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
    /// <summary>Đường dẫn gốc, ghép thành {PublicBaseUrl}/{Path}/{code}.</summary>
    public string Path { get; set; } = "s";
    public int CodeLength { get; set; } = 6;
}

public class ShortLinkService(
    AppDbContext context,
    IOptions<ShortLinkOptions> options,
    ILogger<ShortLinkService> logger)
{
    // Bỏ 0/O/1/l/I để người đọc không gõ nhầm khi chép tay.
    private const string Alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl);

    /// <summary>
    /// Trả về link rút gọn, hoặc chính URL gốc nếu chưa cấu hình tên miền công khai.
    /// KHÔNG ném lỗi: link chỉ là phần phụ, hỏng thì bài vẫn phải đăng được.
    /// </summary>
    public async Task<string> ShortenAsync(string targetUrl, Guid? postId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetUrl)) return targetUrl;
        if (!IsConfigured) return targetUrl;

        try
        {
            var url = targetUrl.Trim();
            var existing = await context.Set<ShortLinkModel>()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.TargetUrl == url, ct);
            if (existing is not null) return Build(existing.Code);

            var code = await GenerateUniqueCodeAsync(ct);
            context.Set<ShortLinkModel>().Add(new ShortLinkModel
            {
                Id = Guid.NewGuid(),
                Code = code,
                TargetUrl = url,
                PostId = postId,
                CreatedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(ct);
            return Build(code);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rút gọn link thất bại, dùng URL gốc: {Url}", targetUrl);
            return targetUrl;
        }
    }

    public async Task<ShortLinkModel?> ResolveAsync(string code, CancellationToken ct = default)
    {
        var link = await context.Set<ShortLinkModel>()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Code == code, ct);
        if (link is null) return null;

        link.ClickCount++;
        link.LastClickedAt = DateTime.UtcNow;
        try { await context.SaveChangesAsync(ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Không ghi được lượt bấm cho {Code}", code); }
        return link;
    }

    public string Build(string code)
        => $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{options.Value.Path.Trim('/')}/{code}";

    /// <summary>Độ dài link rút gọn — dùng để tính ngân sách 130 ký tự của Facebook.</summary>
    public int EstimateLength() => IsConfigured ? Build(new string('x', options.Value.CodeLength)).Length : 0;

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        var length = Math.Clamp(options.Value.CodeLength, 4, 12);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = Random(length);
            if (!await context.Set<ShortLinkModel>().AnyAsync(x => x.Code == code, ct))
                return code;
        }
        // Cực hiếm: 8 lần đều đụng. Nới thêm ký tự cho chắc chắn thay vì ném lỗi.
        return Random(length + 2);
    }

    private static string Random(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
