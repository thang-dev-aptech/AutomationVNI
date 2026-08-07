using System.Text;
using Backend.Modules.ContentCrawl;

namespace Backend.Modules.NewsSite;

/// <summary>
/// Dựng HTML cho trang tin công khai, dùng đúng bộ class của <c>VNINews/assets/styles.css</c>.
///
/// Vì sao render ở server chứ không phải React như phần quản trị: trình thu thập của Facebook
/// KHÔNG chạy JavaScript. Trang SPA share lên fanpage sẽ ra thẻ trống — không tiêu đề, không
/// ảnh, không mô tả — mà share lên fanpage chính là lý do tồn tại của cả trang này.
///
/// Vì sao sinh HTML bằng C# thay vì thay chuỗi trong file mẫu: trang chủ có ba danh sách LẶP
/// (thẻ tin, tin mới nhất, bài đọc nhiều) với số phần tử thay đổi. Thay chuỗi không lặp được
/// nếu không tự viết một bộ template engine. File trong <c>VNINews/</c> giữ vai trò bản thiết
/// kế và fixture đối chiếu; <c>assets/</c> được sao nguyên vào thư mục xuất bản.
/// </summary>
public class NewsSiteRenderer(NewsSiteOptions options)
{
    private string SiteName => options.SiteName;
    private string BaseUrl => options.PublicBaseUrl?.TrimEnd('/') ?? "";

    // ── Khung chung ────────────────────────────────────────────────────────

    private string Head(string title, string? description, string? ogImage, string canonicalPath,
        string ogType = "website")
    {
        var sb = new StringBuilder();
        var url = $"{BaseUrl}{canonicalPath}";
        var img = string.IsNullOrWhiteSpace(ogImage) ? $"{BaseUrl}/assets/og-default.png" : ogImage;

        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"vi\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{NewsHtml.Esc(title)}</title>");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($"<meta name=\"description\" content=\"{NewsHtml.Esc(description)}\">");
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            sb.AppendLine($"<link rel=\"canonical\" href=\"{NewsHtml.Esc(url)}\">");

        // og:* PHẢI có sẵn trong HTML lúc server trả trang — Facebook không chạy JavaScript.
        // Thiếu og:image thì thẻ share ra một ô xám không ảnh và tỉ lệ bấm rơi thẳng.
        sb.AppendLine($"<meta property=\"og:site_name\" content=\"{NewsHtml.Esc(SiteName)}\">");
        sb.AppendLine($"<meta property=\"og:type\" content=\"{ogType}\">");
        sb.AppendLine($"<meta property=\"og:title\" content=\"{NewsHtml.Esc(title)}\">");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($"<meta property=\"og:description\" content=\"{NewsHtml.Esc(description)}\">");
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            sb.AppendLine($"<meta property=\"og:url\" content=\"{NewsHtml.Esc(url)}\">");
            sb.AppendLine($"<meta property=\"og:image\" content=\"{NewsHtml.Esc(img)}\">");
            sb.AppendLine("<meta property=\"og:image:width\" content=\"1200\">");
            sb.AppendLine("<meta property=\"og:image:height\" content=\"630\">");
        }
        sb.AppendLine("<meta name=\"twitter:card\" content=\"summary_large_image\">");

        sb.AppendLine("<link rel=\"icon\" href=\"/assets/favicon.svg\" type=\"image/svg+xml\">");
        sb.AppendLine("<link rel=\"stylesheet\" href=\"/assets/styles.css\">");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<a class=\"skip\" href=\"#main\">Bỏ qua tới nội dung</a>");
        return sb.ToString();
    }

    private string Topbar()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<header class=\"topbar\"><div class=\"wrap topbar-in\">");
        sb.AppendLine("<a class=\"brand\" href=\"/\">");
        sb.AppendLine("<img src=\"/assets/logo-vni.png\" alt=\"VNI Education\" width=\"382\" height=\"238\">");
        sb.AppendLine("<span class=\"brand-text\"><b>Tin tức &amp; Tri thức</b><span>vni.edu.vn</span></span>");
        sb.AppendLine("</a>");
        sb.AppendLine("<div class=\"topbar-right\">");
        sb.AppendLine("<div class=\"socials\">");
        sb.AppendLine(Social("Facebook", "M14 9h3V6h-3c-2.2 0-4 1.8-4 4v2H8v3h2v7h3v-7h3l1-3h-4v-2c0-.6.4-1 1-1z"));
        sb.AppendLine(Social("Telegram", "M21.9 4.3 18.7 19c-.2 1-.9 1.3-1.8.8l-4.9-3.6-2.4 2.3c-.3.3-.5.5-1 .5l.4-5 9.2-8.3c.4-.4-.1-.6-.6-.2L6.3 12.8 1.9 11.4c-1-.3-1-1 .2-1.4l18.5-7.1c.8-.3 1.5.2 1.3 1.4z"));
        sb.AppendLine("</div></div></div></header>");
        return sb.ToString();
    }

    private static string Social(string label, string path)
        => $"<a href=\"#\" aria-label=\"{label}\"><svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"currentColor\"><path d=\"{path}\"/></svg></a>";

    /// <summary>
    /// Thanh chuyên mục sinh từ NewsTaxonomy, KHÔNG viết cứng.
    ///
    /// Bản thiết kế trong VNINews có 7 slug cứng (tuyen-sinh, du-hoc, hoc-bong…) khác hẳn
    /// taxonomy thật. Để nguyên thì phần lớn link trên MỌI trang là 404 — không ai báo, chỉ
    /// tụt hạng Google và độc giả bấm hụt.
    /// </summary>
    private static string MainNav(string? activeSlug)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<nav class=\"mainnav\" aria-label=\"Chuyên mục\"><div class=\"wrap mainnav-in\">");
        sb.AppendLine($"<a href=\"/\"{(activeSlug is null ? " aria-current=\"page\"" : "")}>Mới nhất</a>");
        foreach (var c in NewsTaxonomy.Navigable())
        {
            var cur = c.Slug == activeSlug ? " aria-current=\"page\"" : "";
            sb.AppendLine($"<a href=\"/chuyen-muc/{c.Slug}/\"{cur}>{NewsHtml.Esc(c.Name)}</a>");
        }
        sb.AppendLine("</div></nav>");
        return sb.ToString();
    }

    private string Footer()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<footer class=\"site-foot\"><div class=\"wrap\"><div class=\"foot-grid\">");
        sb.AppendLine("<div class=\"foot-brand\">");
        sb.AppendLine("<img src=\"/assets/logo-vni.png\" alt=\"VNI Education\" width=\"382\" height=\"238\">");
        sb.AppendLine("<p>Tin giáo dục, tuyển sinh, AI &amp; công nghệ và kỹ năng nghề — biên tập lại từ "
                      + "nhiều nguồn báo chí, có dẫn nguồn đầy đủ ở cuối mỗi bài.</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div><h3>Về chúng tôi</h3><ul>"
                      + "<li><a href=\"https://vni.edu.vn\">Giới thiệu VNI</a></li>"
                      + "<li><a href=\"https://vni.edu.vn\">Liên hệ</a></li></ul></div>");

        sb.Append("<div><h3>Chuyên mục</h3><ul>");
        foreach (var c in NewsTaxonomy.Navigable())
            sb.Append($"<li><a href=\"/chuyen-muc/{c.Slug}/\">{NewsHtml.Esc(c.Name)}</a></li>");
        sb.AppendLine("</ul></div>");

        sb.AppendLine("<div><h3>Liên hệ</h3><ul class=\"foot-contact\">"
                      + "<li><span>Hà Nội, Việt Nam</span></li>"
                      + "<li><a href=\"mailto:info@vni.edu.vn\">info@vni.edu.vn</a></li>"
                      + "<li><a href=\"https://vni.edu.vn\">vni.edu.vn</a></li></ul></div>");

        sb.AppendLine("</div>");
        sb.AppendLine($"<div class=\"foot-bottom\">© {DateTime.UtcNow.Year} VNI Education · "
                      + $"{NewsHtml.Esc(BaseUrl.Replace("https://", ""))}</div>");
        sb.AppendLine("</div></footer></body></html>");
        return sb.ToString();
    }

    // ── Mảnh dùng lại ──────────────────────────────────────────────────────

    private static string ThumbClass(string? slug) => "t-" + NewsTaxonomy.Resolve(slug).Slug;

    private string Byline(NewsArticleModel a, bool withDate = true)
    {
        var sb = new StringBuilder("<div class=\"byline\">");
        sb.Append("<span class=\"avatar\" aria-hidden=\"true\">VNI</span>");
        sb.Append($"<span>{NewsHtml.Esc(options.AuthorName)}</span>");
        if (withDate && a.PublishedAt.HasValue)
        {
            sb.Append("<span class=\"sep\">·</span>");
            sb.Append($"<time datetime=\"{NewsHtml.IsoDate(a.PublishedAt.Value)}\">"
                      + $"{NewsHtml.VnDate(a.PublishedAt.Value)}</time>");
        }
        sb.Append("<span class=\"sep\">·</span>");
        sb.Append($"<span>{a.ReadMinutes} phút đọc</span>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private string Card(NewsArticleModel a)
    {
        var cat = NewsTaxonomy.Resolve(a.CategorySlug);
        var sb = new StringBuilder("<article class=\"card\">");
        sb.Append($"<a href=\"/tin/{a.Slug}\" class=\"thumb {ThumbClass(a.CategorySlug)}\" aria-hidden=\"true\" tabindex=\"-1\"></a>");
        sb.Append("<div class=\"card-body\">");
        sb.Append($"<span class=\"kicker\">{NewsHtml.Esc(cat.Name)}</span>");
        sb.Append($"<h3><a href=\"/tin/{a.Slug}\">{NewsHtml.Esc(a.Title)}</a></h3>");
        if (!string.IsNullOrWhiteSpace(a.Sapo)) sb.Append($"<p>{NewsHtml.Esc(a.Sapo)}</p>");
        sb.Append(Byline(a, withDate: false));
        sb.Append("</div></article>");
        return sb.ToString();
    }

    private static string Mini(NewsArticleModel a)
    {
        var sb = new StringBuilder($"<a class=\"mini\" href=\"/tin/{a.Slug}\">");
        sb.Append($"<span class=\"thumb {ThumbClass(a.CategorySlug)}\"></span><span>");
        sb.Append($"<h3>{NewsHtml.Esc(a.Title)}</h3>");
        if (a.PublishedAt.HasValue)
            sb.Append($"<time datetime=\"{NewsHtml.IsoDate(a.PublishedAt.Value)}\">"
                      + $"{NewsHtml.VnDate(a.PublishedAt.Value)}</time>");
        sb.Append("</span></a>");
        return sb.ToString();
    }

    private static string TopReadPanel(List<NewsArticleModel> top)
    {
        if (top.Count == 0) return "";
        var sb = new StringBuilder("<section class=\"panel\"><div class=\"panel-head\"><h2>Bài đọc nhiều</h2></div><ol class=\"ranked\">");
        foreach (var a in top)
            sb.Append($"<li><h4><a href=\"/tin/{a.Slug}\">{NewsHtml.Esc(a.Title)}</a></h4>"
                      + $"<span class=\"views\">{NewsHtml.Views(a.ViewCount)}</span></li>");
        sb.Append("</ol></section>");
        return sb.ToString();
    }

    private static string CategoryTiles()
    {
        var sb = new StringBuilder("<h2 class=\"section-title\">Chuyên mục</h2><div class=\"cats\">");
        foreach (var c in NewsTaxonomy.Navigable())
            sb.Append($"<a class=\"cat\" href=\"/chuyen-muc/{c.Slug}/\">"
                      + "<svg width=\"26\" height=\"26\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" "
                      + "stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\">"
                      + "<path d=\"M22 9 12 4 2 9l10 5 10-5Z\"/><path d=\"M6 11v5c0 1.7 2.7 3 6 3s6-1.3 6-3v-5\"/></svg>"
                      + $"<span>{NewsHtml.Esc(c.Name)}</span></a>");
        sb.Append("</div>");
        return sb.ToString();
    }

    // ── Trang chủ ──────────────────────────────────────────────────────────

    public string RenderHome(List<NewsArticleModel> articles, List<NewsArticleModel> topRead)
    {
        var lead = articles.FirstOrDefault();
        var sb = new StringBuilder();

        sb.Append(Head(
            $"{SiteName} — Giáo dục · AI & Công nghệ · Kỹ năng",
            "Tin giáo dục, tuyển sinh, AI & công nghệ và kỹ năng nghề nghiệp — biên tập lại từ nhiều nguồn, có dẫn nguồn đầy đủ.",
            null, "/"));
        sb.Append(Topbar());
        sb.Append(MainNav(null));
        sb.AppendLine("<main id=\"main\"><div class=\"wrap\">");

        if (lead is null)
        {
            sb.AppendLine("<div class=\"empty\">Chưa có bài nào được đăng.</div>");
        }
        else
        {
            sb.AppendLine("<div class=\"grid-hero\">");
            sb.AppendLine($"<article class=\"feature {ThumbClass(lead.CategorySlug)}\">");
            sb.AppendLine("<span class=\"feature-shade\"></span><div class=\"feature-body\">");
            sb.AppendLine("<span class=\"badge-hot\">Tin nổi bật</span>");
            sb.AppendLine($"<h2><a href=\"/tin/{lead.Slug}\">{NewsHtml.Esc(lead.Title)}</a></h2>");
            if (!string.IsNullOrWhiteSpace(lead.Sapo)) sb.AppendLine($"<p>{NewsHtml.Esc(lead.Sapo)}</p>");
            sb.AppendLine(Byline(lead));
            sb.AppendLine("</div></article>");

            sb.AppendLine("<section class=\"panel\"><div class=\"panel-head\"><h2>Tin mới nhất</h2></div>");
            foreach (var a in articles.Skip(1).Take(4)) sb.AppendLine(Mini(a));
            sb.AppendLine("</section></div>");

            sb.AppendLine(CategoryTiles());

            sb.AppendLine("<div class=\"grid-main\"><div>");
            sb.AppendLine("<h2 class=\"section-title\">Bài viết mới nhất</h2>");
            sb.AppendLine("<nav class=\"tabs\" aria-label=\"Lọc theo chuyên mục\">");
            sb.AppendLine("<a href=\"/\" aria-current=\"page\">Tất cả</a>");
            foreach (var c in NewsTaxonomy.Navigable())
                sb.AppendLine($"<a href=\"/chuyen-muc/{c.Slug}/\">{NewsHtml.Esc(c.Name)}</a>");
            sb.AppendLine("</nav><div class=\"cards\">");
            foreach (var a in articles.Skip(1)) sb.AppendLine(Card(a));
            sb.AppendLine("</div></div>");
            sb.AppendLine($"<aside class=\"aside\">{TopReadPanel(topRead)}</aside>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div></main>");
        sb.Append(Footer());
        return sb.ToString();
    }

    // ── Trang chuyên mục ───────────────────────────────────────────────────

    public string RenderCategory(NewsCategory cat, List<NewsArticleModel> articles, List<NewsArticleModel> topRead)
    {
        var sb = new StringBuilder();
        sb.Append(Head($"{cat.Name} — {SiteName}", cat.Hint, null, $"/chuyen-muc/{cat.Slug}/"));
        sb.Append(Topbar());
        sb.Append(MainNav(cat.Slug));
        sb.AppendLine("<main id=\"main\"><div class=\"wrap\">");
        sb.AppendLine($"<h1 class=\"section-title\">{NewsHtml.Esc(cat.Name)}</h1>");
        sb.AppendLine("<div class=\"grid-main\"><div>");

        // Chuyên mục chưa có bài vẫn phải có TRANG. Trang rỗng lịch sự tốt hơn 404, vì thanh
        // điều hướng luôn liệt kê đủ mục.
        if (articles.Count == 0)
            sb.AppendLine("<div class=\"empty\">Chuyên mục này chưa có bài nào.</div>");
        else
        {
            sb.AppendLine("<div class=\"cards\">");
            foreach (var a in articles) sb.AppendLine(Card(a));
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine($"<aside class=\"aside\">{TopReadPanel(topRead)}</aside>");
        sb.AppendLine("</div></div></main>");
        sb.Append(Footer());
        return sb.ToString();
    }

    // ── Trang bài viết ─────────────────────────────────────────────────────

    public string RenderArticle(
        NewsArticleModel a, List<NewsArticleModel> related, List<NewsArticleModel> topRead)
    {
        var cat = NewsTaxonomy.Resolve(a.CategorySlug);
        var ogImage = string.IsNullOrWhiteSpace(BaseUrl) ? null : $"{BaseUrl}/assets/og/{a.Slug}.jpg";

        var sb = new StringBuilder();
        sb.Append(Head($"{a.Title} — {SiteName}", a.Sapo, ogImage, $"/tin/{a.Slug}", "article"));
        if (a.PublishedAt.HasValue)
            sb.AppendLine($"<meta property=\"article:published_time\" content=\"{NewsHtml.IsoDate(a.PublishedAt.Value)}\">");
        sb.AppendLine($"<meta property=\"article:section\" content=\"{NewsHtml.Esc(cat.Name)}\">");

        sb.Append(Topbar());
        sb.Append(MainNav(cat.Slug));
        sb.AppendLine("<main id=\"main\"><div class=\"wrap\"><div class=\"grid-main\">");

        sb.AppendLine("<article class=\"article\">");
        sb.AppendLine("<nav class=\"crumb\" aria-label=\"Đường dẫn\">"
                      + "<a href=\"/\">Trang chủ</a><span>›</span>"
                      + $"<a href=\"/chuyen-muc/{cat.Slug}/\">{NewsHtml.Esc(cat.Name)}</a></nav>");
        sb.AppendLine($"<span class=\"kicker\">{NewsHtml.Esc(cat.Name)}</span>");
        sb.AppendLine($"<h1>{NewsHtml.Esc(a.Title)}</h1>");
        sb.AppendLine(Byline(a));

        if (!string.IsNullOrWhiteSpace(a.Sapo))
            sb.AppendLine($"<p class=\"sapo\">{NewsHtml.Esc(a.Sapo)}</p>");

        sb.AppendLine($"<figure><div class=\"thumb {ThumbClass(a.CategorySlug)}\"></div>"
                      + "<figcaption>Ảnh minh hoạ.</figcaption></figure>");

        var keyPoints = NewsSiteRepository.ReadKeyPoints(a.KeyPointsJson);
        if (keyPoints.Count > 0)
        {
            sb.AppendLine("<section class=\"keypoints\"><h2>Điều cần nhớ</h2><ul>");
            foreach (var k in keyPoints) sb.AppendLine($"<li>{NewsHtml.Esc(k)}</li>");
            sb.AppendLine("</ul></section>");
        }

        // BodyHtml đã escape từng dòng rồi mới dựng thẻ ở NewsHtml.ToSafeHtml, đưa thẳng vào.
        sb.AppendLine($"<div class=\"prose\">{a.BodyHtml}</div>");

        var timeline = NewsSiteRepository.ReadTimeline(a.TimelineJson);
        if (timeline.Count > 0)
        {
            sb.AppendLine("<section class=\"timeline\"><h2>Mốc cần nhớ</h2><ul>");
            foreach (var t in timeline)
                sb.AppendLine($"<li><b>{NewsHtml.Esc(t.Time)}</b> <span>{NewsHtml.Esc(t.What)}</span></li>");
            sb.AppendLine("</ul></section>");
        }

        // Dẫn nguồn: nơi gọi đã từ chối xuất bản bài thiếu nguồn, ở đây chỉ dựng.
        sb.AppendLine("<div class=\"source\"><b>Nguồn tham khảo</b>"
                      + "Bài viết được biên tập lại từ thông tin trên "
                      + $"<a href=\"{NewsHtml.Esc(a.SourceUrl)}\" target=\"_blank\" rel=\"noopener nofollow\">"
                      + $"{NewsHtml.Esc(a.SourceName ?? a.SourceUrl)}</a>. "
                      + "Các số liệu và mốc thời gian giữ nguyên theo bản gốc.</div>");
        sb.AppendLine("</article>");

        sb.AppendLine("<aside class=\"aside\">");
        if (related.Count > 0)
        {
            sb.AppendLine("<section class=\"panel\"><div class=\"panel-head\"><h2>Tin liên quan</h2></div>");
            foreach (var r in related) sb.AppendLine(Mini(r));
            sb.AppendLine("</section>");
        }
        sb.AppendLine(TopReadPanel(topRead));
        sb.AppendLine("</aside>");

        sb.AppendLine("</div></div></main>");
        sb.Append(Footer());
        return sb.ToString();
    }

    // ── sitemap ────────────────────────────────────────────────────────────

    public string RenderSitemap(List<NewsArticleModel> articles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        sb.AppendLine($"<url><loc>{NewsHtml.Esc(BaseUrl)}/</loc></url>");
        foreach (var c in NewsTaxonomy.Navigable())
            sb.AppendLine($"<url><loc>{NewsHtml.Esc(BaseUrl)}/chuyen-muc/{c.Slug}/</loc></url>");
        foreach (var a in articles)
        {
            sb.Append($"<url><loc>{NewsHtml.Esc(BaseUrl)}/tin/{a.Slug}</loc>");
            if (a.PublishedAt.HasValue)
                sb.Append($"<lastmod>{a.PublishedAt.Value:yyyy-MM-dd}</lastmod>");
            sb.AppendLine("</url>");
        }
        sb.AppendLine("</urlset>");
        return sb.ToString();
    }

    public string RenderRobots()
        => string.IsNullOrWhiteSpace(BaseUrl)
            ? "User-agent: *\nAllow: /\n"
            : $"User-agent: *\nAllow: /\nSitemap: {BaseUrl}/sitemap.xml\n";
}
