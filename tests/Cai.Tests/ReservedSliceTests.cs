using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The permanently pristine slice: repositories nobody develops against, ever.
/// </summary>
/// <remarks>
/// <para>★★ WITHOUT AN ENDPOINT THE DECAY CURVE MEASURES NOTHING. 02 §1 is explicit: the recency strata compare
/// "never trained" against "trained N cycles ago", and if every repository is eventually developed against then
/// the never-trained bucket empties as the standard matures — the overfitting gap becomes uncomputable exactly
/// when the tools have had time to overfit. "One cycle of cooling off is enough" stays an assertion rather than a
/// result.</para>
///
/// <para>★★ SO THE RESERVATION IS ENFORCED, NOT REQUESTED. The corpus marks the slice, the sampler always draws
/// from it, and a submission that declares a reserved repository as trained is REFUSED — that declaration IS the
/// reservation being broken, and it is the only moment anybody outside the vendor can see it happen.</para>
/// </remarks>
public sealed class ReservedSliceTests
{
    [Fact]
    public void STAR_The_Corpus_Reserves_Repositories_And_Says_Which()
    {
        // ★★ In the SIGNED manifest, so the reservation is part of what the signature covers. A slice recorded
        // only in code could be quietly un-reserved in the commit that needed it un-reserved.
        var reserved = CorpusManifest.Load().Candidates.Where(c => c.Reserved).ToList();

        Assert.NotEmpty(reserved);
        Assert.True(reserved.Count >= NoiseCorpus.MinimumReservedRepositories,
            $"only {reserved.Count} reserved; the decay curve needs at least "
          + $"{NoiseCorpus.MinimumReservedRepositories} to have an endpoint at all");
    }

    [Fact]
    public void STAR_Every_Draw_Includes_The_Reserved_Slice()
    {
        // ★★ ALWAYS, not usually. A holdout that happened to miss the reserved repositories in a given period
        // would have no never-trained bucket for that period — and nothing in the number would say so.
        var draw = HoldoutSampler.Draw(
            NoiseCorpus.Draws["2026-09"].Seed, NoiseCorpus.Candidates, NoiseCorpus.Rules);

        var reserved = NoiseCorpus.Candidates.Where(c => c.Reserved).Select(c => c.RepoId).ToList();

        Assert.All(reserved, repoId =>
            Assert.Contains(repoId, draw.Select(d => d.RepoId)));
    }

    [Fact]
    public void STAR_A_Reserved_Repository_Is_Drawn_Under_EVERY_Seed()
    {
        // ★★ The property that makes it a reservation rather than a coincidence of one seed. Ten unrelated seeds,
        // and the reserved slice is in all ten — otherwise "reserved" means "usually included".
        var reserved = NoiseCorpus.Candidates.Where(c => c.Reserved).Select(c => c.RepoId).ToHashSet();

        foreach (var seed in Enumerable.Range(1, 10).Select(i => $"probe-seed-{i}"))
        {
            var draw = HoldoutSampler.Draw(seed, NoiseCorpus.Candidates, NoiseCorpus.Rules)
                .Select(d => d.RepoId)
                .ToHashSet();

            Assert.True(reserved.IsSubsetOf(draw), $"seed '{seed}' dropped part of the reserved slice");
        }
    }

    [Fact]
    public void STAR_The_Reserved_Slice_Does_Not_Crowd_Out_The_Rest_Of_The_Draw()
    {
        // ★ A draw that was ONLY the reserved slice would be pristine and useless: the rate would say nothing
        // about code anybody works on. The reservation adds an endpoint; it does not replace the sample.
        var draw = HoldoutSampler.Draw(
            NoiseCorpus.Draws["2026-09"].Seed, NoiseCorpus.Candidates, NoiseCorpus.Rules);

        var unreserved = draw.Count(d =>
            NoiseCorpus.Candidates.Single(c => c.RepoId == d.RepoId) is { Reserved: false });

        Assert.True(unreserved > 0, "the draw is entirely reserved repositories");
    }
}

/// <summary>
/// Declaring a reserved repository as trained, over the wire.
/// </summary>
/// <remarks>
/// ★★ THE ONE MOMENT THE RESERVATION CAN BE SEEN TO BREAK. Nothing CAI can do stops a vendor developing against a
/// repository — but the recency declaration is where they have to say so, and refusing it there makes breaking the
/// reservation an act rather than an omission. Accepted quietly, the never-trained bucket would fill with
/// repositories that are no longer never-trained, and the overfitting gap would read as zero for the best possible
/// reason and the worst possible cause.
/// </remarks>
public sealed class ReservedSliceApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Tool([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"reserved-{caller}";

    private async Task<List<(string RepoId, string Sha, bool Reserved)>> HoldoutAsync()
    {
        using var client = fx.Client();
        var text = await client.GetStringAsync("/api/noise/holdout/2026-09", Ct);

        return [.. JsonDocument.Parse(text).RootElement.GetProperty("repositories").EnumerateArray()
            .Select(r => (
                r.GetProperty("repoId").GetString()!,
                r.GetProperty("pinnedSha").GetString()!,
                r.TryGetProperty("reserved", out var res) && res.GetBoolean()))];
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SubmitAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/submissions", payload, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static object Submission(
        string tool, List<(string RepoId, string Sha, bool Reserved)> holdout,
        Func<(string RepoId, string Sha, bool Reserved), string> stratum) => new
    {
        period = "2026-09",
        tool,
        toolVersion = "engine-8b08d6c6",
        runStartedAt = "2026-08-20T09:00:00Z",
        configuration = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
        recency = holdout.Select(h => new { repoId = h.RepoId, stratum = stratum(h) }),
        findings = holdout.Select(h => new
        {
            repoId = h.RepoId, pinnedSha = h.Sha, filePath = "src/Thing.cs", line = 42,
            ruleId = "D4", title = "a finding", claimClass = "pointwise",
        }),
        reportedFindingCount = holdout.Count,
    };

    [Fact]
    public async Task STAR_The_Holdout_Says_Which_Repositories_Are_RESERVED()
    {
        // ★ A vendor cannot honour a reservation it cannot see. Published with the draw, not in a document.
        var holdout = await HoldoutAsync();

        Assert.Contains(holdout, h => h.Reserved);
    }

    [Fact]
    public async Task STAR_Declaring_A_RESERVED_Repository_As_Trained_Is_REFUSED()
    {
        // ★★ THE ENFORCEMENT. Accepted quietly, the never-trained bucket fills with repositories that are no
        // longer never-trained, and the overfitting gap reads as zero for the best possible reason and the worst
        // possible cause.
        var holdout = await HoldoutAsync();
        var (status, body) = await SubmitAsync(Submission(
            Tool(), holdout, h => h.Reserved ? "trained-one-cycle-ago" : "never-trained"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("accepted").GetBoolean());

        var problems = string.Join(" ", body.GetProperty("problems").EnumerateArray().Select(p => p.GetString()));
        Assert.Contains("reserved", problems, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(holdout.First(h => h.Reserved).RepoId, problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Reserved_Repository_Declared_Never_Trained_Is_Fine()
    {
        var holdout = await HoldoutAsync();
        var (_, body) = await SubmitAsync(Submission(Tool(), holdout, _ => "never-trained"));

        var problems = string.Join(" ", body.GetProperty("problems").EnumerateArray().Select(p => p.GetString()));
        Assert.DoesNotContain("reserved", problems, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Publication_States_The_Reserved_Slice_Size()
    {
        // ★★ The size is what tells a reader whether the overfitting gap rests on three repositories or thirty —
        // and the gap is computed against the never-trained stratum, which is exactly this slice.
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/publication", new Dictionary<string, object?>
        {
            ["period"] = "2026-09",
            ["reported"] = 2000,
            ["adjudicated"] = 1800,
            ["excluded"] = 36,
            ["unrated"] = 164,
            ["validAndActionable"] = 900,
            ["validNotActionable"] = 360,
            ["noise"] = 540,
            ["clusters"] = 10,
            ["locCovered"] = 4_200_000L,
            ["recallEstimate"] = 0.62,
            ["recallMethod"] = "pooled-union",
            ["claimClasses"] = new object[] { new { claimClass = "pointwise", judged = 1800, noise = 540 } },
            ["toolVersion"] = "watchdog-engine 2026.08.3",
            ["holdoutSeed"] = "cai-2026-09-9f2b41c7e0a85d36",
            ["modelSet"] = "judge-a@2026-07",
            ["gitMiningVerified"] = true,
            ["configuration"] = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
            ["fixRateUnavailable"] = "fixture",
            ["rejudgeUnavailable"] = "fixture",
        }, Ct);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        var recency = body.GetProperty("recency");

        Assert.Equal(
            CorpusManifest.Load().Candidates.Count(c => c.Reserved),
            recency.GetProperty("reservedRepositories").GetInt32());
        Assert.Contains("never used for development",
            recency.GetProperty("reservedNote").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Method_Publishes_The_Reservation_As_A_RULE()
    {
        using var client = fx.Client();
        var method = JsonDocument.Parse(await client.GetStringAsync("/api/noise/method", Ct)).RootElement;

        var reserved = method.GetProperty("reservedSlice");
        Assert.True(reserved.GetProperty("alwaysDrawn").GetBoolean());
        Assert.True(reserved.GetProperty("repositories").GetInt32() > 0);
        Assert.Contains("decay curve", reserved.GetProperty("why").GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refused", reserved.GetProperty("declaringItTrained").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }
}
