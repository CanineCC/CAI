using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Contesting a verdict, in public, against published reasoning.
/// </summary>
/// <remarks>
/// <para>★★ 01 §5: "a vendor who thinks a verdict is wrong can contest it in public, against published reasoning.
/// 'The standard says so' is not an argument CAI gets to make." There was no way to say so. The cascade recorded
/// verdicts with their reasoning and the crowd could contradict a spot-check, but a vendor looking at one verdict
/// it believed was wrong had no route at all — which makes the standard the last word on its own judgements, and
/// that is the position it exists to avoid occupying.</para>
///
/// <para>★★ AND A DISPUTE CANNOT REMOVE A VERDICT. The raw verdict stays, append-only, whatever the outcome: a
/// contestation mechanism that deleted what it overturned would be a withdrawal mechanism, and the register would
/// quietly become "the verdicts nobody objected to".</para>
///
/// <para>★ It publishes EITHER WAY. A dispute that only appears when the vendor wins is a complaints box; the
/// upheld ones are the evidence that the mechanism is not one.</para>
/// </remarks>
public sealed class VerdictDisputeApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"dsp-{caller}";

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    /// <summary>Judge one finding, so there is a verdict to dispute.</summary>
    private async Task JudgeAsync(HttpClient client, string period, string findingId, string verdict = "noise")
    {
        var vote = new
        {
            verdict, model = "gpt-judge", modelVersion = "2026-07-01", promptId = "p1",
            prompt = "Should this have fired?", reasoning = "the evidence shown supports it",
        };

        var body = await JsonAsync(await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
        {
            period, findingId,
            round1 = new object[]
            {
                new { judge = "judge-a", vote.verdict, vote.model, vote.modelVersion, vote.promptId, vote.prompt, vote.reasoning },
                new { judge = "judge-b", vote.verdict, vote.model, vote.modelVersion, vote.promptId, vote.prompt, vote.reasoning },
            },
        }, Ct));

        Assert.True(body.GetProperty("recorded").GetBoolean());
    }

    // ── Raising one ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_A_Dispute_Needs_A_REASON()
    {
        // ★★ "I disagree" is not contestation. The reason is the thing that publishes, and it is what makes the
        // dispute answerable rather than a vote — 01 §5 is about arguing against published reasoning, in both
        // directions.
        const string finding = "f-0001";
        using var client = fx.Client();
        var period = Period();
        await JudgeAsync(client, period, finding);

        var response = await client.PostAsJsonAsync(
            $"/api/noise/verdicts/{finding}/dispute", new { period, raisedBy = "watchdog" }, Ct);
        var body = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reason", body.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_A_Dispute_Against_A_Finding_With_No_Verdict_Is_Refused()
    {
        // ★ Otherwise the register fills with disputes about judgements nobody made, and "12 disputes this
        // period" stops meaning anything.
        using var client = fx.Client();

        var response = await client.PostAsJsonAsync(
            "/api/noise/verdicts/never-judged/dispute",
            new { period = Period(), raisedBy = "watchdog", reason = "we think this is wrong" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task STAR_A_Raised_Dispute_Is_Recorded_And_Open()
    {
        const string finding = "f-0002";
        using var client = fx.Client();
        var period = Period();
        await JudgeAsync(client, period, finding);

        var body = await JsonAsync(await client.PostAsJsonAsync(
            $"/api/noise/verdicts/{finding}/dispute",
            new
            {
                period, raisedBy = "watchdog",
                reason = "the finding cites a generated file the rule is documented not to apply to",
            }, Ct));

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("disputeId").GetString()));
        Assert.Equal("open", body.GetProperty("state").GetString());
        Assert.Equal(finding, body.GetProperty("findingId").GetString());
    }

    // ── Resolving one ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_A_Resolution_Needs_An_OUTCOME_And_ITS_REASONING()
    {
        const string finding = "f-0003";
        using var client = fx.Client();
        var period = Period();
        await JudgeAsync(client, period, finding);

        var raised = await JsonAsync(await client.PostAsJsonAsync(
            $"/api/noise/verdicts/{finding}/dispute",
            new { period, raisedBy = "watchdog", reason = "generated file" }, Ct));
        var id = raised.GetProperty("disputeId").GetString();

        // ★★ An outcome with no reasoning is "the standard says so" — the exact argument 01 §5 says CAI does not
        // get to make. Refused in both directions: upholding needs a reason as much as overturning does.
        var noReasoning = await client.PostAsJsonAsync(
            $"/api/noise/disputes/{id}/resolve", new { outcome = "upheld" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noReasoning.StatusCode);

        var noOutcome = await client.PostAsJsonAsync(
            $"/api/noise/disputes/{id}/resolve", new { reasoning = "we looked and it is fine" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noOutcome.StatusCode);
    }

    [Fact]
    public async Task STAR_An_UPHELD_Dispute_Publishes_Too()
    {
        // ★★ A dispute that only appears when the vendor wins is a complaints box. The upheld ones are the
        // evidence that this is not one, so they publish identically.
        const string finding = "f-0004";
        using var client = fx.Client();
        var period = Period();
        await JudgeAsync(client, period, finding);

        var raised = await JsonAsync(await client.PostAsJsonAsync(
            $"/api/noise/verdicts/{finding}/dispute",
            new { period, raisedBy = "watchdog", reason = "we think the rule does not apply here" }, Ct));

        var resolved = await JsonAsync(await client.PostAsJsonAsync(
            $"/api/noise/disputes/{raised.GetProperty("disputeId").GetString()}/resolve",
            new
            {
                outcome = "upheld",
                reasoning = "the rule's documentation covers this case explicitly; the verdict stands",
            }, Ct));

        Assert.Equal("upheld", resolved.GetProperty("outcome").GetString());

        var record = await JsonAsync(await client.GetAsync($"/api/noise/record/{period}", Ct));
        var dispute = record.GetProperty("disputes").GetProperty("items").EnumerateArray().Single();

        Assert.Equal("upheld", dispute.GetProperty("outcome").GetString());
        Assert.Contains("documentation covers this case",
            dispute.GetProperty("resolutionReasoning").GetString()!, StringComparison.Ordinal);
        Assert.Contains("rule does not apply",
            dispute.GetProperty("reason").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_An_OVERTURNED_Verdict_Is_Still_In_The_RAW_Record()
    {
        // ★★ THE PROPERTY THAT MATTERS MOST. A contestation mechanism that deleted what it overturned would be a
        // withdrawal mechanism, and the register would quietly become "the verdicts nobody objected to". The raw
        // verdict stays exactly as it was recorded; the dispute sits BESIDE it.
        const string finding = "f-0005";
        using var client = fx.Client();
        var period = Period();
        await JudgeAsync(client, period, finding);

        var before = await JsonAsync(await client.GetAsync($"/api/noise/record/{period}", Ct));
        var rawBefore = before.GetProperty("rawVerdicts").GetInt32();

        var raised = await JsonAsync(await client.PostAsJsonAsync(
            $"/api/noise/verdicts/{finding}/dispute",
            new { period, raisedBy = "watchdog", reason = "the cited line is in generated code" }, Ct));

        await client.PostAsJsonAsync(
            $"/api/noise/disputes/{raised.GetProperty("disputeId").GetString()}/resolve",
            new { outcome = "overturned", reasoning = "the file is generated; the rule excludes generated code" },
            Ct);

        var after = await JsonAsync(await client.GetAsync($"/api/noise/record/{period}", Ct));

        Assert.Equal(rawBefore, after.GetProperty("rawVerdicts").GetInt32());
        Assert.Contains(
            after.GetProperty("verdicts").EnumerateArray(),
            v => v.GetProperty("findingId").GetString() == finding);

        // ★★ And an overturned verdict does NOT silently change a published rate: the correction is a second
        // publication, which the append-only store makes visible as a correction. Said in the record, because a
        // reader would otherwise assume one or the other.
        Assert.Contains("corrected publication",
            after.GetProperty("disputes").GetProperty("note").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Dispute_Cannot_Be_Resolved_TWICE()
    {
        // ★ Otherwise the outcome is whatever was written last, and "published either way" becomes "published
        // whichever way we ended up preferring".
        const string finding = "f-0006";
        using var client = fx.Client();
        var period = Period();
        await JudgeAsync(client, period, finding);

        var raised = await JsonAsync(await client.PostAsJsonAsync(
            $"/api/noise/verdicts/{finding}/dispute",
            new { period, raisedBy = "watchdog", reason = "first thoughts" }, Ct));
        var id = raised.GetProperty("disputeId").GetString();

        var first = await client.PostAsJsonAsync($"/api/noise/disputes/{id}/resolve",
            new { outcome = "upheld", reasoning = "the verdict stands" }, Ct);
        var second = await client.PostAsJsonAsync($"/api/noise/disputes/{id}/resolve",
            new { outcome = "overturned", reasoning = "actually, on reflection" }, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task An_Unrecognised_Outcome_Is_Refused_With_The_Two_That_Exist()
    {
        const string finding = "f-0007";
        using var client = fx.Client();
        var period = Period();
        await JudgeAsync(client, period, finding);

        var raised = await JsonAsync(await client.PostAsJsonAsync(
            $"/api/noise/verdicts/{finding}/dispute",
            new { period, raisedBy = "watchdog", reason = "a reason" }, Ct));

        var response = await client.PostAsJsonAsync(
            $"/api/noise/disputes/{raised.GetProperty("disputeId").GetString()}/resolve",
            new { outcome = "partially", reasoning = "somewhere in between" }, Ct);
        var body = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overturned", body.GetProperty("outcomes").ToString(), StringComparison.Ordinal);
    }

    // ── The record ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_An_OPEN_Dispute_Is_Visible_As_Open_Rather_Than_Absent()
    {
        // ★★ An unresolved dispute is the state a reader most needs to see: it is the one where the standard has
        // been challenged and has not answered. Absent from the record, "no disputes" and "three we have not got
        // round to" look the same.
        const string finding = "f-0008";
        using var client = fx.Client();
        var period = Period();
        await JudgeAsync(client, period, finding);

        await client.PostAsJsonAsync($"/api/noise/verdicts/{finding}/dispute",
            new { period, raisedBy = "watchdog", reason = "unanswered so far" }, Ct);

        var record = await JsonAsync(await client.GetAsync($"/api/noise/record/{period}", Ct));
        var disputes = record.GetProperty("disputes");

        Assert.Equal(1, disputes.GetProperty("open").GetInt32());
        Assert.Equal("open", disputes.GetProperty("items").EnumerateArray().Single()
            .GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_Period_With_No_Disputes_Says_So()
    {
        using var client = fx.Client();
        var period = Period();
        await JudgeAsync(client, period, "f-0009");

        var record = await JsonAsync(await client.GetAsync($"/api/noise/record/{period}", Ct));

        Assert.Equal(0, record.GetProperty("disputes").GetProperty("raised").GetInt32());
    }

    [Fact]
    public async Task STAR_The_Method_Publishes_The_Contestation_Route()
    {
        // ★ A right nobody can find is not one. The endpoint, what a dispute requires, and that it publishes
        // either way.
        using var client = fx.Client();
        var method = JsonDocument.Parse(await client.GetStringAsync("/api/noise/method", Ct)).RootElement;

        var contestation = method.GetProperty("contestation");
        Assert.Contains("dispute", contestation.GetProperty("endpoint").GetString()!, StringComparison.Ordinal);
        Assert.Contains("either way", contestation.GetProperty("publishes").GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("append-only", contestation.GetProperty("rawVerdictIsKept").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }
}
