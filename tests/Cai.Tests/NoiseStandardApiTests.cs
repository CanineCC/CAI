using System.Net;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The noise-measurement standard's public contract.
/// </summary>
/// <remarks>
/// <para>★ This lives in CAI rather than in a scanner because a self-measured number is not a claim a
/// buyer can use. "We measure our own noise" is weak in a way no amount of rigour behind it can fix —
/// the reader cannot tell one vendor's rigour from another's assertion. A shared, published method makes
/// numbers commensurable, which is the only thing that lets anyone compare.</para>
/// <para><b>CAI specifies and verifies; it does not referee.</b> It publishes the method, the verdict
/// set and the holdout with its seed, and it checks that a submitted run reproduces. It never runs
/// anyone's scanner and never owns the verdict — because the standard is owned by a participant, and a
/// referee that plays for one team is worth nothing.</para>
/// <para>Anonymous and unauthenticated on purpose: a standard nobody can read without credentials is not
/// a standard.</para>
/// </remarks>
public sealed class NoiseStandardApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var client = fx.Client();
        var response = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    // ── The verdict set ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ The six verdicts publish as a machine-readable contract, because an engine has to implement
    /// them. In cycle 1 a rater answered a binary while the judge answered a five-class taxonomy —
    /// agreement between two different questions is not a measurement, and that is exactly the drift a
    /// published contract prevents between two ORGANISATIONS.
    /// </summary>
    [Fact]
    public async Task The_verdict_set_publishes_all_six_anonymously()
    {
        var json = await GetJsonAsync("/api/noise/verdicts");

        var verdicts = json.GetProperty("verdicts").EnumerateArray().ToList();
        Assert.Equal(6, verdicts.Count);

        var values = verdicts.Select(v => v.GetProperty("value").GetString()).ToList();
        Assert.Contains("noise", values);
        Assert.Contains("valid-actionable", values);
        Assert.Contains("valid-not-actionable", values);
        Assert.Contains("both-wrong", values);
        Assert.Contains("cannot-tell", values);
        Assert.Contains("rubric-ambiguous", values);
    }

    /// <summary>
    /// ★ Each verdict publishes what it DOES to the numbers, not just its name. A vendor implementing
    /// this must know that "valid but not actionable" scores as valid and moves a separate axis, and
    /// that two of the six leave the rate entirely — otherwise every implementer decides differently and
    /// the numbers stop being comparable, which is the whole point of publishing them.
    /// </summary>
    [Fact]
    public async Task Each_verdict_publishes_its_effect_on_the_rate()
    {
        var json = await GetJsonAsync("/api/noise/verdicts");
        var byValue = json.GetProperty("verdicts").EnumerateArray()
            .ToDictionary(v => v.GetProperty("value").GetString()!);

        Assert.True(byValue["noise"].GetProperty("countsTowardRate").GetBoolean());
        Assert.True(byValue["noise"].GetProperty("isNoise").GetBoolean());

        // Valid-but-not-actionable is a TRUE POSITIVE for the detector and a failure for the reader.
        Assert.True(byValue["valid-not-actionable"].GetProperty("countsTowardRate").GetBoolean());
        Assert.False(byValue["valid-not-actionable"].GetProperty("isNoise").GetBoolean());
        Assert.False(byValue["valid-not-actionable"].GetProperty("isActionable").GetBoolean());

        // The two process defects leave the rate.
        Assert.False(byValue["cannot-tell"].GetProperty("countsTowardRate").GetBoolean());
        Assert.False(byValue["rubric-ambiguous"].GetProperty("countsTowardRate").GetBoolean());
    }

    /// <summary>
    /// ★★ THE ASYMMETRY IS PART OF THE CONTRACT. A human who cannot tell has hit an evidence defect and
    /// the item is excluded. A MACHINE that cannot tell must ESCALATE — excluding there hands the
    /// pipeline a way to duck its hardest cases and still report a clean rate, which is selecting on the
    /// outcome by another name. An implementer who gets this backwards reports a flattering number in
    /// good faith, so the standard states it rather than assuming it.
    /// </summary>
    [Fact]
    public async Task STAR_the_human_machine_asymmetry_is_published_not_assumed()
    {
        var json = await GetJsonAsync("/api/noise/verdicts");
        var byValue = json.GetProperty("verdicts").EnumerateArray()
            .ToDictionary(v => v.GetProperty("value").GetString()!);

        var cannotTell = byValue["cannot-tell"];
        Assert.True(cannotTell.GetProperty("excludesForHuman").GetBoolean());
        Assert.False(cannotTell.GetProperty("excludesForMachine").GetBoolean());
        Assert.True(cannotTell.GetProperty("escalatesForMachine").GetBoolean());

        // An ambiguous rubric excludes for both — neither side resolves it by trying harder.
        Assert.True(byValue["rubric-ambiguous"].GetProperty("excludesForHuman").GetBoolean());
        Assert.True(byValue["rubric-ambiguous"].GetProperty("excludesForMachine").GetBoolean());
    }

    // ── The method's own rules ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ The exclusion ceiling is part of the published contract, because an exclusion that is not
    /// bounded is a laundry. Excluded items are not randomly distributed — they concentrate where the
    /// evidence is thin, which is where judging is worst — so a run above the ceiling is VOID rather
    /// than passing with a caveat.
    /// </summary>
    [Fact]
    public async Task The_exclusion_ceiling_publishes_with_the_method()
    {
        var json = await GetJsonAsync("/api/noise/method");

        Assert.Equal(0.05, json.GetProperty("maxExclusionRate").GetDouble(), 3);
        Assert.Contains("void", json.GetProperty("exclusionRule").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ★★ A NOISE RATE IS A PRECISION MEASURE, and the standard says so in its own text. The easiest way
    /// to improve one is to report less, so a specification that publishes precision alone would reward
    /// under-detection across every tool that adopted it.
    /// </summary>
    [Fact]
    public async Task STAR_the_method_states_that_a_rate_alone_is_incomplete()
    {
        var json = await GetJsonAsync("/api/noise/method");

        Assert.False(json.GetProperty("noiseRateIsAQualityScore").GetBoolean());
        Assert.True(json.GetProperty("requiresRecallCounterpart").GetBoolean());

        // The absolutes are what expose suppression; the ratio alone hides it.
        var required = json.GetProperty("requiredWithEveryRate").EnumerateArray()
            .Select(v => v.GetString()).ToList();
        Assert.Contains("validPer100kLoc", required);
        Assert.Contains("noisePer100kLoc", required);
        Assert.Contains("recallEstimate", required);
    }

    /// <summary>
    /// ★ Claim specificity is published because a noise rate compares only tools making comparably
    /// falsifiable claims. "Line 42 dereferences null" can be a false positive; "this file is a hotspot"
    /// cannot be, in the same sense — so a naive pooled rate penalises the more specific tool.
    /// </summary>
    [Fact]
    public async Task The_method_publishes_the_claim_specificity_classes()
    {
        var json = await GetJsonAsync("/api/noise/method");

        var classes = json.GetProperty("claimClasses").EnumerateArray()
            .Select(v => v.GetString()).ToList();
        Assert.Equal(["pointwise", "structural", "statistical", "advisory"], classes);
    }

    [Fact]
    public async Task The_method_is_versioned_so_a_change_cannot_be_silent()
    {
        var json = await GetJsonAsync("/api/noise/method");
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("version").GetString()));
    }

    // ── The holdout ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ THE PUBLISHED HOLDOUT CARRIES ITS SEED AND ITS RULES, so a third party can re-derive the draw
    /// and confirm it was not chosen to flatter anybody. A holdout published without them is an
    /// assertion.
    /// </summary>
    [Fact]
    public async Task STAR_the_holdout_publishes_the_seed_and_rules_that_reproduce_it()
    {
        var json = await GetJsonAsync("/api/noise/holdout/2026-09");

        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("seed").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("samplerVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("drawnAt").GetString()));

        var rules = json.GetProperty("rules");
        Assert.True(rules.GetProperty("targetProductionLocPerLanguage").GetInt32() > 0);
        Assert.True(rules.GetProperty("maxRepositoryLoc").GetInt32() > 0);
        Assert.True(rules.GetProperty("minRepositoriesPerLanguage").GetInt32() > 0);
    }

    /// <summary>Every drawn repository is pinned, or "the same code" means nothing across two runs.</summary>
    [Fact]
    public async Task Every_drawn_repository_is_pinned_to_a_sha()
    {
        var json = await GetJsonAsync("/api/noise/holdout/2026-09");

        var repos = json.GetProperty("repositories").EnumerateArray().ToList();
        Assert.NotEmpty(repos);
        foreach (var r in repos)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("pinnedSha").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("repoId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("language").GetString()));
        }
    }

    /// <summary>
    /// ★★ AND IT REPRODUCES. Asking twice returns the same repositories — the property the whole
    /// standard rests on, checked through the API rather than only in the sampler, because a caller
    /// verifies against what the endpoint serves.
    /// </summary>
    [Fact]
    public async Task STAR_asking_twice_returns_the_identical_draw()
    {
        var a = await GetJsonAsync("/api/noise/holdout/2026-09");
        var b = await GetJsonAsync("/api/noise/holdout/2026-09");

        static List<string> Ids(JsonElement j) =>
            [.. j.GetProperty("repositories").EnumerateArray()
                .Select(r => r.GetProperty("repoId").GetString()!)];

        Assert.Equal(Ids(a), Ids(b));
        Assert.Equal(a.GetProperty("seed").GetString(), b.GetProperty("seed").GetString());
    }

    /// <summary>
    /// ★ Nothing about any scanner's output appears in a published holdout, asserted on the SERIALISED
    /// document rather than only on the type — a field can be added at the endpoint without touching the
    /// candidate record.
    /// </summary>
    [Fact]
    public async Task STAR_the_published_holdout_mentions_no_outcome()
    {
        using var client = fx.Client();
        var body = await client.GetStringAsync("/api/noise/holdout/2026-09", Ct);

        foreach (var w in new[] { "noisePct", "findingCount", "verdict", "judged", "score" })
        {
            Assert.DoesNotContain(w, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A period with no published draw is 404, not an empty holdout. An empty draw reads as "we measured
    /// nothing there", which is a different and false claim.
    /// </summary>
    [Fact]
    public async Task An_unpublished_period_is_not_found_rather_than_empty()
    {
        using var client = fx.Client();
        var response = await client.GetAsync("/api/noise/holdout/1999-01", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The corpus the draw is made from publishes too — a pool nobody can see is not auditable.</summary>
    [Fact]
    public async Task The_candidate_corpus_publishes_so_the_draw_can_be_re_run()
    {
        var json = await GetJsonAsync("/api/noise/corpus");

        var repos = json.GetProperty("repositories").EnumerateArray().ToList();
        Assert.NotEmpty(repos);
        Assert.True(json.GetProperty("count").GetInt32() == repos.Count);
    }
}
