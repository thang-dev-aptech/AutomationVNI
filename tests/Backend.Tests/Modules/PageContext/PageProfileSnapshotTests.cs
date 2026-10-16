using Backend.Modules.PageContext;
using Xunit;

namespace Backend.Tests.Modules.PageContext;

/// <summary>FULLMEDIA t1 / fm-ac-page-profile-hash: ProfileHash ổn định, bỏ qua màu/hotline/website.</summary>
public class PageProfileSnapshotTests
{
    private static PageContextModel Sample() => new()
    {
        BrandName = "  VNi  Education  ",
        ToneOfVoice = " gần gũi  ",
        CtaText = " Inbox ngay ",
        Topics = "  chứng chỉ ISO  ",
        Exclusions = "  chính trị  ",
        BrandColors = "#FF0000",
        Hotline = "1900",
        Website = "https://vni.edu.vn",
        LogoMediaId = Guid.NewGuid(),
    };

    [Fact]
    public void Build_Twice_SameTextAndHash()
    {
        var page = Sample();

        var a = PageProfileSnapshot.Build(page);
        var b = PageProfileSnapshot.Build(page);

        Assert.Equal(a.Text, b.Text);
        Assert.Equal(a.Hash, b.Hash);
        Assert.False(string.IsNullOrWhiteSpace(a.Text));
        Assert.Equal(64, a.Hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", a.Hash);
        Assert.True(a.IsScoreable);
        Assert.Contains("VNi Education", a.Text, StringComparison.Ordinal);
        Assert.Contains("gần gũi", a.Text, StringComparison.Ordinal);
        Assert.Contains("Inbox ngay", a.Text, StringComparison.Ordinal);
        Assert.Contains("chứng chỉ ISO", a.Text, StringComparison.Ordinal);
        Assert.Contains("chính trị", a.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("#FF0000", a.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("1900", a.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("vni.edu.vn", a.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangingBrandColorsHotlineWebsite_DoesNotChangeHash()
    {
        var page = Sample();
        var baseline = PageProfileSnapshot.Build(page);

        page.BrandColors = "#00FF00";
        page.Hotline = "1800";
        page.Website = "https://other.example";
        page.LogoMediaId = Guid.NewGuid();
        page.CtaUrl = "https://cta.example";
        page.DefaultHashtags = "#x";

        var after = PageProfileSnapshot.Build(page);

        Assert.Equal(baseline.Hash, after.Hash);
        Assert.Equal(baseline.Text, after.Text);
    }

    [Fact]
    public void ChangingToneTopicsExclusionsBrandCta_ChangesHash()
    {
        var page = Sample();
        var baseline = PageProfileSnapshot.Build(page);

        page.ToneOfVoice = "chuyên nghiệp";
        Assert.NotEqual(baseline.Hash, PageProfileSnapshot.Build(page).Hash);

        page = Sample();
        page.Topics = "tuyển sinh";
        Assert.NotEqual(baseline.Hash, PageProfileSnapshot.Build(page).Hash);

        page = Sample();
        page.Exclusions = "bạo lực";
        Assert.NotEqual(baseline.Hash, PageProfileSnapshot.Build(page).Hash);

        page = Sample();
        page.BrandName = "Thương hiệu khác";
        Assert.NotEqual(baseline.Hash, PageProfileSnapshot.Build(page).Hash);

        page = Sample();
        page.CtaText = "Đăng ký ngay";
        Assert.NotEqual(baseline.Hash, PageProfileSnapshot.Build(page).Hash);
    }

    [Fact]
    public void EmptyTopicsAndTone_NotScoreable()
    {
        var emptyBoth = PageProfileSnapshot.Build(new PageContextModel
        {
            BrandName = "VNi",
            CtaText = "CTA",
            Topics = "  ",
            ToneOfVoice = null,
        });
        Assert.False(emptyBoth.IsScoreable);

        var topicsOnly = PageProfileSnapshot.Build(new PageContextModel
        {
            BrandName = "VNi",
            Topics = "ISO",
            ToneOfVoice = "  ",
        });
        Assert.True(topicsOnly.IsScoreable);

        var toneOnly = PageProfileSnapshot.Build(new PageContextModel
        {
            BrandName = "VNi",
            Topics = null,
            ToneOfVoice = "gần gũi",
        });
        Assert.True(toneOnly.IsScoreable);
    }
}
