using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The crowd layer over HTTP: a period's queue is registered once, and raters are handed one item.
/// </summary>
/// <remarks>
/// <para>★ Published as an endpoint because the crowd is the only check in the whole method that comes
/// from OUTSIDE the model family. Four Anthropic judges agreeing tells you they are consistent; it does
/// not tell you they are right, and no amount of adding judges converts one into the other.</para>
/// <para>Every assertion here is about what the wire does NOT carry. The leaks that matter are additive —
/// someone helpfully returns the reason "for debugging" — and they are invisible in any result, because
/// a rubber-stamped spot-check looks exactly like a correct one.</para>
/// </remarks>
public sealed class CrowdQueueApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A period unique to each test — a registered queue claims its period for the process.</summary>
    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"crowd-{caller}";

    private static object Candidate(string id, string state, string owner = "acme") =>
        new { findingId = id, state, ownerId = owner };

    private async Task<(HttpStatusCode Status, JsonElement Body)> RegisterAsync(
        string period, IEnumerable<object> candidates, int spotCheck = 10, string seed = "seed-1")
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync(
            "/api/noise/crowd/queue",
            new { period, seed, spotCheck, candidates = candidates.ToArray() },
            Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static IEnumerable<object> Pool(int contested, int accepted, string owner = "acme") =>
    [
        .. Enumerable.Range(0, contested).Select(i => Candidate($"c{i:D3}", "needs-human", owner)),
        .. Enumerable.Range(0, accepted).Select(i => Candidate($"a{i:D3}", "accepted", owner)),
    ];

    // ── Registering a queue ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_registered_queue_reports_what_it_holds()
    {
        var (status, body) = await RegisterAsync(Period(), Pool(contested: 7, accepted: 200), spotCheck: 10);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(7, body.GetProperty("contested").GetInt32());
        Assert.Equal(10, body.GetProperty("spotCheck").GetInt32());
        Assert.Equal(17, body.GetProperty("queued").GetInt32());
    }

    /// <summary>
    /// ★ The registration response counts the spot-checks and does NOT name them. The operator needs to
    /// know the sample was drawn; publishing which findings are in it would let a participant recognise
    /// them, and a spot-check a vendor can identify is a spot-check they can prepare for.
    /// </summary>
    [Fact]
    public async Task STAR_registering_reports_counts_and_never_names_the_sampled_findings()
    {
        var (_, body) = await RegisterAsync(Period(), Pool(contested: 2, accepted: 50), spotCheck: 5);

        var raw = body.GetRawText();
        Assert.DoesNotContain("a0", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("c0", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_A_Queue_Naming_The_Same_Finding_TWICE_Is_Refused_Not_Accepted_And_Exploded_Later()
    {
        // ★★ FOUND BY THE END-TO-END RUN, not by any unit test on either side. Kennel registered a round whose
        // candidates included the same derived finding id twice — legitimately, because two repo-level findings
        // of one dimension in one repository ARE one finding under the id rule (#21a) — and this endpoint
        // accepted it. The duplicate then threw `An item with the same key has already been added` inside
        // CrowdSlice, which is rendered by the PUBLICATION endpoint: a malformed crowd queue took down
        // publishing, several calls later, with a 500 that named neither the queue nor the finding.
        //
        // ★ Refused HERE, where the caller can see what it sent. An accepted-then-fatal input is the worst of
        // both: the client is told it succeeded and the failure surfaces somewhere unrelated.
        var (status, body) = await RegisterAsync(Period(),
        [
            Candidate("dupe-001", "accepted"),
            Candidate("dupe-001", "accepted"),
            Candidate("fine-002", "accepted"),
        ]);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("dupe-001", body.GetProperty("error").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_queue_with_no_candidates_is_rejected()
    {
        var (status, _) = await RegisterAsync(Period(), []);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task An_unknown_cascade_state_is_rejected_rather_than_silently_dropped()
    {
        var (status, body) = await RegisterAsync(Period(), [Candidate("x", "probably-fine")]);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("state", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    // ── Being handed an item ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_rater_is_handed_a_finding()
    {
        var period = Period();
        await RegisterAsync(period, Pool(contested: 5, accepted: 50), spotCheck: 5);

        using var client = fx.Client();
        var body = await client.GetFromJsonAsync<JsonElement>(
            $"/api/noise/crowd/next?period={period}&raterId=rater-1", Ct);

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("findingId").GetString()));
    }

    /// <summary>
    /// ★★ THE WIRE CARRIES THE FINDING AND NOTHING ELSE. Told that four judges already agreed, a
    /// reasonable person reads "probably fine" and rubber-stamps — and the spot-check exists precisely to
    /// catch the case where all four were wrong together. A reason field would destroy the only evidence
    /// it was built to gather, and nothing downstream would ever show that it had.
    /// </summary>
    [Fact]
    public async Task STAR_what_is_handed_to_a_rater_carries_no_hint_of_why_it_was_queued()
    {
        var period = Period();
        await RegisterAsync(period, Pool(contested: 5, accepted: 50), spotCheck: 5);

        using var client = fx.Client();
        var raw = await client.GetStringAsync($"/api/noise/crowd/next?period={period}&raterId=rater-1", Ct);

        foreach (var leak in new[] { "reason", "spotCheck", "spot-check", "contested", "state", "accepted", "needs-human" })
        {
            Assert.DoesNotContain(leak, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// ★ Nobody rates a finding on their own estate. "This isn't a real problem" is a very human reaction
    /// to your own code being criticised, and it biases the rate systematically rather than randomly.
    /// </summary>
    [Fact]
    public async Task STAR_a_rater_is_never_handed_a_finding_from_their_own_estate()
    {
        var period = Period();
        await RegisterAsync(period, Pool(contested: 5, accepted: 20, owner: "rater-42"), spotCheck: 5);

        using var client = fx.Client();
        var response = await client.GetAsync($"/api/noise/crowd/next?period={period}&raterId=rater-42", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task An_answered_finding_is_not_handed_out_again()
    {
        var period = Period();
        await RegisterAsync(period, Pool(contested: 3, accepted: 0), spotCheck: 0);

        using var client = fx.Client();
        var first = await client.GetFromJsonAsync<JsonElement>(
            $"/api/noise/crowd/next?period={period}&raterId=rater-1", Ct);
        var id = first.GetProperty("findingId").GetString()!;

        var answer = await client.PostAsJsonAsync(
            "/api/noise/crowd/answers",
            new { period, raterId = "rater-1", findingId = id, verdict = "noise" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        var second = await client.GetFromJsonAsync<JsonElement>(
            $"/api/noise/crowd/next?period={period}&raterId=rater-1", Ct);
        Assert.NotEqual(id, second.GetProperty("findingId").GetString());
    }

    /// <summary>
    /// ★ An answer to a finding the rater was never handed is refused. Without it the queue is only a
    /// suggestion, and a participant could answer the whole accepted pool — including the items they were
    /// deliberately not shown — which is the one thing the disguise exists to prevent.
    /// </summary>
    [Fact]
    public async Task STAR_an_answer_to_an_unoffered_finding_is_refused()
    {
        var period = Period();
        await RegisterAsync(period, Pool(contested: 2, accepted: 50), spotCheck: 0);

        using var client = fx.Client();
        var response = await client.PostAsJsonAsync(
            "/api/noise/crowd/answers",
            new { period, raterId = "rater-1", findingId = "a007", verdict = "noise" },
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_answer_carrying_an_unpublished_verdict_is_refused()
    {
        var period = Period();
        await RegisterAsync(period, Pool(contested: 2, accepted: 0), spotCheck: 0);

        using var client = fx.Client();
        var first = await client.GetFromJsonAsync<JsonElement>(
            $"/api/noise/crowd/next?period={period}&raterId=rater-1", Ct);

        var response = await client.PostAsJsonAsync(
            "/api/noise/crowd/answers",
            new { period, raterId = "rater-1", findingId = first.GetProperty("findingId").GetString(), verdict = "meh" },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_rater_who_has_answered_everything_is_handed_nothing()
    {
        var period = Period();
        await RegisterAsync(period, Pool(contested: 2, accepted: 0), spotCheck: 0);

        using var client = fx.Client();
        for (var i = 0; i < 2; i++)
        {
            var next = await client.GetFromJsonAsync<JsonElement>(
                $"/api/noise/crowd/next?period={period}&raterId=rater-1", Ct);
            await client.PostAsJsonAsync(
                "/api/noise/crowd/answers",
                new { period, raterId = "rater-1", findingId = next.GetProperty("findingId").GetString(), verdict = "noise" },
                Ct);
        }

        var response = await client.GetAsync($"/api/noise/crowd/next?period={period}&raterId=rater-1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_period_hands_out_nothing_rather_than_inventing_a_queue()
    {
        using var client = fx.Client();
        var response = await client.GetAsync("/api/noise/crowd/next?period=never-registered&raterId=r", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// ★★ Found by driving the LIVE endpoint with eight raters: every one of them was handed the same
    /// finding. The queue has a head and the head is the same for everybody, so eight answers landed on
    /// one item and none on the other seven — including all three contested ones. Every unit test passed
    /// on that behaviour, because each used a single rater.
    /// </summary>
    [Fact]
    public async Task STAR_eight_raters_are_handed_eight_different_findings()
    {
        var period = Period();
        await RegisterAsync(period, Pool(contested: 3, accepted: 40), spotCheck: 5);

        using var client = fx.Client();
        List<string> handed = [];
        for (var i = 0; i < 8; i++)
        {
            var next = await client.GetFromJsonAsync<JsonElement>(
                $"/api/noise/crowd/next?period={period}&raterId=rater-{i}", Ct);
            handed.Add(next.GetProperty("findingId").GetString()!);
        }

        Assert.Equal(8, handed.Distinct().Count());
    }

    /// <summary>
    /// ★ And the contested tail is actually reached. It is the part the cascade escalated BECAUSE it is
    /// hard; a crowd round that never puts it in front of anyone has skipped the only work it was
    /// convened for.
    /// </summary>
    [Fact]
    public async Task STAR_the_contested_tail_is_reached_not_just_the_spot_check()
    {
        var period = Period();
        await RegisterAsync(period, Pool(contested: 3, accepted: 40), spotCheck: 5);

        using var client = fx.Client();
        for (var i = 0; i < 8; i++)
        {
            var next = await client.GetFromJsonAsync<JsonElement>(
                $"/api/noise/crowd/next?period={period}&raterId=rater-{i}", Ct);
            await client.PostAsJsonAsync(
                "/api/noise/crowd/answers",
                new { period, raterId = $"rater-{i}", findingId = next.GetProperty("findingId").GetString(), verdict = "noise" },
                Ct);
        }

        var results = await client.GetFromJsonAsync<JsonElement>($"/api/noise/crowd/results/{period}", Ct);
        Assert.Equal(3, results.GetProperty("contested").GetProperty("answered").GetInt32());
        Assert.Equal(5, results.GetProperty("spotCheck").GetProperty("answered").GetInt32());
    }

    // ── What the results say ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ THE RESULT SPLITS SPOT-CHECK FROM CONTESTED, and only after the answers are in. A merged
    /// figure is the one number nobody can use: the contested items are hard BY CONSTRUCTION and the
    /// accepted ones are the pipeline's own claim, so averaging them hides exactly the disagreement rate
    /// on auto-accepted findings that the whole layer exists to measure.
    /// </summary>
    [Fact]
    public async Task STAR_results_report_the_spot_check_separately_from_the_contested_tail()
    {
        var period = Period();
        await RegisterAsync(period, Pool(contested: 3, accepted: 50), spotCheck: 4);

        using var client = fx.Client();
        for (var i = 0; i < 7; i++)
        {
            var next = await client.GetFromJsonAsync<JsonElement>(
                $"/api/noise/crowd/next?period={period}&raterId=rater-{i}", Ct);
            await client.PostAsJsonAsync(
                "/api/noise/crowd/answers",
                new { period, raterId = $"rater-{i}", findingId = next.GetProperty("findingId").GetString(), verdict = "noise" },
                Ct);
        }

        var results = await client.GetFromJsonAsync<JsonElement>($"/api/noise/crowd/results/{period}", Ct);

        Assert.Equal(3, results.GetProperty("contested").GetProperty("queued").GetInt32());
        Assert.Equal(4, results.GetProperty("spotCheck").GetProperty("queued").GetInt32());
        Assert.False(results.TryGetProperty("overallAgreement", out _),
            "a merged agreement figure hides the one rate the spot-check exists to measure");
    }

    /// <summary>
    /// ★★ A spot-check answer that DISAGREES with the judges is the finding this layer exists to
    /// surface: four models agreed, and a person outside the family says otherwise. It is reported as its
    /// own number, because it is the only evidence available that unanimity can be wrong.
    /// </summary>
    [Fact]
    public async Task STAR_a_spot_check_answer_that_contradicts_the_judges_is_counted()
    {
        var period = Period();
        // Every accepted finding is auto-accepted as noise; a rater calling one valid contradicts that.
        await RegisterAsync(period, Pool(contested: 0, accepted: 20), spotCheck: 3);

        using var client = fx.Client();
        for (var i = 0; i < 3; i++)
        {
            var next = await client.GetFromJsonAsync<JsonElement>(
                $"/api/noise/crowd/next?period={period}&raterId=rater-{i}", Ct);
            await client.PostAsJsonAsync(
                "/api/noise/crowd/answers",
                new
                {
                    period,
                    raterId = $"rater-{i}",
                    findingId = next.GetProperty("findingId").GetString(),
                    verdict = "valid-actionable",
                    machineVerdict = "noise",
                },
                Ct);
        }

        var results = await client.GetFromJsonAsync<JsonElement>($"/api/noise/crowd/results/{period}", Ct);

        Assert.Equal(3, results.GetProperty("spotCheck").GetProperty("answered").GetInt32());
        Assert.Equal(3, results.GetProperty("spotCheck").GetProperty("contradicted").GetInt32());
    }
}
