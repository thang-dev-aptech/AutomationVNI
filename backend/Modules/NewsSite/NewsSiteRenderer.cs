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

    /// <summary>
    /// Bọc MỌI link nội bộ hiển thị trong trang (menu, asset, link bài, breadcrumb...) — khác
    /// với BaseUrl (dùng cho canonical/og/sitemap, đã ghép sẵn origin đầy đủ). Deploy domain
    /// riêng thì BasePath rỗng, hàm này là no-op, hành vi y hệt trước khi có subpath.
    /// </summary>
    private string Link(string path) => $"{options.BasePath}{path}";

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

        sb.AppendLine($"<link rel=\"icon\" href=\"{Link("/assets/favicon.svg")}\" type=\"image/svg+xml\">");
        sb.AppendLine($"<link rel=\"stylesheet\" href=\"{Link("/assets/styles.css")}\">");

        // Trang kết quả tìm kiếm và form đăng ký nhận tin cần gọi ra ngoài site tĩnh này (qua
        // proxy PHP cùng-origin) — JS cần biết BasePath để ghép đúng đường dẫn dù deploy ở
        // subdomain riêng (BasePath rỗng) hay dưới subpath (vd "/news").
        sb.AppendLine($"<script>window.NEWS_BASE_PATH=\"{options.BasePath}\";</script>");
        sb.AppendLine($"<script defer src=\"{Link("/assets/site.js")}\"></script>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<a class=\"skip\" href=\"#main\">Bỏ qua tới nội dung</a>");
        return sb.ToString();
    }

    private string Topbar()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<header class=\"topbar\"><div class=\"wrap topbar-in\">");

        // Logo và tên trang trỏ 2 nơi khác nhau CỐ Ý: logo về trang chủ VNI Education thật
        // (vni.edu.vn), tên trang bên cạnh về trang chủ TIN TỨC (Link("/")) — người bấm logo
        // đang muốn ra trang trường, người bấm tên trang đang muốn ở lại đọc tin.
        sb.AppendLine("<div class=\"brand\">");
        sb.AppendLine("<a class=\"brand-logo\" href=\"https://vni.edu.vn\" aria-label=\"VNI Education\">"
                      + $"<img src=\"{Link("/assets/logo-vni.png")}\" alt=\"VNI Education\" width=\"382\" height=\"238\"></a>");
        sb.AppendLine($"<a class=\"brand-text-link\" href=\"{Link("/")}\">"
                      + "<span class=\"brand-text\"><b>Tin tức &amp; Tri thức</b><span>vni.edu.vn</span></span></a>");
        sb.AppendLine("</div>");

        // Submit GET thẳng tới trang kết quả tĩnh /tim-kiem/ — trình duyệt tự thêm ?q=... vào
        // URL đích, không cần JS ở đây. Trang /tim-kiem/ mới cần JS để đọc và hiển thị.
        sb.AppendLine($"<form class=\"search\" role=\"search\" action=\"{Link("/tim-kiem/")}\" method=\"get\">");
        sb.AppendLine("<label class=\"skip\" for=\"q\">Tìm kiếm</label>");
        sb.AppendLine("<input id=\"q\" name=\"q\" type=\"search\" placeholder=\"Tìm kiếm bài viết…\" autocomplete=\"off\">");
        sb.AppendLine("<svg class=\"icon\" width=\"18\" height=\"18\" viewBox=\"0 0 24 24\" fill=\"none\" "
                      + "stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\">"
                      + "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m20 20-3.5-3.5\"/></svg>");
        sb.AppendLine("</form>");

        sb.AppendLine("<div class=\"topbar-right\">");
        sb.AppendLine($"<a class=\"btn-outline\" href=\"{Link("/#newsletter")}\">"
                      + "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" "
                      + "stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">"
                      + "<rect x=\"2\" y=\"4\" width=\"20\" height=\"16\" rx=\"2\"/><path d=\"m2 7 10 6 10-6\"/></svg>"
                      + "<span>Đăng ký nhận tin</span></a>");
        sb.AppendLine("<div class=\"socials\">");
        // Chỉ hiện icon nền tảng nào ĐÃ có URL cấu hình thật — href="#" chết còn tệ hơn không
        // có icon, vì trông như đang dùng được mà bấm vào không đi đâu cả.
        if (!string.IsNullOrWhiteSpace(options.SocialLinks?.FacebookUrl))
            sb.AppendLine(Social("Facebook", "M14 9h3V6h-3c-2.2 0-4 1.8-4 4v2H8v3h2v7h3v-7h3l1-3h-4v-2c0-.6.4-1 1-1z", options.SocialLinks!.FacebookUrl!));
        if (!string.IsNullOrWhiteSpace(options.SocialLinks?.YouTubeUrl))
            sb.AppendLine(Social("YouTube", "M22 12s0-3.5-.4-5.2a2.7 2.7 0 0 0-1.9-1.9C18 4.5 12 4.5 12 4.5s-6 0-7.7.4A2.7 2.7 0 0 0 2.4 6.8 28 28 0 0 0 2 12c0 1.7.4 5.2.4 5.2a2.7 2.7 0 0 0 1.9 1.9c1.7.4 7.7.4 7.7.4s6 0 7.7-.4a2.7 2.7 0 0 0 1.9-1.9C22 15.5 22 12 22 12zM10 15V9l5.2 3z", options.SocialLinks!.YouTubeUrl!));
        if (!string.IsNullOrWhiteSpace(options.SocialLinks?.TikTokUrl))
            sb.AppendLine(Social("TikTok", "M16.6 5.8a4.5 4.5 0 0 1-3.8-4H9.6v13.4a2.6 2.6 0 1 1-2.6-2.7c.3 0 .5 0 .8.1V9.4a5.9 5.9 0 0 0-.8 0A5.9 5.9 0 1 0 12.9 15V9.7a7.7 7.7 0 0 0 4.5 1.4V7.9a4.5 4.5 0 0 1-.8-2.1z", options.SocialLinks!.TikTokUrl!));
        sb.AppendLine("</div></div></div></header>");
        return sb.ToString();
    }

    private static string Social(string label, string path, string href)
        => $"<a href=\"{NewsHtml.Esc(href)}\" target=\"_blank\" rel=\"noopener noreferrer\" aria-label=\"{label}\">"
           + $"<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"currentColor\"><path d=\"{path}\"/></svg></a>";

    /// <summary>
    /// Thanh chuyên mục sinh từ NewsTaxonomy, KHÔNG viết cứng.
    ///
    /// Bản thiết kế trong VNINews có 7 slug cứng (tuyen-sinh, du-hoc, hoc-bong…) khác hẳn
    /// taxonomy thật. Để nguyên thì phần lớn link trên MỌI trang là 404 — không ai báo, chỉ
    /// tụt hạng Google và độc giả bấm hụt.
    /// </summary>
    private string MainNav(string? activeSlug)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<nav class=\"mainnav\" aria-label=\"Chuyên mục\"><div class=\"wrap mainnav-in\">");
        sb.AppendLine("<button class=\"burger\" type=\"button\" aria-label=\"Mở menu\">"
                      + "<svg width=\"20\" height=\"20\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" "
                      + "stroke-width=\"2\" stroke-linecap=\"round\"><path d=\"M3 6h18M3 12h18M3 18h18\"/></svg></button>");
        sb.AppendLine($"<a href=\"{Link("/")}\"{(activeSlug is null ? " aria-current=\"page\"" : "")}>Mới nhất</a>");
        foreach (var c in NewsTaxonomy.Navigable())
        {
            var cur = c.Slug == activeSlug ? " aria-current=\"page\"" : "";
            sb.AppendLine($"<a href=\"{Link($"/chuyen-muc/{c.Slug}/")}\"{cur}>{NewsHtml.Esc(c.Name)}</a>");
        }
        sb.AppendLine("</div></nav>");
        return sb.ToString();
    }

    private string Footer()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<footer class=\"site-foot\"><div class=\"wrap\"><div class=\"foot-grid\">");
        sb.AppendLine("<div class=\"foot-brand\">");
        sb.AppendLine("<a href=\"https://vni.edu.vn\" aria-label=\"VNI Education\">"
                      + $"<img src=\"{Link("/assets/logo-vni.png")}\" alt=\"VNI Education\" width=\"382\" height=\"238\"></a>");
        sb.AppendLine("<a href=\"https://vni.edu.vn\" class=\"foot-brand-link\">vni.edu.vn</a>");
        sb.AppendLine("<p>Tin giáo dục, tuyển sinh, AI &amp; công nghệ và kỹ năng nghề — biên tập lại từ "
                      + "nhiều nguồn báo chí, có dẫn nguồn đầy đủ ở cuối mỗi bài.</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div><h3>Về chúng tôi</h3><ul>"
                      + "<li><a href=\"https://vni.edu.vn\">Giới thiệu VNI</a></li>"
                      + "<li><a href=\"https://vni.edu.vn\">Liên hệ</a></li></ul></div>");

        sb.Append("<div><h3>Chuyên mục</h3><ul>");
        foreach (var c in NewsTaxonomy.Navigable())
            sb.Append($"<li><a href=\"{Link($"/chuyen-muc/{c.Slug}/")}\">{NewsHtml.Esc(c.Name)}</a></li>");
        sb.AppendLine("</ul></div>");

        sb.AppendLine("<div><h3>Liên hệ</h3><ul class=\"foot-contact\">"
                      + "<li><span>Hà Nội, Việt Nam</span></li>"
                      + "<li><a href=\"tel:+84823865858\">0823 86 5858</a></li>"
                      + "<li><a href=\"https://vni.edu.vn\">vni.edu.vn</a></li></ul></div>");

        sb.AppendLine("</div>");
        sb.AppendLine($"<div class=\"foot-bottom\">© {DateTime.UtcNow.Year} VNI Education · "
                      + $"{NewsHtml.Esc(BaseUrl.Replace("https://", ""))}</div>");
        sb.AppendLine("</div></footer></body></html>");
        return sb.ToString();
    }

    // ── Mảnh dùng lại ──────────────────────────────────────────────────────

    /// <summary>
    /// Ô ảnh của thẻ tin. Có ảnh thật thì dùng, không thì giữ ô gradient như trước.
    ///
    /// loading="lazy" và decoding="async": trang chủ có tới 12 thẻ, tải hết ảnh ngay lúc mở là
    /// chậm hẳn trên 3G. onerror ẩn ảnh để lộ nền gradient bên dưới — báo đổi đường dẫn ảnh
    /// hoặc chặn nhúng chéo thì thẻ vẫn nguyên khung, không thủng một lỗ trắng.
    /// </summary>
    private string Thumb(NewsArticleModel a, string tag, string? extraClass = null)
    {
        var cls = $"thumb {ThumbClass(a.CategorySlug)}{(extraClass is null ? "" : " " + extraClass)}";
        var inner = string.IsNullOrWhiteSpace(a.ImageUrl)
            ? ""
            : $"<img src=\"{NewsHtml.Esc(a.ImageUrl)}\" alt=\"\" loading=\"lazy\" decoding=\"async\" "
              + "onerror=\"this.remove()\">";
        return tag switch
        {
            "a" => $"<a href=\"{Link(NewsHtml.ArticlePath(a.Slug))}\" class=\"{cls}\" aria-hidden=\"true\" tabindex=\"-1\">{inner}</a>",
            _ => $"<span class=\"{cls}\">{inner}</span>",
        };
    }

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
        sb.Append(Thumb(a, "a"));
        sb.Append("<div class=\"card-body\">");
        sb.Append($"<span class=\"kicker\">{NewsHtml.Esc(cat.Name)}</span>");
        sb.Append($"<h3><a href=\"{Link(NewsHtml.ArticlePath(a.Slug))}\">{NewsHtml.Esc(a.Title)}</a></h3>");
        if (!string.IsNullOrWhiteSpace(a.Sapo)) sb.Append($"<p>{NewsHtml.Esc(a.Sapo)}</p>");
        sb.Append(Byline(a, withDate: false));
        sb.Append("</div></article>");
        return sb.ToString();
    }

    private string Mini(NewsArticleModel a)
    {
        var sb = new StringBuilder($"<a class=\"mini\" href=\"{Link(NewsHtml.ArticlePath(a.Slug))}\">");
        sb.Append(Thumb(a, "span") + "<span>");
        sb.Append($"<h3>{NewsHtml.Esc(a.Title)}</h3>");
        if (a.PublishedAt.HasValue)
            sb.Append($"<time datetime=\"{NewsHtml.IsoDate(a.PublishedAt.Value)}\">"
                      + $"{NewsHtml.VnDate(a.PublishedAt.Value)}</time>");
        sb.Append("</span></a>");
        return sb.ToString();
    }

    private string TopReadPanel(List<NewsArticleModel> top)
    {
        if (top.Count == 0) return "";
        var sb = new StringBuilder("<section class=\"panel\"><div class=\"panel-head\"><h2>Bài đọc nhiều</h2></div><ol class=\"ranked\">");
        foreach (var a in top)
            sb.Append($"<li><h4><a href=\"{Link(NewsHtml.ArticlePath(a.Slug))}\">{NewsHtml.Esc(a.Title)}</a></h4>"
                      + $"<span class=\"views\">{NewsHtml.Views(a.ViewCount)}</span></li>");
        sb.Append("</ol></section>");
        return sb.ToString();
    }

    /// <summary>Icon riêng từng chuyên mục — dùng chung một icon thì hàng ô nhìn như lỗi lặp.</summary>
    private static string CategoryIcon(string slug) => slug switch
    {
        "cong-nghe-ai" => "<rect x=\"7\" y=\"7\" width=\"10\" height=\"10\" rx=\"2\"/>"
                          + "<path d=\"M10 2v3M14 2v3M10 19v3M14 19v3M2 10h3M2 14h3M19 10h3M19 14h3\"/>",
        "ky-nang" => "<path d=\"M12 2 9.6 8.6 3 9.9l4.8 4.6L6.6 21 12 17.8 17.4 21l-1.2-6.5L21 9.9l-6.6-1.3L12 2Z\"/>",
        "phap-luat-chinh-sach" => "<path d=\"M12 3v18M8 21h8M12 3 5 7M12 3l7 4M2 11l3-5 3 5M2 11a3 3 0 0 0 6 0"
                                  + "M16 11l3-5 3 5M16 11a3 3 0 0 0 6 0\"/>",
        _ => "<path d=\"M22 9 12 4 2 9l10 5 10-5Z\"/><path d=\"M6 11v5c0 1.7 2.7 3 6 3s6-1.3 6-3v-5\"/>",
    };

    private static string Tile(string href, string icon, string label)
        => $"<a class=\"cat\" href=\"{href}\">"
           + "<svg width=\"26\" height=\"26\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" "
           + $"stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{icon}</svg>"
           + $"<span>{NewsHtml.Esc(label)}</span></a>";

    private string CategoryTiles()
    {
        var sb = new StringBuilder("<h2 class=\"section-title\">Chuyên mục</h2><div class=\"cats\">");
        foreach (var c in NewsTaxonomy.Navigable())
            sb.Append(Tile(Link($"/chuyen-muc/{c.Slug}/"), CategoryIcon(c.Slug), c.Name));

        sb.Append(Tile(Link("/"), "<rect x=\"3\" y=\"3\" width=\"7\" height=\"7\" rx=\"1.5\"/>"
                            + "<rect x=\"14\" y=\"3\" width=\"7\" height=\"7\" rx=\"1.5\"/>"
                            + "<rect x=\"3\" y=\"14\" width=\"7\" height=\"7\" rx=\"1.5\"/>"
                            + "<rect x=\"14\" y=\"14\" width=\"7\" height=\"7\" rx=\"1.5\"/>", "Xem tất cả"));
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// Băng đăng ký bản tin. Submit qua JS (site.js) gọi proxy PHP cùng-origin sang
    /// <c>POST /api/news-public/subscribe</c> — không cần trang chuyển hướng, kết quả hiện
    /// ngay trong <c>#newsletter-msg</c>.
    /// </summary>
    private static string Newsletter()
        => "<section class=\"newsletter\" id=\"newsletter\">"
           + "<div class=\"art\" aria-hidden=\"true\"><svg width=\"56\" height=\"56\" viewBox=\"0 0 24 24\" "
           + "fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.3\" stroke-linecap=\"round\" "
           + "stroke-linejoin=\"round\"><rect x=\"2\" y=\"5\" width=\"20\" height=\"14\" rx=\"2\"/>"
           + "<path d=\"m2 8 10 6 10-6\"/></svg></div>"
           + "<div><h2>Đăng ký nhận bản tin VNi</h2>"
           + "<p>Nhận tin giáo dục, tuyển sinh và kỹ năng nghề mới nhất, gửi ngay khi có bài mới.</p></div>"
           + "<div><form id=\"newsletter-form\">"
           + "<label class=\"skip\" for=\"email\">Email của bạn</label>"
           + "<input id=\"email\" name=\"email\" type=\"email\" placeholder=\"Nhập email của bạn…\" required>"
           + "<button class=\"btn-primary\" type=\"submit\">Đăng ký ngay</button></form>"
           + "<small id=\"newsletter-msg\"></small></div></section>";

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

            // Ảnh nền của tin nổi bật nằm DƯỚI lớp phủ tối, nên chữ tít vẫn đọc được.
            // Thiếu ảnh thì lớp gradient của .feature lộ ra — đúng diện mạo cũ.
            if (!string.IsNullOrWhiteSpace(lead.ImageUrl))
                sb.AppendLine($"<img class=\"feature-bg\" src=\"{NewsHtml.Esc(lead.ImageUrl)}\" alt=\"\" "
                              + "decoding=\"async\" onerror=\"this.remove()\">");

            sb.AppendLine("<span class=\"feature-shade\"></span><div class=\"feature-body\">");
            sb.AppendLine("<span class=\"badge-hot\">Tin nổi bật</span>");
            sb.AppendLine($"<h2><a href=\"{Link(NewsHtml.ArticlePath(lead.Slug))}\">{NewsHtml.Esc(lead.Title)}</a></h2>");
            if (!string.IsNullOrWhiteSpace(lead.Sapo)) sb.AppendLine($"<p>{NewsHtml.Esc(lead.Sapo)}</p>");
            sb.AppendLine(Byline(lead));
            sb.AppendLine("</div></article>");

            sb.AppendLine("<section class=\"panel\"><div class=\"panel-head\"><h2>Tin mới nhất</h2>"
                          + $"<a href=\"{Link("/chuyen-muc/giao-duc/")}\">Xem tất cả ›</a></div>");
            foreach (var a in articles.Skip(1).Take(4)) sb.AppendLine(Mini(a));
            sb.AppendLine("</section></div>");

            sb.AppendLine(CategoryTiles());

            sb.AppendLine("<div class=\"grid-main\"><div>");
            sb.AppendLine("<h2 class=\"section-title\">Bài viết mới nhất</h2>");
            sb.AppendLine("<nav class=\"tabs\" aria-label=\"Lọc theo chuyên mục\">");
            sb.AppendLine($"<a href=\"{Link("/")}\" aria-current=\"page\">Tất cả</a>");
            foreach (var c in NewsTaxonomy.Navigable())
                sb.AppendLine($"<a href=\"{Link($"/chuyen-muc/{c.Slug}/")}\">{NewsHtml.Esc(c.Name)}</a>");
            sb.AppendLine("</nav><div class=\"cards\">");
            foreach (var a in articles.Skip(1)) sb.AppendLine(Card(a));
            sb.AppendLine("</div>");
            sb.AppendLine("<div class=\"more-wrap\">"
                          + $"<a class=\"btn-outline\" href=\"{Link("/chuyen-muc/giao-duc/")}\">Xem thêm bài viết</a></div>");
            sb.AppendLine("</div>");
            sb.AppendLine($"<aside class=\"aside\">{TopReadPanel(topRead)}</aside>");
            sb.AppendLine("</div>");
            sb.AppendLine(Newsletter());
        }

        sb.AppendLine("</div></main>");
        sb.Append(Footer());
        return sb.ToString();
    }

    // ── Trang tìm kiếm ─────────────────────────────────────────────────────

    /// <summary>
    /// Khung rỗng, tĩnh — site.js đọc `?q=` từ location.search lúc trang mở, gọi proxy PHP rồi
    /// tự đổ kết quả vào #search-results. Không dựng sẵn kết quả ở đây vì trang này CHÍNH nó là
    /// file tĩnh, không biết trước người dùng sẽ tìm gì.
    /// </summary>
    public string RenderSearch()
    {
        var sb = new StringBuilder();
        sb.Append(Head($"Tìm kiếm — {SiteName}", "Tìm kiếm bài viết trên " + SiteName, null, "/tim-kiem/"));
        sb.Append(Topbar());
        sb.Append(MainNav(null));
        sb.AppendLine("<main id=\"main\"><div class=\"wrap\">");
        sb.AppendLine("<h1 class=\"section-title\">Kết quả tìm kiếm</h1>");
        sb.AppendLine("<div id=\"search-status\" class=\"empty\">Nhập từ khoá ở ô tìm kiếm phía trên.</div>");
        sb.AppendLine("<div id=\"search-results\" class=\"cards\"></div>");
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
        sb.Append(Head($"{a.Title} — {SiteName}", a.Sapo, ogImage, NewsHtml.ArticlePath(a.Slug), "article"));
        if (a.PublishedAt.HasValue)
            sb.AppendLine($"<meta property=\"article:published_time\" content=\"{NewsHtml.IsoDate(a.PublishedAt.Value)}\">");
        sb.AppendLine($"<meta property=\"article:section\" content=\"{NewsHtml.Esc(cat.Name)}\">");

        sb.Append(Topbar());
        sb.Append(MainNav(cat.Slug));
        sb.AppendLine("<main id=\"main\"><div class=\"wrap\"><div class=\"grid-main\">");

        sb.AppendLine("<article class=\"article\">");
        sb.AppendLine("<nav class=\"crumb\" aria-label=\"Đường dẫn\">"
                      + $"<a href=\"{Link("/")}\">Trang chủ</a><span>›</span>"
                      + $"<a href=\"{Link($"/chuyen-muc/{cat.Slug}/")}\">{NewsHtml.Esc(cat.Name)}</a></nav>");
        sb.AppendLine($"<span class=\"kicker\">{NewsHtml.Esc(cat.Name)}</span>");
        sb.AppendLine($"<h1>{NewsHtml.Esc(a.Title)}</h1>");
        sb.AppendLine(Byline(a));

        if (!string.IsNullOrWhiteSpace(a.Sapo))
            sb.AppendLine($"<p class=\"sapo\">{NewsHtml.Esc(a.Sapo)}</p>");

        // Ảnh đầu bài LUÔN đi kèm dòng ghi nguồn.
        //
        // "Ảnh: thanhnien.vn" là điều kiện để dùng ảnh của toà soạn, không phải phần trang trí
        // — cũng vì thế NewsPublisher chỉ nhận ảnh khi biết tên báo. Không có ảnh thật thì giữ
        // ô gradient và ghi "Ảnh minh hoạ", đừng ghi nguồn cho một ô màu.
        sb.AppendLine(string.IsNullOrWhiteSpace(a.ImageUrl)
            ? $"<figure><div class=\"thumb {ThumbClass(a.CategorySlug)}\"></div>"
              + "<figcaption>Ảnh minh hoạ.</figcaption></figure>"
            : $"<figure><div class=\"thumb {ThumbClass(a.CategorySlug)}\">"
              + $"<img src=\"{NewsHtml.Esc(a.ImageUrl)}\" alt=\"{NewsHtml.Esc(a.Title)}\" "
              + "decoding=\"async\" onerror=\"this.remove()\"></div>"
              + $"<figcaption>{NewsHtml.Esc(a.ImageCredit ?? "Ảnh minh hoạ.")}</figcaption></figure>");

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
                      + "Bài viết được biên tập lại bởi VNi An từ thông tin trên "
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
            sb.Append($"<url><loc>{NewsHtml.Esc(BaseUrl)}{NewsHtml.ArticlePath(a.Slug)}</loc>");
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
