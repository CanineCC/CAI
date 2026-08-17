using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The gate between "a number was computed" and "a number may publish".
/// </summary>
/// <remarks>
/// <para>★★ <c>/api/noise/method</c> WAS PUBLISHING A CONTRACT NOTHING ENFORCED. Ten fields were listed as
/// <c>requiredWithEveryRate</c> — the LoC absolutes, a recall estimate with its method named, the claim-class
/// breakdown, the provenance — and the publication endpoint accepted a request with nowhere to put most of
/// them and checked two. A rule published without enforcement is worse than an absent rule: a reader assumes
/// it was kept.</para>
///
/// <para>★★ And <c>maxExclusionRate</c> was echoed to readers as though it governed something. The
/// specification says exclusions above it VOID the run; it was compared against nothing. The existing test
/// fixture sat at 5.9 % and published happily for as long as it existed.</para>
/// </remarks>
public sealed class PublicationContractApiTests(RegistryUnconfiguredFixture fx)
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

    /// <summary>A result that meets the whole contract. Every test below removes exactly one thing.</summary>
    private static Dictionary<string, object?> Complete() => new()
    {
        // ★ The period the number measures — required since #23-2, so the rate can be tied to the
        // method version that governed it.
        ["period"] = "2026-09",
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
        ["claimClasses"] = new object[]
        {
            new { claimClass = "pointwise", judged = 1400, noise = 300 },
            new { claimClass = "structural", judged = 300, noise = 90 },
            new { claimClass = "statistical", judged = 140, noise = 40 },
            new { claimClass = "advisory", judged = 60, noise = 10 },
        },
        ["toolVersion"] = "watchdog-engine 2026.08.3",
        ["holdoutSeed"] = "cai-2026-08-a1b2c3",
        ["modelSet"] = "judge-a@2026-07, judge-b@2026-07, blind-c@2026-06, blind-d@2026-06",
        ["gitMiningVerified"] = true,
        ["configuration"] = new
        {
            rulesetId = "watchdog-default-2026.08",
            isProductDefault = true,
        },
        ["fixRateUnavailable"] = "fixture — the anchor has its own tests",
        ["rejudgeUnavailable"] = "fixture: no second pass in this test — the re-judge has its own tests",
    };

    private static string[] Fields(JsonElement body) =>
        [.. body.GetProperty("breaches").EnumerateArray().Select(b => b.GetProperty("field").GetString()!)];

    [Fact]
    public async Task A_complete_result_publishes()
    {
        var (status, body) = await PublishAsync(Complete());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(4_200_000, body.GetProperty("locCovered").GetInt64());
        Assert.True(body.GetProperty("validPer100kLoc").GetDouble() > 0);
        Assert.True(body.GetProperty("noisePer100kLoc").GetDouble() > 0);
    }

    [Fact]
    public async Task STAR_Exclusions_Above_The_Ceiling_VOID_The_Run()
    {
        // ★★ Not a pass with a caveat and not a verdict on the tool — an instrument unfit to have been run.
        // Exclusions are not randomly distributed: they concentrate where the evidence is thin, which is
        // where judging is worst, so the run is flattered by exactly the findings it dropped.
        var run = Complete();
        run["adjudicated"] = 1800;
        run["excluded"] = 160;      // 160 of 1960 = 8.2 %
        run["unrated"] = 40;
        run["reported"] = 2000;

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("excluded", Fields(body));
        var text = string.Join(" ", body.GetProperty("breaches").EnumerateArray()
            .Select(b => b.GetProperty("error").GetString()));
        Assert.Contains("VOID", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exclusions_Exactly_At_The_Ceiling_Still_Publish()
    {
        // ★ The ceiling is "above", not "at". A boundary that voids the run at exactly the published figure
        // makes the published figure wrong by one item.
        var run = Complete();
        run["reported"] = 2000;
        run["adjudicated"] = 1900;
        run["excluded"] = 100;      // 100 of 2000 = exactly 5 %
        run["unrated"] = 0;

        var (status, _) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task STAR_A_Rate_Without_Its_LoC_Denominator_Is_Refused()
    {
        // ★★ The ratio hides suppression; the absolutes expose it. 42 valid / 8 noise per 100k LoC has a
        // WORSE ratio than 12 valid / 2 noise and is plainly the better instrument, so a rate whose
        // denominator the measured party controls cannot be the headline.
        var run = Complete();
        run.Remove("locCovered");

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("locCovered", Fields(body));
    }

    [Fact]
    public async Task STAR_A_Precision_Figure_Without_A_Recall_Counterpart_Is_Refused()
    {
        // ★★ The one that matters. A noise rate is a PRECISION measure, and the cheapest way to improve one
        // is to report less — so a standard publishing precision alone would reward under-detection across
        // every tool that adopted it.
        var run = Complete();
        run.Remove("recallMethod");
        run.Remove("recallEstimate");

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("recallMethod", Fields(body));
    }

    [Fact]
    public async Task STAR_A_Recall_Estimate_Must_Name_Its_Method()
    {
        // ★★ The five available methods do not measure the same thing. "Recall: 80 %" against a multi-vendor
        // union is a strong claim; against a planted corpus it is a regression floor; against nothing it is
        // a guess. A number a reader cannot weigh is not a published result.
        var run = Complete();
        run["recallMethod"] = "we-had-a-look";

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("recallMethod", Fields(body));
    }

    [Fact]
    public async Task Declared_Absence_Of_Recall_Publishes_With_Its_Reason()
    {
        // ★ Absence is allowed and publishes AS absence. What is not allowed is silence, which reads as
        // "recall was fine" — and refusing outright would push submitters toward inventing an estimate.
        var run = Complete();
        run["recallMethod"] = "none";
        run["recallEstimate"] = null;
        run["recallNote"] = "first period on this holdout — no second tool has submitted, so the pooled "
                          + "union is this tool's own findings and its recall against it is 100 % by construction";

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("none", body.GetProperty("recall").GetProperty("method").GetString());
        Assert.Contains("by construction",
            body.GetProperty("recall").GetProperty("note").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_Declared_Absence_Without_A_Reason_Is_Refused()
    {
        var run = Complete();
        run["recallMethod"] = "none";
        run["recallEstimate"] = null;

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("recallNote", Fields(body));
    }

    [Fact]
    public async Task STAR_A_Run_That_Does_Not_Declare_How_Falsifiable_Its_Output_Is_Gets_No_Rate()
    {
        // ★★ Without this the rate PENALISES SPECIFICITY: the more checkable a tool's output, the more of it
        // can be shown wrong, and the worse it looks beside a competitor whose claims are softer. That is
        // the actual shape of this market — the first vendor to publish a noise rate is compared against
        // silence, and silence reads as clean.
        var run = Complete();
        run.Remove("claimClasses");

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("claimClasses", Fields(body));
    }

    [Fact]
    public async Task STAR_The_Statistical_Class_Gets_No_Rate_Rather_Than_A_Zero()
    {
        // ★★ "Not measurable under this method" is an honest cell in a table; a blank that reads as clean is
        // not, and a computed number would measure the raters' opinions rather than the tool. A statistical
        // claim — "this file is a hotspot" — has no false-positive state to be wrong about.
        var (status, body) = await PublishAsync(Complete());

        Assert.Equal(HttpStatusCode.OK, status);
        var byClass = body.GetProperty("claimClasses").EnumerateArray()
            .ToDictionary(c => c.GetProperty("claimClass").GetString()!, c => c);

        Assert.Equal(JsonValueKind.Null, byClass["statistical"].GetProperty("noiseRate").ValueKind);
        Assert.False(byClass["statistical"].GetProperty("measurable").GetBoolean());
        Assert.Contains("no false-positive state",
            byClass["statistical"].GetProperty("notMeasurableReason").GetString()!, StringComparison.Ordinal);

        // …while the falsifiable classes do carry one, each over its own denominator.
        Assert.True(byClass["pointwise"].GetProperty("noiseRate").GetDouble() > 0);
        Assert.True(byClass["advisory"].GetProperty("noiseRate").GetDouble() > 0);

        // And the reader is told how much of the run was measurable at all.
        Assert.True(body.GetProperty("measurableShare").GetDouble() < 1.0);
        Assert.True(body.GetProperty("pooledRateComparable").GetBoolean());
    }

    [Fact]
    public async Task STAR_A_Tool_With_No_Falsifiable_Output_Is_Marked_Not_Comparable()
    {
        // ★★ A tool that only ever says "this area deserves attention" is not a clean tool; it is a tool
        // this instrument cannot read. Scoring it would publish the single most misleading number the
        // standard could produce.
        var run = Complete();
        run["claimClasses"] = new object[] { new { claimClass = "statistical", judged = 1900, noise = 440 } };

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("pooledRateComparable").GetBoolean());
        Assert.Equal(0.0, body.GetProperty("measurableShare").GetDouble());
    }

    [Fact]
    public async Task STAR_A_Run_Whose_Git_History_Was_Unreadable_Cannot_Publish()
    {
        // ★★ THE 05 PRE-PUBLICATION GATE. A contained scan without a usable .git makes the history-derived
        // dimensions emit false verdicts, and those are exactly the dimensions that face the competitors who
        // publish no error rate at all. Publishing that noise would report our own harness bug as a product
        // weakness — so it is a gate, not a caveat added afterwards.
        var run = Complete();
        run["gitMiningVerified"] = false;

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("gitMiningVerified", Fields(body));
        var text = string.Join(" ", body.GetProperty("breaches").EnumerateArray()
            .Select(b => b.GetProperty("error").GetString()));
        Assert.Contains("environment artefact", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_Not_Declaring_Git_Mining_Fails_Too()
    {
        // ★ Null is not a pass. A run that cannot say whether it had history is a run that did not check,
        // and the gate exists precisely because the answer is cheap to establish and expensive to assume.
        var run = Complete();
        run.Remove("gitMiningVerified");

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("gitMiningVerified", Fields(body));
    }

    [Theory]
    [InlineData("toolVersion")]
    [InlineData("holdoutSeed")]
    [InlineData("modelSet")]
    public async Task Provenance_Is_Required(string field)
    {
        // ★ A result that cannot be re-derived is not a result under this method — it is an assertion with a
        // number in it. Which build, which draw, which judges.
        var run = Complete();
        run.Remove(field);

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(field, Fields(body));
    }

    [Fact]
    public async Task STAR_Every_Breach_Comes_Back_At_Once()
    {
        // ★★ Told one at a time, a submitter fixes six things over six round-trips and learns nothing about
        // the shape of the contract. This is also the honest description of what was wrong with the endpoint
        // before: it reported the first problem it found and had no opinion about the other eight.
        var run = Complete();
        foreach (var f in new[] { "locCovered", "recallMethod", "claimClasses", "toolVersion", "gitMiningVerified" })
        {
            run.Remove(f);
        }

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var fields = Fields(body);
        Assert.Contains("locCovered", fields);
        Assert.Contains("recallMethod", fields);
        Assert.Contains("claimClasses", fields);
        Assert.Contains("toolVersion", fields);
        Assert.Contains("gitMiningVerified", fields);
        Assert.True(fields.Length >= 5);
    }

    [Fact]
    public async Task The_Gap_Count_Publishes_Beside_The_Rate()
    {
        // ★ 04 fix #1: the gap backlog IS a recall signal and it costs a query. A falling noise rate beside
        // a rising gap count is a story a reader can only see if both are on the page.
        var run = Complete();
        run["gapsFoundSinceLastPeriod"] = 34;

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(34, body.GetProperty("gapsFoundSinceLastPeriod").GetInt32());
    }

    [Fact]
    public async Task STAR_The_Refusal_Reads_The_Same_In_Every_CULTURE()
    {
        // ★★ Caught by running it, not by a test: on a da-DK host the ceiling breach rendered "16,7 %" — a
        // comma decimal separator, in a message a participant reads and quotes. Every figure the standard
        // states is part of its public surface, so it is formatted invariantly whatever the host's locale.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("da-DK");

            var run = Complete();
            run["reported"] = 2000;
            run["adjudicated"] = 1800;
            run["excluded"] = 160;
            run["unrated"] = 40;

            var (status, body) = await PublishAsync(run);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            var text = string.Join(" ", body.GetProperty("breaches").EnumerateArray()
                .Select(b => b.GetProperty("error").GetString()));
            Assert.Contains("8.2", text, StringComparison.Ordinal);
            Assert.DoesNotContain("8,2", text, StringComparison.Ordinal);
            Assert.DoesNotContain("5,0", text, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task STAR_The_Published_NUMBER_Must_Carry_Its_Configuration()
    {
        // ★★ Found by submitting from Watchdog: the declaration was derived from the runs, sent, and silently
        // ignored, because only /submissions checked it. #23-1 says deviations publish "alongside the number",
        // and the publication IS the number a reader quotes — a declaration that reaches only the register
        // leaves the figure with nothing attached to it.
        var run = Complete();
        run.Remove("configuration");

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("configuration", Fields(body));
    }

    [Fact]
    public async Task STAR_The_Configuration_Publishes_With_The_Rate()
    {
        var run = Complete();
        run["configuration"] = new
        {
            rulesetId = "tuned-for-corpus",
            isProductDefault = false,
            divergenceExplanation = "the two Electron lenses are off: the corpus has no Electron application",
            rulesDisabled = new[] { "D29-electron-preload" },
        };

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.OK, status);
        var cfg = body.GetProperty("configuration");
        Assert.Equal("tuned-for-corpus", cfg.GetProperty("rulesetId").GetString());
        Assert.False(cfg.GetProperty("isProductDefault").GetBoolean());
        Assert.Contains("no Electron application",
            cfg.GetProperty("divergenceExplanation").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Publication_Claiming_The_Default_While_Listing_Changes_Is_Refused()
    {
        var run = Complete();
        run["configuration"] = new
        {
            rulesetId = "watchdog-default-2026.08",
            isProductDefault = true,
            rulesDisabled = new[] { "D15-hotspot" },
        };

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("configuration.isProductDefault", Fields(body));
    }
}
