using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The front door to the crowd: a public page that serves one item, its evidence, and the choices.
/// </summary>
/// <remarks>
/// <para>★★ THE ENDPOINTS EXISTED WITH NO FRONT DOOR. Rollout step 2 is "open the crowd — public,
/// cross-vendor raters, so 'is this noise?' is not our opinion even while we are the only participant", and
/// every part of that shipped as JSON reachable by a vendor's engineer with a curl. A crowd layer whose only
/// client is the organisation being measured is not a check on that organisation.</para>
///
/// <para>★★ AND THE ITEM MUST CARRY ITS EVIDENCE. <c>/api/noise/crowd/next</c> served a finding id and two
/// questions: a rater was being asked whether a hex string should have fired. Nothing about that answer is
/// worth having, and the round would still have produced an agreement rate.</para>
///
/// <para>★ NOT the tool, and not what the judges said. The disguise is the whole reason the spot-check can
/// catch a case where all four judges were wrong together.</para>
/// </remarks>
public sealed class PublicCrowdSurfaceTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"surface-{caller}";

    /// <summary>Submit a run, so the findings the crowd is asked about actually exist.</summary>
    /// <returns>The derived finding ids, in the order the holdout gave their repositories.</returns>
    private async Task<List<(string Id, string Repo, string Sha)>> SubmitAsync(string tool)
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
            findings = holdout.Select(h => new
            {
                repoId = h.Repo, pinnedSha = h.Sha, filePath = "src/Payments/Ledger.cs", line = 118,
                ruleId = "D4", title = "this method is long enough to be hard to review",
                claimClass = "pointwise",
            }),
            reportedFindingCount = holdout.Count,
        }, Ct);

        return [.. holdout.Select(h => (
            Id: FindingKey.For(h.Repo, h.Sha, "src/Payments/Ledger.cs", 118, "D4"),
            h.Repo,
            h.Sha))];
    }

    private async Task RegisterQueueAsync(string period, IEnumerable<string> findingIds, string owner)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/crowd/queue", new
        {
            period,
            seed = "surface-seed",
            spotCheck = 20,
            candidates = findingIds.Select(id => new { findingId = id, state = "accepted", ownerId = owner }),
        }, Ct);

        response.EnsureSuccessStatusCode();
    }

    // ── What the wire hands a rater ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_The_Offered_Item_Carries_The_Findings_EVIDENCE()
    {
        // ★★ Without this a rater is answering "should this hex string have fired?". The evidence is a link
        // to public code at the pinned revision — which is why the corpus is public repositories only.
        var findings = await SubmitAsync("surface-evidence");
        await RegisterQueueAsync(Period(), findings.Select(f => f.Id), owner: "acme");

        using var client = fx.Client();
        var item = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/noise/crowd/next?period={Period()}&raterId=rater-1", Ct)).RootElement;

        var evidence = item.GetProperty("evidence");
        var served = findings.Single(f => f.Id == item.GetProperty("findingId").GetString());

        Assert.Equal(served.Repo, evidence.GetProperty("repoId").GetString());
        Assert.Equal(118, evidence.GetProperty("line").GetInt32());
        Assert.Contains("hard to review", evidence.GetProperty("title").GetString()!, StringComparison.Ordinal);
        Assert.Contains(served.Sha, evidence.GetProperty("sourceUrl").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_The_Offered_Item_Still_Names_No_TOOL_And_No_REASON()
    {
        // ★★ THE DISGUISE THE SPOT-CHECK DEPENDS ON, re-asserted over the enriched payload. Adding the
        // evidence is exactly the kind of change that leaks the rest of the record along with it.
        var findings = await SubmitAsync("secret-surface-vendor");
        await RegisterQueueAsync(Period(), findings.Select(f => f.Id), owner: "acme");

        using var client = fx.Client();
        var raw = await client.GetStringAsync($"/api/noise/crowd/next?period={Period()}&raterId=rater-1", Ct);

        Assert.DoesNotContain("secret-surface-vendor", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("spot-check", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contested", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_An_Item_Whose_Finding_Was_Never_Stored_SAYS_So()
    {
        // ★★ FAIL LOUD, not blank. An operator can register a queue of ids that no submission recorded — and
        // serving those as an ordinary question is how the round fills up with answers from people who were
        // shown nothing. It is stated on the item, so the page can refuse to ask.
        await RegisterQueueAsync(Period(), ["no-such-finding-0001"], owner: "acme");

        using var client = fx.Client();
        var item = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/noise/crowd/next?period={Period()}&raterId=rater-1", Ct)).RootElement;

        Assert.Equal(JsonValueKind.Null, item.GetProperty("evidence").ValueKind);
        Assert.Contains("cannot be shown", item.GetProperty("evidenceProblem").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── The page ──────────────────────────────────────────────────────────────────────────────────

    private async Task<string> PageAsync(string url)
    {
        using var client = fx.Client();
        var response = await client.GetAsync(url, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return html;
    }

    [Fact]
    public async Task STAR_The_Page_Shows_The_Evidence_And_EVERY_Verdict_As_A_Choice()
    {
        // ★★ Six, not four. Two of them are process defects — "the evidence was not enough" and "the rubric
        // has no answer here" — and a rater denied those has to guess, which puts a fabricated verdict into
        // the rate rather than a filed defect. The vocabulary is the standard's; the page shows all of it.
        var findings = await SubmitAsync("surface-page");
        await RegisterQueueAsync(Period(), findings.Select(f => f.Id), owner: "acme");

        var html = await PageAsync($"/noise/rate/{Period()}?raterId=rater-page");

        Assert.Contains("src/Payments/Ledger.cs", html, StringComparison.Ordinal);
        Assert.Contains("hard to review", html, StringComparison.Ordinal);

        foreach (var verdict in Enum.GetValues<NoiseVerdict>())
        {
            Assert.Contains($"value=\"{verdict.Wire()}\"", html, StringComparison.Ordinal);
        }

        // ★ And what each one means, on the page. A rater choosing between six words they have to guess at
        // is a rater whose answers measure their reading of the vocabulary.
        Assert.Contains(NoiseVerdict.ValidNotActionable.Meaning(), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_The_Page_Asks_Both_BEHAVIOURAL_Questions()
    {
        // ★ #13: what a practitioner would DO, beside what they labelled it. Asked with the item or not at
        // all — a separate page nobody visits is the same as not asking.
        var findings = await SubmitAsync("surface-behaviour");
        await RegisterQueueAsync(Period(), findings.Select(f => f.Id), owner: "acme");

        var html = await PageAsync($"/noise/rate/{Period()}?raterId=rater-behaviour");

        Assert.Contains(BehaviouralQuestions.WouldFix, html, StringComparison.Ordinal);
        Assert.Contains(BehaviouralQuestions.WantInReport, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_The_Page_Shows_The_Rater_Their_OWN_Calibration_And_Standing()
    {
        // ★★ The rater is the one person the calibration figures are never shown to, and they are the only
        // one who can act on them. Their own score, the minimum sample, and — stated — that a poor score
        // never removes their answers, because a rater who suspects it does will answer to please.
        var findings = await SubmitAsync("surface-standing");
        await RegisterQueueAsync(Period(), findings.Select(f => f.Id), owner: "acme");

        var html = await PageAsync($"/noise/rate/{Period()}?raterId=rater-standing");

        Assert.Contains("rater-standing", html, StringComparison.Ordinal);
        Assert.Contains(
            RaterCalibration.MinimumSample.ToString(System.Globalization.CultureInfo.InvariantCulture),
            html, StringComparison.Ordinal);
        Assert.Contains("never applied", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_A_Rater_Is_Never_Served_A_Finding_On_Their_OWN_Estate()
    {
        // ★★ The rule is in CrowdQueue.For; this asserts the SURFACE honours it. "This isn't a real problem"
        // is a very human reaction to your own code being criticised, and it biases the rate systematically
        // rather than randomly — which no amount of averaging removes.
        var findings = await SubmitAsync("surface-estate");
        await RegisterQueueAsync(Period(), findings.Select(f => f.Id), owner: "rater-owns-this");

        var html = await PageAsync($"/noise/rate/{Period()}?raterId=rater-owns-this");

        Assert.DoesNotContain("src/Payments/Ledger.cs", html, StringComparison.Ordinal);

        // ★ And it SAYS so rather than showing an empty page, which reads as "the crowd is closed".
        Assert.Contains("nothing left for you", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Page_Is_Reachable_Without_Being_Told_A_Period()
    {
        // ★ A page that only answers /noise/rate/{period} needs the reader to already know a period — and
        // the standard's layout carries no navigation of its own, so without an index the crowd's front door
        // is a URL you have to be given.
        var findings = await SubmitAsync("surface-index");
        await RegisterQueueAsync(Period(), findings.Select(f => f.Id), owner: "acme");

        var html = await PageAsync("/noise/rate");

        Assert.Contains($"/noise/rate/{Period()}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_An_Answer_From_The_Page_Is_RECORDED_And_The_Next_Item_Differs()
    {
        // ★★ The round trip, driven the way a rater drives it: the page hands out an item, the form posts a
        // verdict back, and the next item is a different finding. A page that renders choices which record
        // nothing is the most convincing kind of nothing.
        var findings = await SubmitAsync("surface-answer");
        await RegisterQueueAsync(Period(), findings.Select(f => f.Id), owner: "acme");

        using var client = fx.Client();
        var first = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/noise/crowd/next?period={Period()}&raterId=rater-round-trip", Ct)).RootElement;
        var firstId = first.GetProperty("findingId").GetString()!;

        var posted = await client.PostAsJsonAsync("/api/noise/crowd/answers", new
        {
            period = Period(),
            raterId = "rater-round-trip",
            findingId = firstId,
            verdict = "noise",
            wouldFix = false,
            wantInReport = false,
        }, Ct);
        posted.EnsureSuccessStatusCode();

        var next = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/noise/crowd/next?period={Period()}&raterId=rater-round-trip", Ct)).RootElement;

        Assert.NotEqual(firstId, next.GetProperty("findingId").GetString());
    }
}
