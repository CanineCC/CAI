using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The draw must precede the run — the neutrality property everything else rests on.
/// </summary>
/// <remarks>
/// <para>★★ IT WAS ASSERTED IN PROSE AND CHECKED NOWHERE. 01-scope-and-governance lists it second among the
/// things that make neutrality checkable rather than promised: "the draw is published — timestamped and
/// signed — BEFORE any scanner runs. A holdout published afterwards is worthless no matter how it was made."
/// The draw carried a <c>drawnAt</c>, the submission carried no run time, and nothing compared them — so a
/// submission could answer a holdout with findings produced before that holdout existed.</para>
///
/// <para>★ A submission that omits its run time is not accepted with a shrug: it is told the check cannot be
/// made. The alternative — treating "no timestamp" as "fine" — makes the field optional in the only sense that
/// matters.</para>
/// </remarks>
public sealed class DrawOrderingApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The one period with a published draw, fixed at 2026-08-15.</summary>
    private const string Period = "2026-09";

    private async Task<JsonElement> SubmitAsync(string tool, string? runStartedAt)
    {
        using var client = fx.Client();

        var holdoutText = await client.GetStringAsync($"/api/noise/holdout/{Period}", Ct);
        var holdout = JsonDocument.Parse(holdoutText).RootElement
            .GetProperty("repositories").EnumerateArray()
            .Select(r => r.GetProperty("repoId").GetString()!)
            .ToList();

        var response = await client.PostAsJsonAsync("/api/noise/submissions", new
        {
            period = Period,
            tool,
            toolVersion = "v1",
            runStartedAt,
            recency = holdout.Select(r => new { repoId = r, stratum = "never-trained" }),
            findings = Array.Empty<object>(),
        }, Ct);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    private static string Problems(JsonElement body) =>
        string.Join(" ", body.GetProperty("problems").EnumerateArray().Select(p => p.GetString()));

    [Fact]
    public async Task STAR_A_Run_That_Started_BEFORE_The_Draw_Is_Refused()
    {
        // ★★ A result produced before its own draw was either run against something else, or run against a
        // draw somebody saw early. Either way it cannot answer this holdout, and accepting it would make the
        // published draw decorative.
        var body = await SubmitAsync("time-traveller", "2026-07-01T00:00:00Z");

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("BEFORE this period's holdout was published", Problems(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_A_Submission_That_Cannot_Say_When_It_Ran_Is_Told_So()
    {
        // ★ Not accepted with a shrug. Treating an absent timestamp as "fine" makes the field optional in the
        // only sense that matters — nobody would send it, and the ordering would go back to being a promise.
        var body = await SubmitAsync("no-clock", null);

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("does not say when the run started", Problems(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Run_After_The_Draw_Passes_The_Ordering_Check()
    {
        var body = await SubmitAsync("well-behaved", "2026-08-20T09:00:00Z");

        // It may still fail on coverage — this run submits no findings — but never on the ordering.
        Assert.DoesNotContain("holdout was published", Problems(body), StringComparison.Ordinal);
        Assert.DoesNotContain("when the run started", Problems(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_The_Receipt_Records_The_Refusal_Rather_Than_Erasing_It()
    {
        // ★★ A receipt is issued even for a rejected run, because the receipt IS the no-withdrawal record. A
        // submission that predates its holdout is exactly the kind a vendor would want to disappear.
        var body = await SubmitAsync("recorded-anyway", "2026-01-01T00:00:00Z");

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("submissionId").GetString()));
    }
}
