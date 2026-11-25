using Backend.Modules.Campaign.FullMedia;
using Backend.Modules.MediaAsset;
using Xunit;

namespace Backend.Tests.Modules.Campaign.FullMedia;

public sealed class TagConsistencyTests
{
    [Fact]
    public void EmptyMediaTags_IsConsistent()
    {
        var media = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = "x.png",
            OriginalFileName = "x.png",
            StoragePath = "x",
            MimeType = "image/png",
            Tags = null,
            Description = null,
            AltText = null,
        };
        var vision = Desc(keywords: ["mèo"], topic: "thú cưng");

        Assert.True(TagConsistency.IsConsistent(vision, media));
    }

    [Fact]
    public void EmptyVisionTokens_IsConsistent()
    {
        var media = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = "x.png",
            OriginalFileName = "x.png",
            StoragePath = "x",
            MimeType = "image/png",
            Description = "Ảnh mèo đáng yêu",
        };
        var vision = Desc(keywords: [], topic: "", setting: "");

        Assert.True(TagConsistency.IsConsistent(vision, media));
    }

    [Fact]
    public void OverlappingTokens_CaseAndDiacritics_IsConsistent()
    {
        var media = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = "x.png",
            OriginalFileName = "x.png",
            StoragePath = "x",
            MimeType = "image/png",
            Description = "MÈO đáng yêu",
        };
        var vision = Desc(keywords: ["Meo"], topic: "thú cưng");

        Assert.True(TagConsistency.IsConsistent(vision, media));
    }

    [Fact]
    public void NoOverlappingTokens_IsInconsistent()
    {
        var media = new MediaAssetModel
        {
            Id = Guid.NewGuid(),
            FileName = "x.png",
            OriginalFileName = "x.png",
            StoragePath = "x",
            MimeType = "image/png",
            Description = "Ảnh mèo đáng yêu",
        };
        var vision = Desc(keywords: ["xe hơi", "đường phố"], topic: "ô tô", setting: "phố");

        Assert.False(TagConsistency.IsConsistent(vision, media));
    }

    [Fact]
    public void ToState_FixedFormat()
    {
        var withKeywords = new VisionDescription(
            Topic: "mèo",
            Audience: "gia đình",
            Tone: "ấm áp",
            Setting: "nhà",
            TextInImage: "Sale 50%",
            Keywords: ["mèo", "thú cưng"],
            Confidence: 0.9,
            Unclear: false,
            Model: "m");
        var lines = withKeywords.ToState().Split('\n');
        Assert.Equal(6, lines.Length);
        Assert.Equal("Chủ đề: mèo", lines[0]);
        Assert.Equal("Đối tượng: gia đình", lines[1]);
        Assert.Equal("Tông: ấm áp", lines[2]);
        Assert.Equal("Bối cảnh: nhà", lines[3]);
        Assert.Equal("Chữ trong ảnh: Sale 50%", lines[4]);
        Assert.Equal("Từ khoá: mèo, thú cưng", lines[5]);

        var emptyKw = withKeywords with { Keywords = Array.Empty<string>() };
        var emptyLines = emptyKw.ToState().Split('\n');
        Assert.Equal("Từ khoá: ", emptyLines[5]);
    }

    private static VisionDescription Desc(
        string[]? keywords = null,
        string topic = "mèo",
        string setting = "nhà")
        => new(
            Topic: topic,
            Audience: "gia đình",
            Tone: "ấm áp",
            Setting: setting,
            TextInImage: "",
            Keywords: keywords ?? ["mèo"],
            Confidence: 0.9,
            Unclear: false,
            Model: "t");
}
