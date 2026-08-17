using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The mark: mechanical, revocable, free to earn, and never the word "certified".
/// </summary>
/// <remarks>
/// <para>★★ A MARK THAT CAN BE PULLED IS POWER OVER A COMPETITOR'S MARKETING, and pulling one is far more
/// newsworthy than granting one. So every condition is mechanical — each is a fact CAI already holds — and
/// revocation names the condition that failed. A judgement call anywhere in here would be an unreviewable veto
/// held by a participant over its rivals.</para>
///
/// <para>★★ "CAI-MEASURED", NEVER "CAI-CERTIFIED". Certification implies an audit of the tool; this is a record
/// that a run was measured under a published method. The difference is the entire liability position, and it is
/// the kind of word that spreads from one careless string into everyone's marketing.</para>
///
/// <para>★ FREE, and any change to that takes effect no earlier than one full published period after it is
/// announced — never for a period already opened. A fee introduced against a period people have already run is a
/// fee they cannot decline.</para>
/// </remarks>
public sealed class ComplianceMarkTests
{
    private static MarkInputs All(bool? ran = true, bool? inTime = true, bool? reproduces = true,
        bool? published = true) => new(
        RanAgainstTheHoldout: ran ?? false,
        SubmittedBeforeTheDeadline: inTime ?? false,
        RunReproduces: reproduces ?? false,
        NumbersPublishedInFull: published ?? false);

    [Fact]
    public void STAR_All_Four_Conditions_Grant_The_Mark()
    {
        var mark = ComplianceMark.Evaluate("watchdog", "2026-09", All());

        Assert.True(mark.Granted);
        Assert.Empty(mark.Failing);

        // ★ Every condition is listed whether or not it held — a mark that showed only the failures would leave
        // a reader unable to see what it was actually checked against.
        Assert.Equal(4, mark.Conditions.Count);
    }

    [Fact]
    public void STAR_ANY_Condition_Failing_Withholds_It_And_NAMES_The_Condition()
    {
        // ★★ Named, not summarised. "You did not qualify" is a verdict nobody can act on or contest, and this is
        // the message a competitor reads about itself in public.
        var mark = ComplianceMark.Evaluate("rival", "2026-09", All(reproduces: false));

        Assert.False(mark.Granted);
        var failed = Assert.Single(mark.Failing);
        Assert.Equal(ComplianceMark.RunReproduces, failed.Condition);
        Assert.False(string.IsNullOrWhiteSpace(failed.Why));
    }

    [Fact]
    public void STAR_Every_Condition_Is_MECHANICAL_And_Says_What_It_Reads()
    {
        // ★★ The whole defence against "an unreviewable veto": each condition names the fact CAI already holds
        // that decides it. A condition worded as a judgement — "the run looks credible" — could not be checked by
        // the tool it was applied to.
        foreach (var condition in ComplianceMark.Conditions)
        {
            Assert.False(string.IsNullOrWhiteSpace(condition.Reads),
                $"'{condition.Name}' does not say what fact decides it");
        }

        Assert.Equal(4, ComplianceMark.Conditions.Count);
    }

    [Fact]
    public void STAR_Revocation_Is_A_STATED_Condition_Failing_And_Is_Appealable()
    {
        // ★★ A mark held and then pulled is the newsworthy event, so the reasoning travels with it and the appeal
        // route is named in the same breath. Revoking without either is the veto this design exists to prevent.
        var granted = ComplianceMark.Evaluate("watchdog", "2026-09", All());
        var revoked = ComplianceMark.Evaluate("watchdog", "2026-09", All(published: false));

        Assert.True(granted.Granted);
        Assert.False(revoked.Granted);
        Assert.Contains(ComplianceMark.NumbersPublishedInFull, revoked.Failing.Select(f => f.Condition));
        Assert.Contains("appeal", revoked.AppealRoute, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void STAR_The_Mark_Is_FREE_And_A_Change_Cannot_Reach_An_Open_Period()
    {
        // ★★ A fee introduced against a period people have already run is a fee they cannot decline — they have
        // spent the compute. So the commitment is not "we intend to keep it free", it is a rule about WHEN a
        // change may take effect, and it publishes.
        Assert.True(ComplianceMark.Free);
        Assert.Contains("one full published period", ComplianceMark.ChangeRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("already opened", ComplianceMark.ChangeRule, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void STAR_The_WORDING_Is_Measured_Never_Certified()
    {
        // ★★ Certification implies an audit of the tool; this is a record that a run was measured under a
        // published method, and the difference is the entire liability position. Asserted on every string the
        // mark publishes, because this is exactly the word that leaks from one careless label into marketing.
        var strings = new List<string>
        {
            ComplianceMark.Label, ComplianceMark.ChangeRule, ComplianceMark.WordingRule,
            ComplianceMark.Evaluate("watchdog", "2026-09", All()).Statement,
            ComplianceMark.Evaluate("watchdog", "2026-09", All(ran: false)).Statement,
        };
        strings.AddRange(ComplianceMark.Conditions.Select(c => c.Reads));
        strings.AddRange(ComplianceMark.Conditions.Select(c => c.Name));

        Assert.Contains("measured", ComplianceMark.Label, StringComparison.OrdinalIgnoreCase);
        foreach (var s in strings)
        {
            Assert.DoesNotContain("certif", s, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void STAR_A_Withheld_Mark_Is_Not_A_Verdict_On_The_TOOL()
    {
        // ★ Three of the four conditions are about PROCESS — did you run the right corpus, in time, and publish.
        // A tool with a terrible noise rate that did all three earns the mark, and that is correct: the mark says
        // the measurement happened properly, and the RATE says how it went. Conflating them would make the mark a
        // quality badge nobody voted for.
        var noisy = ComplianceMark.Evaluate("noisy-but-honest", "2026-09", All());

        Assert.True(noisy.Granted);
        Assert.Contains("not a statement about how good", noisy.Statement, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The mark over the wire: decided from what the store holds, and asked of nobody.
/// </summary>
/// <remarks>
/// ★★ NOTHING IS DECLARED. The four conditions read the receipt, the deadline against the receipt's timestamp, and
/// whether a publication exists — all facts CAI recorded when they happened. A mark that needed an input would be
/// a mark somebody could argue for, which is the veto this design exists to prevent.
/// </remarks>
public sealed class ComplianceMarkApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonElement> MarkAsync(string period)
    {
        using var client = fx.Client();
        return JsonDocument.Parse(await client.GetStringAsync($"/api/noise/mark/{period}", Ct))
            .RootElement.Clone();
    }

    private async Task SubmitAsync(string tool, bool valid)
    {
        using var client = fx.Client();
        var holdout = JsonDocument.Parse(await client.GetStringAsync("/api/noise/holdout/2026-09", Ct))
            .RootElement.GetProperty("repositories").EnumerateArray()
            .Select(r => (Repo: r.GetProperty("repoId").GetString()!, Sha: r.GetProperty("pinnedSha").GetString()!))
            .ToList();

        await client.PostAsJsonAsync("/api/noise/submissions", new
        {
            period = "2026-09",
            tool,
            toolVersion = "engine-1.0",
            runStartedAt = "2026-08-20T09:00:00Z",
            configuration = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
            recency = holdout.Select(h => new { repoId = h.Repo, stratum = "never-trained" }),

            // ★ The invalid run cites a sha the holdout does not pin — a real verification failure rather than a
            // flag somebody set.
            findings = holdout.Select(h => new
            {
                repoId = h.Repo,
                pinnedSha = valid ? h.Sha : new string('f', 40),
                filePath = "src/Thing.cs", line = 42, ruleId = "D4", title = "a finding",
                claimClass = "pointwise",
            }),
            reportedFindingCount = holdout.Count,
        }, Ct);
    }

    [Fact]
    public async Task STAR_A_Run_That_Fails_VERIFICATION_Does_Not_Earn_The_Mark()
    {
        // ★★ The condition is read from the receipt, not asserted. This run cites shas the draw does not pin, so
        // the receipt is refused — and the mark says which condition that was.
        await SubmitAsync("wrong-shas", valid: false);

        var body = await MarkAsync("2026-09");
        var mark = body.GetProperty("marks").EnumerateArray()
            .Single(m => m.GetProperty("tool").GetString() == "wrong-shas");

        Assert.False(mark.GetProperty("granted").GetBoolean());
        Assert.Contains(
            ComplianceMark.RunReproduces,
            mark.GetProperty("failing").EnumerateArray().Select(f => f.GetProperty("condition").GetString()));
    }

    [Fact]
    public async Task STAR_A_Period_With_No_Submissions_States_That_Rather_Than_Granting_Nothing_Silently()
    {
        var body = await MarkAsync("mark-quiet-period");

        Assert.Empty(body.GetProperty("marks").EnumerateArray());
        Assert.Contains("no tool has submitted", body.GetProperty("note").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Conditions_And_The_FREE_Commitment_Publish_With_It()
    {
        // ★ A mark whose conditions are not published is one nobody can predict or contest, and the free
        // commitment is worth nothing if the rule about changing it is not beside it.
        var body = await MarkAsync("2026-09");

        Assert.Equal(ComplianceMark.Label, body.GetProperty("label").GetString());
        Assert.True(body.GetProperty("free").GetBoolean());
        Assert.Equal(4, body.GetProperty("conditions").GetArrayLength());
        Assert.Contains("one full published period", body.GetProperty("changeRule").GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("appeal", body.GetProperty("appealRoute").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_Nothing_The_Endpoint_Publishes_Says_CERTIFIED()
    {
        // ★★ The word leaks from one careless string into everyone's marketing, so it is asserted over the whole
        // response rather than over the strings I remembered to check.
        using var client = fx.Client();
        var raw = await client.GetStringAsync("/api/noise/mark/2026-09", Ct);

        Assert.DoesNotContain("certif", raw, StringComparison.OrdinalIgnoreCase);
    }
}
