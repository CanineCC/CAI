using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The re-judge over the wire: the sample CAI demands, the second pass, and the gate on publication.
/// </summary>
/// <remarks>
/// <para>★★ "ACCEPTED" MUST STOP READING AS "VERIFIED". A submission that passed the membership, sha, ordering,
/// claim-class, recency, configuration and count checks was indistinguishable from one whose judging had also
/// been shown to reproduce — because the second thing had never been checked at all. This is the endpoint that
/// makes the difference visible, and the publication gate that makes it cost something.</para>
///
/// <para>★★ CAI HOLDS THE OUTCOME, SO THE PUBLICATION CANNOT LIE ABOUT IT. The re-judge result is looked up
/// from the store for the period rather than being declared in the publication body. A self-declared
/// reproducibility figure is the self-measured number the standard exists to replace.</para>
/// </remarks>
public sealed class RejudgeApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Its own period per test.
    /// </summary>
    /// <remarks>
    /// ★★ THE CALLER'S NAME, NOT A HASH OF IT. The register is one database for the whole class and xUnit does
    /// not guarantee ordering, so periods must not collide — and a first attempt used
    /// <c>GetHashCode() % 12</c>, which is randomised per process in .NET AND has eleven tests fighting over
    /// twelve slots. It would have passed most runs and failed some, which is the worst available outcome.
    /// </remarks>
    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"rj-{caller}";

    private static object Vote(string judge, string verdict) => new
    {
        judge, verdict,
        model = "gpt-judge", modelVersion = "2026-07-01", promptId = "p1",
        prompt = "Is this finding worth acting on?", reasoning = "because of the evidence shown",
    };

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    /// <summary>Judge <paramref name="count"/> findings for a period, so there is something to re-judge.</summary>
    private async Task<List<string>> JudgeAsync(string period, int count, string verdict = "noise")
    {
        using var client = fx.Client();
        List<string> ids = [];

        for (var i = 1; i <= count; i++)
        {
            var id = $"f{i:D4}";
            ids.Add(id);
            var body = await JsonAsync(await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
            {
                period,
                findingId = id,
                round1 = new[] { Vote("judge-a", verdict), Vote("judge-b", verdict) },
            }, Ct));
            Assert.True(body.GetProperty("recorded").GetBoolean());
        }

        return ids;
    }

    // ── The sample CAI demands ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_The_Sample_Is_Published_Before_Anybody_Re_Judges_It()
    {
        // ★★ CAI names the findings. A vendor — or an operator — choosing which to re-judge is not a check, and
        // the sample has to be readable BEFORE the second pass or it cannot be shown to have been fixed first.
        // ★ MORE judged than the sample size, so the sample is a real SUBSET. Judging eight and sampling
        // thirty would return all eight, and re-deriving "the sample" from itself proves nothing.
        var period = Period();
        var judged = await JudgeAsync(period, Rejudge.DefaultSampleSize + 5);

        using var client = fx.Client();
        var body = await JsonAsync(await client.GetAsync($"/api/noise/rejudge/{period}", Ct));

        var sample = body.GetProperty("sample").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(Rejudge.DefaultSampleSize, sample.Count);
        Assert.True(sample.Count < judged.Count);
        Assert.Equal(Rejudge.Tolerance, body.GetProperty("tolerance").GetDouble());
        Assert.False(body.GetProperty("rejudged").GetBoolean());

        // ★★ REPRODUCIBLE FROM PUBLISHED VALUES. The seed the sampler used is published beside the sample, so
        // a third party re-derives it from the full judged set — which is the only version of this claim worth
        // making. Re-deriving it from the sample would be circular.
        Assert.Equal(
            Rejudge.SelectSample(
                body.GetProperty("sampleSeed").GetString()!, period, judged, Rejudge.DefaultSampleSize),
            sample);
    }

    [Fact]
    public async Task A_Period_With_Nothing_Judged_Has_No_Sample_And_Says_So()
    {
        using var client = fx.Client();
        var body = await JsonAsync(await client.GetAsync($"/api/noise/rejudge/{Period()}", Ct));

        Assert.Empty(body.GetProperty("sample").EnumerateArray());
        Assert.Contains("nothing has been judged", body.GetProperty("note").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── The second pass ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_A_Second_Pass_That_Reproduces_Is_Within_Tolerance()
    {
        var period = Period();
        var ids = await JudgeAsync(period, 6);

        using var client = fx.Client();
        var body = await JsonAsync(await client.PostAsJsonAsync($"/api/noise/rejudge/{period}", new
        {
            verdicts = ids.Select(id => new
            {
                findingId = id, verdict = "noise",
                model = "claude-judge", modelVersion = "2026-08-01", promptId = "p2",
                prompt = "Independently: should this have fired?", reasoning = "second pass reasoning",
            }),
        }, Ct));

        Assert.Equal(6, body.GetProperty("compared").GetInt32());
        Assert.Equal(0, body.GetProperty("disagreements").GetInt32());
        Assert.True(body.GetProperty("withinTolerance").GetBoolean());
    }

    [Fact]
    public async Task STAR_A_Pass_That_Answers_Only_The_Agreeing_Half_Is_NOT_Within_Tolerance()
    {
        // ★★ THE MANOEUVRE THIS BLOCKS. Re-judge six, answer the two that agree, and a rate over "compared"
        // reports 0 % disagreement on a sample of two. The unanswered ones are named and block the tolerance.
        var period = Period();
        var ids = await JudgeAsync(period, 6);

        using var client = fx.Client();
        var body = await JsonAsync(await client.PostAsJsonAsync($"/api/noise/rejudge/{period}", new
        {
            verdicts = ids.Take(2).Select(id => new
            {
                findingId = id, verdict = "noise",
                model = "claude-judge", modelVersion = "2026-08-01", promptId = "p2",
                prompt = "Independently: should this have fired?", reasoning = "second pass reasoning",
            }),
        }, Ct));

        Assert.Equal(0, body.GetProperty("disagreements").GetInt32());
        Assert.False(body.GetProperty("withinTolerance").GetBoolean());
        Assert.Equal(4, body.GetProperty("unjudged").GetArrayLength());
    }

    [Fact]
    public async Task STAR_A_Verdict_Without_Its_Model_And_Reasoning_Is_Not_Recordable()
    {
        // ★ The same discipline the cascade record already applies: a verdict a reader cannot argue with is not
        // open judging, and here it would also be an unauditable half of a reproducibility claim.
        var period = Period();
        var ids = await JudgeAsync(period, 2);

        using var client = fx.Client();
        var response = await client.PostAsJsonAsync($"/api/noise/rejudge/{period}", new
        {
            verdicts = ids.Select(id => new { findingId = id, verdict = "noise" }),
        }, Ct);
        var body = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(body.GetProperty("unrecordable").EnumerateArray());
    }

    [Fact]
    public async Task STAR_A_Verdict_On_A_Finding_OUTSIDE_The_Sample_Is_Refused()
    {
        // ★★ Otherwise the second pass re-judges whatever it likes and reports agreement over its own choice,
        // which is the steerable sample the seed exists to prevent, arriving through the back door.
        var period = Period();
        await JudgeAsync(period, 4);

        using var client = fx.Client();
        var response = await client.PostAsJsonAsync($"/api/noise/rejudge/{period}", new
        {
            verdicts = new[]
            {
                new
                {
                    findingId = "not-in-the-sample", verdict = "noise",
                    model = "claude-judge", modelVersion = "2026-08-01", promptId = "p2",
                    prompt = "q", reasoning = "r",
                },
            },
        }, Ct);
        var body = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not-in-the-sample", body.GetProperty("error").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Recorded_Outcome_Is_Retrievable_And_Appears_In_The_RECORD()
    {
        var period = Period();
        var ids = await JudgeAsync(period, 4);

        using var client = fx.Client();
        await client.PostAsJsonAsync($"/api/noise/rejudge/{period}", new
        {
            verdicts = ids.Select(id => new
            {
                findingId = id, verdict = "noise",
                model = "claude-judge", modelVersion = "2026-08-01", promptId = "p2",
                prompt = "q", reasoning = "r",
            }),
        }, Ct);

        var get = await JsonAsync(await client.GetAsync($"/api/noise/rejudge/{period}", Ct));
        Assert.True(get.GetProperty("rejudged").GetBoolean());
        Assert.True(get.GetProperty("withinTolerance").GetBoolean());

        // ★ And in the public record, raw — the reproducibility claim is only worth what its evidence is.
        var record = await JsonAsync(await client.GetAsync($"/api/noise/record/{period}", Ct));
        Assert.Equal(4, record.GetProperty("rejudge").GetProperty("verdicts").GetArrayLength());
    }

    // ── What the method says ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_The_Method_Publishes_The_Tolerance_And_The_Fold()
    {
        using var client = fx.Client();
        var method = JsonDocument.Parse(await client.GetStringAsync("/api/noise/method", Ct)).RootElement;

        var rejudge = method.GetProperty("rejudge");
        Assert.Equal(Rejudge.Tolerance, rejudge.GetProperty("tolerance").GetDouble());
        Assert.Equal(Rejudge.DefaultSampleSize, rejudge.GetProperty("sampleSize").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(rejudge.GetProperty("toleranceRationale").GetString()));

        // ★★ The binary fold is stated, because it is the part somebody would otherwise assume was class-level.
        Assert.Contains("noise", rejudge.GetProperty("fold").GetString()!, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("rejudge",
            method.GetProperty("verificationChecks").EnumerateArray()
                .Select(c => c.GetProperty("check").GetString()));
    }
}

/// <summary>
/// The re-judge as a condition of publishing.
/// </summary>
/// <remarks>
/// <para>★★ THIS IS WHERE THE CHECK COSTS SOMETHING. A re-judge endpoint nobody has to use is a gate that fires
/// and tells nobody — the same failure this repository already documents about the rubric publish gate, which
/// checks presence and not contents. So a published rate must rest on a re-judge that landed within tolerance,
/// or state in words that none was run.</para>
///
/// <para>★★ AND THE OUTCOME IS LOOKED UP, NOT DECLARED. The publication body cannot carry a reproducibility
/// figure about itself: a self-declared one is exactly the self-measured number the standard replaces. CAI holds
/// the second pass, so CAI reads it.</para>
/// </remarks>
public sealed class RejudgePublicationGateTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"rjp-{caller}";

    private static Dictionary<string, object?> Publication(string period) => new()
    {
        ["period"] = period,
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
        ["holdoutSeed"] = "cai-2026-09-9f2b41c7e0a85d36",
        ["modelSet"] = "judge-a@2026-07",
        ["gitMiningVerified"] = true,
        ["configuration"] = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
        ["fixRateUnavailable"] = "fixture",
    };

    private async Task<(HttpStatusCode Status, JsonElement Body)> PublishAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/publication", payload, Ct);
        return (response.StatusCode,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone());
    }

    private async Task JudgeAndRejudgeAsync(string period, int count, string second)
    {
        using var client = fx.Client();
        List<string> ids = [];
        for (var i = 1; i <= count; i++)
        {
            var id = $"g{i:D4}";
            ids.Add(id);
            await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
            {
                period, findingId = id,
                round1 = new[]
                {
                    new { judge = "judge-a", verdict = "noise", model = "m", modelVersion = "v",
                          promptId = "p", prompt = "q", reasoning = "r" },
                    new { judge = "judge-b", verdict = "noise", model = "m", modelVersion = "v",
                          promptId = "p", prompt = "q", reasoning = "r" },
                },
            }, Ct);
        }

        await client.PostAsJsonAsync($"/api/noise/rejudge/{period}", new
        {
            verdicts = ids.Select(id => new
            {
                findingId = id, verdict = second,
                model = "claude-judge", modelVersion = "2026-08-01", promptId = "p2",
                prompt = "independent", reasoning = "second pass",
            }),
        }, Ct);
    }

    [Fact]
    public async Task STAR_A_Rate_With_No_Re_Judge_And_No_Reason_Cannot_Publish()
    {
        // ★★ The gate. "We measured 23 %" over judging nobody checked reproduces is the claim CAI exists to
        // stop being publishable on trust — including our own.
        var (status, body) = await PublishAsync(Publication(Period()));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("rejudge",
            string.Join(" ", body.GetProperty("breaches").EnumerateArray()
                .Select(b => b.GetProperty("field").GetString())),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Stated_Reason_There_Was_No_Re_Judge_Publishes_And_Says_So()
    {
        // ★ Not a refusal: a first period legitimately has no second pass yet, and forbidding that would stop
        // the standard from ever publishing its first number. But the absence is NAMED and it publishes.
        var run = Publication(Period());
        run["rejudgeUnavailable"] = "first period: no second pass has been convened yet";

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.OK, status);
        var rejudge = body.GetProperty("rejudge");
        Assert.False(rejudge.GetProperty("declared").GetBoolean());
        Assert.Contains("first period", rejudge.GetProperty("unavailableReason").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_A_Re_Judge_Within_Tolerance_Publishes_With_Its_Numbers()
    {
        var period = Period();
        await JudgeAndRejudgeAsync(period, 6, second: "noise");

        var (status, body) = await PublishAsync(Publication(period));

        Assert.Equal(HttpStatusCode.OK, status);
        var rejudge = body.GetProperty("rejudge");
        Assert.True(rejudge.GetProperty("declared").GetBoolean());
        Assert.True(rejudge.GetProperty("withinTolerance").GetBoolean());
        Assert.Equal(0d, rejudge.GetProperty("disagreementRate").GetDouble());
        Assert.Equal(Rejudge.Tolerance, rejudge.GetProperty("tolerance").GetDouble());
    }

    [Fact]
    public async Task STAR_A_Re_Judge_OUTSIDE_Tolerance_Blocks_The_Publication()
    {
        // ★★ The whole point of a tolerance. Every finding judged noise first time and valid the second: the
        // instrument is moving the number by more than the moves the number is used to argue about, and a rate
        // read off it is not a measurement. A declared reason must not rescue it either — the reason covers
        // "we did not run one", not "we ran one and it failed".
        var period = Period();
        await JudgeAndRejudgeAsync(period, 6, second: "valid-actionable");

        var run = Publication(period);
        run["rejudgeUnavailable"] = "trying to talk my way out of this";

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var breaches = string.Join(" ", body.GetProperty("breaches").EnumerateArray()
            .Select(b => b.GetProperty("error").GetString()));
        Assert.Contains("tolerance", breaches, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Publication_Cannot_DECLARE_Its_Own_Reproducibility()
    {
        // ★★ A body that could assert "withinTolerance: true" would be a self-measured reproducibility figure —
        // the exact thing the standard exists to replace. The published value comes from the store, so a
        // request claiming otherwise changes nothing.
        var period = Period();
        await JudgeAndRejudgeAsync(period, 6, second: "valid-actionable");   // a FAILING second pass

        var run = Publication(period);
        run["rejudgeWithinTolerance"] = true;                                // ignored
        run["rejudgeDisagreementRate"] = 0.0;                                // ignored

        var (status, _) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
