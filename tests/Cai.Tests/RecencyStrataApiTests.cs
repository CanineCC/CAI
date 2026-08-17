using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The overfitting number: how much better a tool looks on code it was developed against.
/// </summary>
/// <remarks>
/// <para>★★ THE MOST INTERESTING FIGURE THE STANDARD PRODUCES, and one no vendor would publish about itself
/// unprompted. Every other number here can be improved by building a better tool; this one can only be
/// improved by building a tool that generalises. It was in the specification from the first draft and
/// implemented nowhere — so every published rate was silent on whether it described the instrument or the
/// vendor's familiarity with the sample.</para>
///
/// <para>★ "Has this tool seen this code before?" is a property of the TOOL, so it cannot be derived from the
/// draw. It is declared by the vendor and published by CAI, which is what makes it costly to get wrong.</para>
/// </remarks>
public sealed class RecencyStrataApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpStatusCode Status, JsonElement Body)> PublishAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/publication", payload, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Dictionary<string, object?> Complete() => new()
    {
        ["reported"] = 2000,
        ["adjudicated"] = 1900,
        ["excluded"] = 60,
        ["unrated"] = 40,
        ["validAndActionable"] = 900,
        ["validNotActionable"] = 560,
        ["noise"] = 440,
        ["clusters"] = 14,
        ["locCovered"] = 4_200_000L,
        ["recallEstimate"] = 0.62,
        ["recallMethod"] = "pooled-union",
        ["claimClasses"] = new object[] { new { claimClass = "pointwise", judged = 1900, noise = 440 } },
        ["toolVersion"] = "watchdog-engine 2026.08.3",
        ["holdoutSeed"] = "cai-2026-08-a1b2c3",
        ["modelSet"] = "judge-a@2026-07",
        ["gitMiningVerified"] = true,
        ["configuration"] = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
        ["fixRateUnavailable"] = "fixture",
    };

    [Fact]
    public async Task STAR_The_Pristine_Versus_Trained_Gap_Publishes()
    {
        // ★★ 24 % noise on code it has never seen against 12 % on code it was developed against: the rate a
        // reader would have been shown is the blend, and the blend is the least informative of the three.
        var run = Complete();
        run["recencyStrata"] = new object[]
        {
            new { stratum = "never-trained", judged = 500, noise = 120 },          // 24 %
            new { stratum = "trained-one-cycle-ago", judged = 900, noise = 110 },  // 12.2 %
            new { stratum = "trained-two-plus-cycles-ago", judged = 500, noise = 60 }, // 12 %
        };

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.OK, status);
        var recency = body.GetProperty("recency");
        Assert.True(recency.GetProperty("declared").GetBoolean());
        Assert.True(recency.GetProperty("hasPristineSlice").GetBoolean());

        // 0.24 − (170/1400) ≈ 0.1186 — a twelve-point gap, which is most of the number.
        var gap = recency.GetProperty("overfittingGapPoints").GetDouble();
        Assert.InRange(gap, 0.118, 0.119);
        Assert.True(recency.GetProperty("gapIsNotable").GetBoolean());
    }

    [Fact]
    public async Task STAR_No_Pristine_Slice_Says_So_Rather_Than_Reading_As_No_Overfitting()
    {
        // ★★ A missing gap and a gap of zero are OPPOSITE claims, and they look identical when one of them is
        // a blank. Without a never-trained endpoint the decay curve measures nothing, and "one cycle of
        // cooling off is enough" stays an assertion rather than a result.
        var run = Complete();
        run["recencyStrata"] = new object[]
        {
            new { stratum = "trained-one-cycle-ago", judged = 1400, noise = 300 },
            new { stratum = "trained-two-plus-cycles-ago", judged = 500, noise = 140 },
        };

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.OK, status);
        var recency = body.GetProperty("recency");
        Assert.True(recency.GetProperty("declared").GetBoolean());
        Assert.False(recency.GetProperty("hasPristineSlice").GetBoolean());
        Assert.Equal(JsonValueKind.Null, recency.GetProperty("overfittingGapPoints").ValueKind);
        Assert.Contains("pristine slice", recency.GetProperty("note").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_Declaring_Nothing_Is_Reported_As_Saying_Nothing()
    {
        // ★ Not a refusal — a first period can legitimately have no strata yet — but the silence is named, so
        // a reader is not left to assume the rate was checked for overfitting when it was not.
        var (status, body) = await PublishAsync(Complete());

        Assert.Equal(HttpStatusCode.OK, status);
        var recency = body.GetProperty("recency");
        Assert.False(recency.GetProperty("declared").GetBoolean());
        Assert.Contains("says nothing", recency.GetProperty("note").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Unrecognised_Stratum_Is_Refused_Rather_Than_Folded()
    {
        var run = Complete();
        run["recencyStrata"] = new object[] { new { stratum = "sort-of-trained", judged = 10, noise = 1 } };

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("sort-of-trained", body.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Small_Gap_Is_Not_Notable()
    {
        // ★ The threshold is stated rather than left to the reader, and it flags a run for reading — it never
        // voids one. The standard does not get to decide that a vendor overfitted.
        Assert.False(RecencyStrata.GapIsNotable(0.01));
        Assert.True(RecencyStrata.GapIsNotable(RecencyStrata.NotableGapPoints));
    }

    [Fact]
    public void STAR_The_Gap_Is_Signed_Because_The_Sign_Means_Something()
    {
        // ★★ Positive is the overfitting direction: noisier on code it has never seen. NEGATIVE is odd enough
        // to be worth a look rather than a boast — it usually means the trained slice is harder, not that
        // familiarity hurt. Folding to an absolute value would lose exactly that.
        var overfitted = RecencyStrata.OverfittingGap(
        [
            new RecencyTally(RecencyStratum.NeverTrained, 100, 30),
            new RecencyTally(RecencyStratum.TrainedOneCycleAgo, 100, 10),
        ]);
        var inverted = RecencyStrata.OverfittingGap(
        [
            new RecencyTally(RecencyStratum.NeverTrained, 100, 10),
            new RecencyTally(RecencyStratum.TrainedOneCycleAgo, 100, 30),
        ]);

        Assert.True(overfitted > 0);
        Assert.True(inverted < 0);
    }
}
