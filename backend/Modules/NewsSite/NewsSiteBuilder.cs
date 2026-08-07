using Backend.Modules.ContentCrawl;
using Microsoft.Extensions.Options;

namespace Backend.Modules.NewsSite;

public sealed record BuildResult(int Articles, int Pages, string OutputPath, string? Error);

/// <summary>
/// Sinh toàn bộ trang tĩnh rồi ghi ra thư mục nginx phục vụ.
///
/// Build LẠI TOÀN BỘ mỗi lượt thay vì build tăng dần: site chỉ vài chục file, dựng hết mất
/// dưới một giây, và luôn nhất quán. Build tăng dần thì trang chủ và trang chuyên mục dễ lệch
/// với trang bài — lệch kiểu đó không có lỗi nào, chỉ có link trỏ vào bài đã ẩn.
/// </summary>
public class NewsSiteBuilder(
    NewsSiteRepository repository,
    NewsOgImageService ogImages,
    IOptions<NewsSiteOptions> options,
    IWebHostEnvironment env,
    ILogger<NewsSiteBuilder> logger)
{
    private NewsSiteOptions Opt => options.Value;

    private string OutputRoot => Path.IsPathRooted(Opt.OutputPath)
        ? Opt.OutputPath
        : Path.Combine(env.ContentRootPath, Opt.OutputPath);

    /// <summary>
    /// Kiểm ghi được thư mục xuất bản.
    ///
    /// Bắt buộc gọi lúc khởi động. Trong Docker, quên bind mount thì build "thành công" mỗi
    /// lượt, ghi vào lớp ghi tạm của container, và trang ngoài KHÔNG BAO GIỜ đổi — không lỗi
    /// nào, không ai phát hiện cho tới khi khách hỏi sao bài chưa lên.
    /// </summary>
    public bool CanWrite(out string reason)
    {
        try
        {
            Directory.CreateDirectory(OutputRoot);
            var probe = Path.Combine(OutputRoot, ".write-check");
            File.WriteAllText(probe, DateTime.UtcNow.ToString("O"));
            File.Delete(probe);
            reason = OutputRoot;
            return true;
        }
        catch (Exception ex)
        {
            reason = $"{OutputRoot}: {ex.Message}";
            return false;
        }
    }

    public async Task<BuildResult> BuildAsync(CancellationToken ct = default)
    {
        if (!Opt.Enabled)
            return new BuildResult(0, 0, OutputRoot, "NewsSite:Enabled đang tắt");

        if (!CanWrite(out var why))
        {
            logger.LogError("Không ghi được thư mục trang tin — {Why}", why);
            return new BuildResult(0, 0, OutputRoot, $"Không ghi được: {why}");
        }

        var renderer = new NewsSiteRenderer(Opt);
        var published = await repository.GetPublishedAsync(null, 200, ct);
        var topRead = await repository.GetTopReadAsync(5, ct);
        var pages = 0;

        // Trang bài
        foreach (var a in published)
        {
            ct.ThrowIfCancellationRequested();

            var related = published
                .Where(x => x.Id != a.Id && x.CategorySlug == a.CategorySlug)
                .Take(3).ToList();
            if (related.Count == 0)
                related = published.Where(x => x.Id != a.Id).Take(3).ToList();

            await WriteAtomicAsync(
                Path.Combine(OutputRoot, "tin", a.Slug + ".html"),
                renderer.RenderArticle(a, related, topRead), ct);
            pages++;

            // Chỉ dựng ảnh khi CHƯA có. Ảnh không đổi theo tiêu đề đã đóng băng, mà dựng lại
            // mỗi lượt build thì tốn vô ích — và tệ hơn: Facebook thấy file đổi thời gian sẽ
            // tải lại, trong khi nội dung y hệt.
            var ogPath = Path.Combine(OutputRoot, "assets", "og", a.Slug + ".jpg");
            if (!File.Exists(ogPath)) ogImages.TryRender(a.Title, a.CategorySlug, ogPath);

            a.LastBuiltAt = DateTime.UtcNow;
        }

        // Trang chủ
        await WriteAtomicAsync(
            Path.Combine(OutputRoot, "index.html"),
            renderer.RenderHome(published.Take(Opt.HomePageSize).ToList(), topRead), ct);
        pages++;

        // Trang chuyên mục — sinh cho MỌI mục trong taxonomy, kể cả mục chưa có bài và kể cả
        // "Khác" vốn không nằm trên thanh điều hướng.
        //
        // Trước đây chỉ sinh cho Navigable() nên /chuyen-muc/khac/ là 404, trong khi nhãn
        // chuyên mục trên trang bài vẫn trỏ vào đó — bài nào AI xếp vào "Khác" là có một link
        // chết ngay đầu bài. Thanh điều hướng vẫn giữ 3 mục; "Khác" chỉ tới được từ nhãn.
        foreach (var cat in NewsTaxonomy.All)
        {
            var items = published.Where(x => x.CategorySlug == cat.Slug).ToList();
            await WriteAtomicAsync(
                Path.Combine(OutputRoot, "chuyen-muc", cat.Slug, "index.html"),
                renderer.RenderCategory(cat, items, topRead), ct);
            pages++;
        }

        await WriteAtomicAsync(Path.Combine(OutputRoot, "sitemap.xml"), renderer.RenderSitemap(published), ct);
        await WriteAtomicAsync(Path.Combine(OutputRoot, "robots.txt"), renderer.RenderRobots(), ct);
        pages += 2;

        CopyAssets();

        if (published.Count > 0)
            await repository.SaveAsync(published[0], ct);

        logger.LogInformation("Dựng xong trang tin: {Articles} bài, {Pages} trang → {Path}",
            published.Count, pages, OutputRoot);
        return new BuildResult(published.Count, pages, OutputRoot, null);
    }

    /// <summary>
    /// Ghi ra file tạm CÙNG THƯ MỤC rồi đổi tên đè lên.
    ///
    /// Ghi thẳng vào file nginx đang đọc: tiến trình chết giữa chừng thì độc giả và Facebook
    /// thấy HTML cụt — và Facebook cache thẻ og rất lâu nên bản cụt đó dính lại nhiều ngày.
    /// File tạm PHẢI cùng thư mục: khác thư mục thì File.Move thành copy+delete và mất tính
    /// nguyên tử, đúng cái mình đang cố tránh.
    /// </summary>
    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, Path.GetFileName(path) + ".tmp");
        await File.WriteAllTextAsync(tmp, content, ct);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Sao assets từ khuôn VNINews. Không có thư mục khuôn thì bỏ qua và ghi cảnh báo — trang
    /// vẫn dựng được, chỉ mất CSS. Ném ở đây thì một thư mục thiếu làm hỏng cả lượt build.
    /// </summary>
    private void CopyAssets()
    {
        var src = Path.IsPathRooted(Opt.TemplatePath)
            ? Path.Combine(Opt.TemplatePath, "assets")
            : Path.Combine(env.ContentRootPath, Opt.TemplatePath, "assets");

        if (!Directory.Exists(src))
        {
            logger.LogWarning("Không thấy thư mục khuôn {Src} — trang sẽ không có CSS", src);
            return;
        }

        var dst = Path.Combine(OutputRoot, "assets");
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
    }
}
