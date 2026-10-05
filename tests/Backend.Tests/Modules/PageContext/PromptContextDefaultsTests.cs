using Backend.Modules.PageContext;
using Xunit;

namespace Backend.Tests.Modules.PageContext;

/// <summary>Fallback ngữ cảnh thương hiệu dùng chung bởi Full AI và caption từ ảnh (R-029).</summary>
public sealed class PromptContextDefaultsTests
{
    [Fact]
    public void PageContextWins_AndValuesAreTrimmed()
    {
        var context = new PageContextModel
        {
            BrandName = "  VNi Edu ", ToneOfVoice = " gần gũi ", CtaText = " Gọi ngay ", DefaultHashtags = " #a #b ",
            Hotline = " 1900 ", Website = " vni.edu.vn ", BrandColors = " #fff ",
        };

        var d = PromptContextDefaults.From(context, "Tên kênh", "Chung");

        Assert.Same(context, d.PageContext);
        Assert.Equal(("VNi Edu", "gần gũi", "Gọi ngay", "#a #b"), (d.Brand, d.Tone, d.Cta, d.Hashtags));
        Assert.Equal(("1900", "vni.edu.vn", "#fff"), (d.Hotline, d.Website, d.BrandColors));
        Assert.True(d.HasRealBrand);
    }

    [Fact]
    public void WithoutPageContext_BrandIsTheChannelName_AndTheRestUsesDefaults()
    {
        var d = PromptContextDefaults.From(null, "Page Alpha", "Chung");

        Assert.Equal("Page Alpha", d.Brand);
        Assert.True(d.HasRealBrand);
        Assert.Equal(PromptContextDefaults.FallbackTone, d.Tone);
        Assert.Equal("Inbox ngay để được tư vấn chi tiết nhé 💬", d.Cta);
        Assert.Equal("#Chung #Facebook #Marketing #BanHang", d.Hashtags);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (d.Hotline, d.Website, d.BrandColors));
    }

    [Fact]
    public void NoPageAtAll_UsesThePlaceholderBrand_WhichIsNotARealBrand()
    {
        var d = PromptContextDefaults.From(null, null, "Chung");

        Assert.Equal("Page của bạn", d.Brand);
        Assert.False(d.HasRealBrand);
    }

    [Fact]
    public void WebsiteFallsBackToTheCtaUrl()
    {
        var d = PromptContextDefaults.From(new PageContextModel { CtaUrl = " https://x.vn " }, null, "Chung");

        Assert.Equal("https://x.vn", d.Website);
    }

    [Theory]
    [InlineData("Tin tức/Khuyến mãi", "#Tin_tức_Khuyến_mãi #Facebook #Marketing #BanHang")]
    [InlineData("  Thời   trang  ", "#Thời_trang #Facebook #Marketing #BanHang")]
    [InlineData("!!!", "#Facebook #Marketing #BanHang")]
    [InlineData("", "#Facebook #Marketing #BanHang")]
    public void FallbackHashtags_AreBuiltFromTheCategory(string category, string expected)
        => Assert.Equal(expected, PromptContextDefaults.BuildFallbackHashtags(category));
}
