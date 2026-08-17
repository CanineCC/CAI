using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// What one more participant costs the standard, counted while it happens.
/// </summary>
/// <remarks>
/// <para>★★ #23-3 DELIBERATELY ASSERTS NO FIGURE, because none has been measured, and says to measure it during
/// the first full period. That only happens if the counters exist before the period opens — afterwards it is an
/// estimate reconstructed from memory, which is exactly the kind of number this standard exists not to publish.
/// </para>
///
/// <para>★★ AND IT IS ATTRIBUTED FROM THE FINDING, not declared by the caller. The judging cascade runs over one
/// tool's findings, and the findings are stored (#23) — so the tool is looked up rather than asserted. A cost the
/// caller attributes is a cost the caller can attribute to somebody else.</para>
///
/// <para>★ WHAT IS NOT COUNTED IS STATED. Human time, our own engine's compute and the corpus hosting are not in
/// these numbers, and a marginal-cost figure that quietly omitted them would read as the whole cost of
/// participation.</para>
/// </remarks>
public sealed class ParticipantCostTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"cost-{caller}";

    /// <summary>Submit a run so its findings exist, and return their derived ids.</summary>
    private async Task<List<string>> SubmitAsync(string tool)
    {
        using var client = fx.Client();
        var holdout = JsonDocument.Parse(await client.GetStringAsync("/api/noise/holdout/2026-09", Ct))
            .RootElement.GetProperty("repositories").EnumerateArray()
            .Select(r => (Repo: r.GetProperty("repoId").GetString()!, Sha: r.GetProperty("pinnedSha").GetString()!))
            .ToList();

        var response = await client.PostAsJsonAsync("/api/noise/submissions", new
        {
            period = "2026-09",
            tool,
            toolVersion = "engine-1.0",
            runStartedAt = "2026-08-20T09:00:00Z",
            configuration = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
            recency = holdout.Select(h => new { repoId = h.Repo, stratum = "never-trained" }),

            // ★★ THE LINE IS PER-TOOL ON PURPOSE. Two tools submitting the same coordinates produce the SAME
            // derived finding id — which is right, and is what makes cross-vendor matching possible — so a test
            // that wanted one tool's own findings had to give them their own coordinates. The shared case has its
            // own test below, because it is the interesting one for cost.
            findings = holdout.Select(h => new
            {
                repoId = h.Repo, pinnedSha = h.Sha, filePath = $"src/{tool}.cs", line = 42,
                ruleId = "D4", title = "a finding", claimClass = "pointwise",
            }),
            reportedFindingCount = holdout.Count,
        }, Ct);

        return [.. JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct))
            .RootElement.GetProperty("findingIds").EnumerateArray().Select(x => x.GetString()!)];
    }

    private static object Vote(string verdict, double? seconds = null, int? inTok = null, int? outTok = null) => new
    {
        judge = "j" + Guid.NewGuid().ToString("N")[..4],
        verdict,
        model = "claude", modelVersion = "opus-5", promptId = "p1", reasoning = "because",
        modelFamily = "anthropic", temperature = 0.0,
        modelSeconds = seconds, inputTokens = inTok, outputTokens = outTok,
    };

    private async Task JudgeAsync(string period, string findingId, double seconds, int inTok, int outTok)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
        {
            period,
            findingId,
            round1 = new[]
            {
                Vote("noise", seconds, inTok, outTok),
                Vote("noise", seconds, inTok, outTok),
            },
        }, Ct);

        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonElement> CostAsync(string period)
    {
        using var client = fx.Client();
        return JsonDocument.Parse(await client.GetStringAsync($"/api/noise/cost/{period}", Ct))
            .RootElement.Clone();
    }

    [Fact]
    public async Task STAR_Model_Time_Is_Counted_Per_PARTICIPANT_And_Attributed_From_The_Finding()
    {
        // ★★ The cascade judges ONE TOOL'S findings, so the cost of judging belongs to that tool — looked up
        // from the stored finding rather than taken from the caller. A caller-declared tool is a cost that can
        // be attributed to somebody else, which on a per-participant figure is the whole game.
        var findings = await SubmitAsync("cost-alpha");
        await JudgeAsync("2026-09", findings[0], seconds: 4.5, inTok: 1_000, outTok: 200);
        await JudgeAsync("2026-09", findings[1], seconds: 2.5, inTok: 500, outTok: 100);

        var body = await CostAsync("2026-09");
        var mine = body.GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("tool").GetString() == "cost-alpha");

        // Two findings, two judges each — four votes.
        mine.GetProperty("judgements").GetInt32().ShouldBeFour();
        Assert.Equal(14.0, mine.GetProperty("modelSeconds").GetDouble(), 3);
        Assert.Equal(3_000, mine.GetProperty("inputTokens").GetInt32());
        Assert.Equal(600, mine.GetProperty("outputTokens").GetInt32());

        // ★★ AND ALL OF IT IS MARGINAL: nobody else reported these findings, so none of it would have been spent
        // had this participant stayed home. That is the figure #23-3 asks for.
        mine.GetProperty("judgementsSolelyYours").GetInt32().ShouldBeFour();
        Assert.Equal(14.0, mine.GetProperty("modelSecondsSolelyYours").GetDouble(), 3);
    }

    [Fact]
    public async Task STAR_A_Judgement_On_An_Unknown_Finding_Is_UNATTRIBUTED_Not_Dropped()
    {
        // ★★ A cost nobody can attribute must not vanish: the total would then be smaller than what was spent,
        // and a per-participant figure computed from it would understate every participant. Published as its own
        // row, so the gap is visible rather than absorbed.
        await JudgeAsync(Period(), "no-such-finding-here", seconds: 3.0, inTok: 100, outTok: 10);

        var body = await CostAsync(Period());
        Assert.Empty(body.GetProperty("participants").EnumerateArray());

        var unattributed = body.GetProperty("unattributed");
        Assert.Equal(2, unattributed.GetProperty("judgements").GetInt32());
        Assert.Equal(6.0, unattributed.GetProperty("modelSeconds").GetDouble(), 3);
    }

    [Fact]
    public async Task STAR_A_Judgement_That_Reports_No_Model_Time_Is_COUNTED_And_Flagged()
    {
        // ★★ Zero would be a lie and dropping it would shrink the denominator. The judgement happened, so it
        // counts as a judgement — and the number of judgements with no time reported publishes beside the
        // seconds, because a mean over half the votes is not the mean anybody will quote.
        var findings = await SubmitAsync("cost-silent");
        using var client = fx.Client();
        await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
        {
            period = "2026-09",
            findingId = findings[0],
            round1 = new[] { Vote("noise"), Vote("noise") },
        }, Ct);

        var mine = (await CostAsync("2026-09")).GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("tool").GetString() == "cost-silent");

        Assert.Equal(2, mine.GetProperty("judgements").GetInt32());
        Assert.Equal(2, mine.GetProperty("judgementsWithNoTimeReported").GetInt32());
        Assert.Equal(0.0, mine.GetProperty("modelSeconds").GetDouble(), 3);
    }

    [Fact]
    public async Task STAR_Crowd_Items_Rated_Are_Counted_Per_Participant_Too()
    {
        // ★ The other half of the marginal cost is human: how many crowd items were rated on this tool's
        // findings. Counted as items, never as money — nobody has priced a rater's minute, and a currency
        // figure invented here would be quoted as the cost of the crowd.
        var findings = await SubmitAsync("cost-crowd");

        using var client = fx.Client();
        await client.PostAsJsonAsync("/api/noise/crowd/queue", new
        {
            period = "2026-09",
            seed = "cost-seed",
            spotCheck = 20,
            candidates = findings.Select(id => new { findingId = id, state = "accepted", ownerId = "acme" }),
        }, Ct);

        var offered = JsonDocument.Parse(await client.GetStringAsync(
            "/api/noise/crowd/next?period=2026-09&raterId=cost-rater", Ct)).RootElement
            .GetProperty("findingId").GetString()!;

        await client.PostAsJsonAsync("/api/noise/crowd/answers", new
        {
            period = "2026-09", raterId = "cost-rater", findingId = offered, verdict = "noise",
        }, Ct);

        var mine = (await CostAsync("2026-09")).GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("tool").GetString() == "cost-crowd");

        Assert.True(mine.GetProperty("crowdItemsRated").GetInt32() >= 1);
    }

    [Fact]
    public async Task STAR_A_Finding_TWO_Tools_Reported_Is_Counted_For_BOTH_And_Marginal_For_NEITHER()
    {
        // ★★ THE CASE THAT CHANGED THE DESIGN. The first version attributed a judgement to the finding's stored
        // tool — the FIRST submitter — because two tools reporting one defect produce one finding row. That
        // overstates whoever submitted first and understates the other, and it was found by two tests colliding
        // on the same holdout coordinates rather than by reasoning about it.
        //
        // ★★ The judgement was spent on BOTH, so it counts for both — and it is marginal for NEITHER, because it
        // would have been judged had either one stayed home. That is the difference between "what was spent on
        // you" and "what would not have been spent without you", and #23-3 asks for the second.
        using var client = fx.Client();
        var holdout = JsonDocument.Parse(await client.GetStringAsync("/api/noise/holdout/2026-09", Ct))
            .RootElement.GetProperty("repositories").EnumerateArray()
            .Select(r => (Repo: r.GetProperty("repoId").GetString()!, Sha: r.GetProperty("pinnedSha").GetString()!))
            .First();

        string? sharedId = null;
        foreach (var tool in new[] { "cost-both-a", "cost-both-b" })
        {
            var response = await client.PostAsJsonAsync("/api/noise/submissions", new
            {
                period = "2026-09",
                tool,
                toolVersion = "engine-1.0",
                runStartedAt = "2026-08-20T09:00:00Z",
                configuration = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
                recency = new[] { new { repoId = holdout.Repo, stratum = "never-trained" } },
                findings = new[]
                {
                    new
                    {
                        repoId = holdout.Repo, pinnedSha = holdout.Sha, filePath = "src/Shared.cs", line = 7,
                        ruleId = "D4", title = "both of us found this", claimClass = "pointwise",
                    },
                },
                reportedFindingCount = 1,
            }, Ct);

            sharedId = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct))
                .RootElement.GetProperty("findingIds").EnumerateArray().Single().GetString();
        }

        await JudgeAsync("2026-09", sharedId!, seconds: 5.0, inTok: 10, outTok: 1);

        var participants = (await CostAsync("2026-09")).GetProperty("participants").EnumerateArray().ToList();

        foreach (var tool in new[] { "cost-both-a", "cost-both-b" })
        {
            var mine = participants.Single(p => p.GetProperty("tool").GetString() == tool);
            Assert.Equal(2, mine.GetProperty("judgements").GetInt32());
            Assert.Equal(10.0, mine.GetProperty("modelSeconds").GetDouble(), 3);

            // ★★ Marginal for neither.
            Assert.Equal(0, mine.GetProperty("judgementsSolelyYours").GetInt32());
            Assert.Equal(0.0, mine.GetProperty("modelSecondsSolelyYours").GetDouble(), 3);
        }
    }

    [Fact]
    public async Task STAR_The_Rows_SAY_That_They_Do_Not_Sum()
    {
        // ★ A reader adding the participants up would double-count every shared finding. The response says so
        // rather than leaving the arithmetic to be discovered — and names which figures DO add up.
        var body = await CostAsync(Period());
        var note = body.GetProperty("perParticipantFiguresOverlap").GetString()!;

        Assert.Contains("do not sum", note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("solelyYours", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_What_Is_NOT_Counted_Publishes_With_The_Figures()
    {
        // ★★ Human time, our own engine's compute and the corpus hosting are not in these numbers. A marginal
        // cost that quietly omitted them would be read as the cost of participation — and #23-3 asserts no
        // figure precisely because a partial one is worse than none.
        var body = await CostAsync(Period());

        var excluded = body.GetProperty("notCounted").GetString()!;
        Assert.Contains("human", excluded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hosting", excluded, StringComparison.OrdinalIgnoreCase);

        // ★ And no money. Nobody has priced any of this, and a currency figure here would be quoted.
        Assert.DoesNotContain("$", body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("usd", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Period_With_No_Judging_Reports_Zero_Participants_Rather_Than_Failing()
    {
        var body = await CostAsync("cost-quiet-period");

        Assert.Empty(body.GetProperty("participants").EnumerateArray());
        Assert.Equal(0, body.GetProperty("unattributed").GetProperty("judgements").GetInt32());
    }
}

/// <summary>Small readability helper — the four is the panel shape, not an arbitrary number.</summary>
internal static class FourAssert
{
    public static void ShouldBeFour(this int actual) => Assert.Equal(4, actual);
}
