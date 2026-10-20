using Backend.Modules.Campaign.FullMedia;
using Xunit;

namespace Backend.Tests.Modules.Campaign.FullMedia;

/// <summary>R-047: quy tắc dùng lại điểm đã lưu (FullMediaScoreReuse.IsReusable) — thuần, không DB.</summary>
public sealed class FullMediaScoreReuseTests
{
    private static CampaignMediaScoreModel Row(int? yes, int? cast, int? total)
        => new() { VotesYes = yes, VotesCast = cast, VotesTotal = total };

    [Theory]
    [InlineData(0.30)]
    [InlineData(0.66)]
    [InlineData(0.94)]
    [InlineData(1.0)]
    public void Reuse_CompleteScore_Reused(double min)
        => Assert.True(FullMediaScoreReuse.IsReusable(Row(2, 3, 3), min)); // cast == total ⇒ đầy đủ, đúng ở mọi ngưỡng

    [Fact]
    public void Reuse_NullVotes_TreatedAsComplete()
        => Assert.True(FullMediaScoreReuse.IsReusable(Row(null, null, null), 0.1));

    [Fact]
    public void Reuse_EarlyExitScore_StillBelowThreshold_Reused()
        => Assert.True(FullMediaScoreReuse.IsReusable(Row(0, 1, 3), 0.94)); // upper = 2/3

    [Fact]
    public void Reuse_EarlyExitScore_ThresholdLowered_Rescored()
        => Assert.False(FullMediaScoreReuse.IsReusable(Row(0, 1, 3), 0.66)); // 0.667 ≥ 0.66

    [Fact]
    public void Reuse_ThresholdRaised_StillReusesEarlyExit()
    {
        var row = Row(1, 2, 3); // upper = 2/3
        Assert.False(FullMediaScoreReuse.IsReusable(row, 0.5));
        Assert.True(FullMediaScoreReuse.IsReusable(row, 0.7));
        Assert.True(FullMediaScoreReuse.IsReusable(row, 0.94));
    }

    [Fact]
    public void Reuse_UpperBound_CountsRemainingVotesAsYes()
    {
        // yes=1 cast=2 total=4 → upper = (1+2)/4 = 0.75; thiếu phần còn lại sẽ ra 0.25.
        Assert.False(FullMediaScoreReuse.IsReusable(Row(1, 2, 4), 0.5));
        Assert.True(FullMediaScoreReuse.IsReusable(Row(1, 2, 4), 0.8));
    }

    [Fact]
    public void Reuse_BoundaryUpperEqualsThreshold_Rescored()
    {
        var row = Row(1, 2, 4); // upper đúng 0.75
        Assert.False(FullMediaScoreReuse.IsReusable(row, 0.75));   // Passes(0.75, 0.75) = true ⇒ chấm lại
        Assert.True(FullMediaScoreReuse.IsReusable(row, 0.7501));

        // Không làm tròn: upper 0.9399 so với 0.94 và 0.9399.
        var near = Row(0, 601, 10000); // upper = 9399/10000
        Assert.True(FullMediaScoreReuse.IsReusable(near, 0.94));
        Assert.False(FullMediaScoreReuse.IsReusable(near, 0.9399));
    }

    [Theory]
    [InlineData(0, 0, 0)]    // VotesTotal == 0
    [InlineData(0, 0, -3)]
    [InlineData(1, 4, 3)]    // Cast > Total
    [InlineData(3, 2, 3)]    // Yes > Cast
    [InlineData(-1, 1, 3)]
    [InlineData(0, -1, 3)]
    public void Reuse_VotesTotalZeroOrAbnormal_FailClosed_Rescored(int yes, int cast, int total)
        => Assert.False(FullMediaScoreReuse.IsReusable(Row(yes, cast, total), 0.94));

    [Theory]
    [InlineData(1, null, null)]
    [InlineData(null, 2, 3)]
    [InlineData(1, 2, null)]
    public void Reuse_PartialNulls_FailClosed_Rescored(int? yes, int? cast, int? total)
        => Assert.False(FullMediaScoreReuse.IsReusable(Row(yes, cast, total), 0.94));
}
