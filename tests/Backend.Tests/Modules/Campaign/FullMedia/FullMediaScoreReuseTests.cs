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

    // --- Classify V/R/U ---
    private static CampaignMediaScoreModel ScoreRow(double score, int? yes = null, int? cast = null, int? total = null, string? hash = "h")
        => new() { Score = score, VotesYes = yes, VotesCast = cast, VotesTotal = total, ProfileHash = hash };

    [Fact]
    public void Classify_AllValid_V()
        => Assert.Equal(FullMediaScoreClass.Valid,
            FullMediaScoreReuse.Classify(ScoreRow(0.95, 3, 3, 3), ScoreRow(0.96, 3, 3, 3), "h", 0.94));

    [Fact]
    public void Classify_DefinitiveFail_R()
    {
        Assert.Equal(FullMediaScoreClass.RejectedDefinitively,
            FullMediaScoreReuse.Classify(ScoreRow(0.5, 1, 3, 3), ScoreRow(0.96, 3, 3, 3), "h", 0.94));
        Assert.Equal(FullMediaScoreClass.RejectedDefinitively,
            FullMediaScoreReuse.Classify(ScoreRow(0.95, 3, 3, 3), ScoreRow(0.5, 1, 3, 3), "h", 0.94));
        // Dừng sớm nhưng cận trên vẫn dưới ngưỡng hiện tại + điểm dưới ngưỡng -> R dứt khoát
        Assert.Equal(FullMediaScoreClass.RejectedDefinitively,
            FullMediaScoreReuse.Classify(ScoreRow(0.0, 0, 1, 3), ScoreRow(0.96, 3, 3, 3), "h", 0.94));
    }

    [Fact]
    public void Classify_EarlyExitNowReachable_U()
        => Assert.Equal(FullMediaScoreClass.Unknown,
            FullMediaScoreReuse.Classify(ScoreRow(0.0, 0, 1, 3), ScoreRow(0.96, 3, 3, 3), "h", 0.66));

    [Fact]
    public void Classify_EarlyExitStillBelow_R()
        => Assert.Equal(FullMediaScoreClass.RejectedDefinitively,
            FullMediaScoreReuse.Classify(ScoreRow(0.0, 0, 1, 3), ScoreRow(0.96, 3, 3, 3), "h", 0.94));

    [Fact]
    public void Classify_MissingScore_U()
    {
        Assert.Equal(FullMediaScoreClass.Unknown,
            FullMediaScoreReuse.Classify(null, ScoreRow(0.96, 3, 3, 3), "h", 0.94));
        Assert.Equal(FullMediaScoreClass.Unknown,
            FullMediaScoreReuse.Classify(ScoreRow(0.95, 3, 3, 3), null, "h", 0.94));
    }

    [Fact]
    public void Classify_ProfileHashChanged_U()
    {
        Assert.Equal(FullMediaScoreClass.Unknown,
            FullMediaScoreReuse.Classify(ScoreRow(0.95, 3, 3, 3, hash: "old"), ScoreRow(0.96, 3, 3, 3), "h", 0.94));
        Assert.Equal(FullMediaScoreClass.Unknown,
            FullMediaScoreReuse.Classify(ScoreRow(0.95, 3, 3, 3, hash: null), ScoreRow(0.96, 3, 3, 3), "h", 0.94));
    }

    [Fact]
    public void Classify_NullVotesTreatedComplete_ByRule()
    {
        Assert.Equal(FullMediaScoreClass.Valid,
            FullMediaScoreReuse.Classify(ScoreRow(0.95), ScoreRow(0.96), "h", 0.94));
        Assert.Equal(FullMediaScoreClass.RejectedDefinitively,
            FullMediaScoreReuse.Classify(ScoreRow(0.5), ScoreRow(0.96), "h", 0.94));
    }

    [Fact]
    public void Classify_Boundary_UpperEqualsThreshold_U()
    {
        // upper = (1+1)/? dùng (yes=1,cast=2,total=4) upper=0.75 đúng ngưỡng -> U
        Assert.Equal(FullMediaScoreClass.Unknown,
            FullMediaScoreReuse.Classify(ScoreRow(0.25, 1, 2, 4), ScoreRow(0.96, 3, 3, 3), "h", 0.75));
        // 0.9399 không làm tròn lên 0.94: upper = 9399/10000 < 0.94 -> tin được, điểm dưới ngưỡng -> R
        Assert.Equal(FullMediaScoreClass.RejectedDefinitively,
            FullMediaScoreReuse.Classify(ScoreRow(0.0, 0, 601, 10000), ScoreRow(0.96, 3, 3, 3), "h", 0.94));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 4, 3)]
    [InlineData(3, 2, 3)]
    public void Classify_BadVotesData_FailClosed_U(int yes, int cast, int total)
        => Assert.Equal(FullMediaScoreClass.Unknown,
            FullMediaScoreReuse.Classify(ScoreRow(0.95, yes, cast, total), ScoreRow(0.96, 3, 3, 3), "h", 0.94));

    [Fact]
    public void Classify_SingleThresholdSite_UsesFitThresholdPasses()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var file = Path.Combine(root, "backend", "Modules", "Campaign", "FullMedia", "FullMediaScoreReuse.cs");
        var text = File.ReadAllText(file);
        Assert.Contains("FitThreshold.Passes", text);
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("///")) continue;
            Assert.False(
                System.Text.RegularExpressions.Regex.IsMatch(t, @"\.Score\s*>=|Score\s*>=|upper\s*>=|>=\s*currentMinFitScore|>=\s*min"),
                $"Raw threshold compare in FullMediaScoreReuse.cs: {t}");
        }
    }

    [Fact]
    public void NoPageSilentlyDropped_Invariant()
    {
        var rnd = new Random(1234);
        for (var i = 0; i < 300; i++)
        {
            CampaignMediaScoreModel? Mk()
            {
                if (rnd.Next(5) == 0) return null;
                var withVotes = rnd.Next(2) == 0;
                return ScoreRow(
                    Math.Round(rnd.NextDouble(), 2),
                    withVotes ? rnd.Next(0, 4) : null,
                    withVotes ? rnd.Next(0, 4) : null,
                    withVotes ? 3 : null,
                    rnd.Next(4) == 0 ? "old" : "h");
            }

            var p1 = Mk();
            var p2 = Mk();
            var min = new[] { 0.5, 0.66, 0.94 }[rnd.Next(3)];
            var cls = FullMediaScoreReuse.Classify(p1, p2, "h", min);
            // Mọi ứng viên rơi đúng 1 nhóm; (V) chỉ khi tin được + đạt cả hai; (U) không bao giờ là (V)/(R) âm thầm.
            if (cls == FullMediaScoreClass.Valid)
            {
                Assert.NotNull(p1); Assert.NotNull(p2);
                Assert.True(FullMediaScoreReuse.IsReusable(p1!, min));
                Assert.True(FullMediaScoreReuse.IsReusable(p2!, min));
                Assert.True(FitThreshold.Passes(p1!.Score, min));
                Assert.True(FitThreshold.Passes(p2!.Score, min));
            }

            if (p1 is not null && p1.ProfileHash != "h")
                Assert.Equal(FullMediaScoreClass.Unknown, cls);
            if (cls == FullMediaScoreClass.RejectedDefinitively)
            {
                Assert.NotNull(p1);
                Assert.True(FullMediaScoreReuse.IsReusable(p1!, min));
                Assert.True(p1.ProfileHash == "h");
            }
        }
    }
}
