namespace Backend.Modules.GenerationJob;

/// <summary>
/// Thư viện "hướng sáng tạo" cho banner. Mỗi lần sinh ảnh bốc ngẫu nhiên 1 giá trị mỗi trục để
/// LLM art-director hiện thực hóa một bố cục KHÁC nhau, tránh ra 2 dạng generic (logo/ảnh/CTA).
/// Mức "sáng tạo có kiểm soát": mọi lựa chọn đều an toàn cho chữ tiếng Việt + giữ rõ nhận diện brand.
/// </summary>
public static class CreativeDirectionLibrary
{
    private static readonly string[] Compositions =
    [
        "Split layout: a rich photo scene on one side and a densely designed content panel (headline + feature chips + highlight badges) on the other, divided by a smooth curved brand-color sweep — every area filled",
        "Full-bleed scene filling one side edge-to-edge, a solid brand-color panel holding the copy on the other",
        "Immersive full-frame scene with the headline overlaid on a lower-third gradient band",
        "Hero scene filling the whole frame, headline anchored top under a subtle scrim, chips + CTA integrated at the bottom over the image",
        "Vertical brand-color side column for logo + headline + CTA, the scene filling the rest edge-to-edge",
        "Editorial layout: a large full-bleed scene with an offset headline block and a clean footer strip for the CTA",
        "Diagonal split across a full-bleed scene, a solid brand-color band carrying the headline",
        "Angled brand-color band crossing a full-bleed scene to hold the headline, subject filling the rest"
    ];

    private static readonly string[] Formats =
    [
        "a modern social-media banner",
        "an editorial poster",
        "a premium advertising key visual",
        "a magazine-style ad",
        "a bold promotional poster",
        "a clean corporate ad",
        "a vibrant campaign key visual",
        "an infographic-style promo"
    ];

    // Chỉ những nền khối HÀI HÒA (xanh/navy/trắng/kem/gradient/glass/no-panel).
    // Cam & xanh lá KHÔNG dùng làm nền khối lớn (chói) — chỉ làm accent nhỏ.
    private static readonly string[] ContentBlocks =
    [
        "a clean white / light panel with dark-blue text and tasteful accents",
        "a solid brand-blue panel with white text",
        "a deep navy panel with white text and a thin orange accent line",
        "a deep blue-to-indigo gradient panel with white text",
        "a soft cream / off-white panel with navy text and subtle accents",
        "a dark charcoal panel with white text and one bright accent",
        "a muted warm-amber gradient panel with dark, high-contrast text",
        "a translucent frosted-glass panel over the scene",
        "no solid panel — the headline sits directly on the scene with a soft scrim/shadow for legibility",
        "no solid panel — text over the image with only a subtle gradient fade for legibility"
    ];

    // ~1/3 là "không script" để luân phiên, không lạm dụng font viết tay.
    private static readonly string[] ScriptAccents =
    [
        "no handwritten script — keep all type clean modern sans-serif",
        "no handwritten script — clean sans-serif only",
        "no handwritten script — bold sans-serif throughout",
        "add a tasteful handwritten/script accent on ONE short phrase (the hook or tagline), kept legible",
        "add a small handwritten flourish or underline accent near the headline",
        "mix a light handwritten tagline with the bold sans-serif headline"
    ];

    private static readonly string[] FocalPoints =
    [
        "the confident hero subject caught mid-action, spotlighted by light and scale so the eye lands there first",
        "a bold oversized stat or number badge (e.g. a percentage) acting as the main eye-magnet",
        "one key word of the headline spotlighted in the accent color at a dramatic size",
        "a striking close-up of the topic's signature tool/prop, sharply lit against a softer surround",
        "a dynamic gesture or interaction moment that pulls the eye and radiates energy"
    ];

    private static readonly string[] Typography =
    [
        "Oversized bold condensed uppercase headline as the clear focal element",
        "Strong weight contrast: an ultra-bold headline paired with a light elegant subheadline",
        "Left-aligned editorial headline with a colored underline or short accent bar",
        "Stacked headline where one key word is highlighted in an accent brand color",
        "Headline set inside a rounded brand-color tag/ribbon shape",
        "Clean modern sans-serif with tight tracking and a very clear size hierarchy",
        "A big keyword used as a graphic anchor next to the supporting headline"
    ];

    private static readonly string[] VisualStyles =
    [
        "Premium editorial photography in a rich, detailed real environment with natural light",
        "Duotone brand-color treatment over a full-frame scene",
        "Cinematic photography with shallow depth and a full, lived-in setting",
        "Modern flat vector illustration with a fully-illustrated detailed scene (no empty background)",
        "Warm documentary-style photo packed with authentic environmental detail",
        "Soft friendly 3D render of a complete, detailed scene"
    ];

    private static readonly string[] Moods =
    [
        "bold and energetic",
        "calm and premium",
        "warm, human and approachable",
        "confident and professional",
        "fresh, youthful and optimistic"
    ];

    private static readonly string[] Heroes =
    [
        "typography-led (a bold headline leads over a full scene)",
        "subject-led (a person leads within a rich scene)",
        "scene-led (an immersive full environment leads)",
        "action-led (people doing the activity fill the frame)"
    ];

    /// <summary>Bốc 1 giá trị mỗi trục → 1 creative brief độc nhất (nhiều nghìn tổ hợp).</summary>
    public static string BuildBrief(Random rng) =>
        $"- Format / genre: {Pick(Formats, rng)}\n"
        + $"- Composition: {Pick(Compositions, rng)}\n"
        + $"- Content block (use THIS, do not default to a blue panel): {Pick(ContentBlocks, rng)}\n"
        + $"- Focal point (make THIS pop above all else): {Pick(FocalPoints, rng)}\n"
        + $"- Typography: {Pick(Typography, rng)}\n"
        + $"- Script accent: {Pick(ScriptAccents, rng)}\n"
        + $"- Visual style: {Pick(VisualStyles, rng)}\n"
        + $"- Mood: {Pick(Moods, rng)}\n"
        + $"- Hero focus: {Pick(Heroes, rng)}";

    private static string Pick(string[] arr, Random rng) => arr[rng.Next(arr.Length)];
}
