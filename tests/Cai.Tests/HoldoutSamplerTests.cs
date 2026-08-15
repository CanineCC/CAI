using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The holdout draw — the property the whole standard rests on.
/// </summary>
/// <remarks>
/// <para>★ A holdout nobody can re-derive is worthless: the reader has only our word that it was not
/// chosen to flatter somebody. So the draw is a pure function of a PUBLISHED SEED and a PUBLIC candidate
/// pool, and anyone may run it and get the same repositories.</para>
/// <para>★★ And nothing about any scanner's output may reach it. Filtering repositories by how many
/// findings they produce conditions the sample on the thing being measured — tested on Watchdog's cycle
/// 1, where a 50–250 finding cap moved csharp −15.5 points and java +9.3.</para>
/// </remarks>
public sealed class HoldoutSamplerTests
{
    private static IReadOnlyList<HoldoutCandidate> Pool(int perLanguage, params string[] languages) =>
        [.. languages.SelectMany(lang =>
            Enumerable.Range(0, perLanguage).Select(i => new HoldoutCandidate(
                RepoId: $"{lang}/repo{i:D3}",
                Language: lang,
                ProductionLoc: 10_000 + (i * 137),
                Licence: "MIT",
                PinnedSha: $"{lang}{i:D3}".PadRight(40, '0'))))];

    private static HoldoutRules Rules(int targetLoc = 60_000, int ceiling = 400_000) =>
        new(TargetProductionLocPerLanguage: targetLoc, MaxRepositoryLoc: ceiling,
            MinRepositoriesPerLanguage: 3, MinRepositoriesPerSlice: 10);

    // ── Reproducibility ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ THE WHOLE POINT. Same seed, same pool, same draw — every time, in any process, by anybody.
    /// Without this the published holdout is an assertion rather than a fact.
    /// </summary>
    [Fact]
    public void STAR_the_same_seed_and_pool_always_draw_the_same_repositories()
    {
        var pool = Pool(40, "csharp", "go", "rust");

        var a = HoldoutSampler.Draw("seed-2026-09", pool, Rules());
        var b = HoldoutSampler.Draw("seed-2026-09", pool, Rules());

        Assert.Equal(
            a.Select(r => r.RepoId).ToList(),
            b.Select(r => r.RepoId).ToList());
    }

    /// <summary>A different seed draws a different sample, or the seed is decoration.</summary>
    [Fact]
    public void A_different_seed_draws_a_different_sample()
    {
        var pool = Pool(40, "csharp", "go", "rust");

        var a = HoldoutSampler.Draw("seed-2026-09", pool, Rules()).Select(r => r.RepoId).ToList();
        var b = HoldoutSampler.Draw("seed-2026-10", pool, Rules()).Select(r => r.RepoId).ToList();

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// The order the pool arrives in must not change the draw. A candidate list assembled by a different
    /// query, or a different filesystem, would otherwise produce a different "reproduction".
    /// </summary>
    [Fact]
    public void The_pool_order_does_not_change_the_draw()
    {
        var pool = Pool(40, "csharp", "go").ToList();
        var shuffled = pool.AsEnumerable().Reverse().ToList();

        Assert.Equal(
            HoldoutSampler.Draw("s", pool, Rules()).Select(r => r.RepoId).OrderBy(x => x, StringComparer.Ordinal),
            HoldoutSampler.Draw("s", shuffled, Rules()).Select(r => r.RepoId).OrderBy(x => x, StringComparer.Ordinal));
    }

    // ── Sizing ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ Sized by TARGET LoC, not by repository count. Precision follows the number of findings, and LoC
    /// is the outcome-blind proxy for that — repository count is neither.
    /// </summary>
    [Fact]
    public void STAR_each_language_is_drawn_to_a_target_LoC_not_a_repo_count()
    {
        var pool = Pool(60, "csharp", "go");

        var drawn = HoldoutSampler.Draw("s", pool, Rules(targetLoc: 60_000));

        foreach (var group in drawn.GroupBy(r => r.Language))
        {
            var loc = group.Sum(r => r.ProductionLoc);
            Assert.True(loc >= 60_000, $"{group.Key} drew {loc} LoC, short of the target");

            // …and stops once the target is met rather than taking the whole pool.
            var withoutLast = loc - group.OrderBy(r => r.ProductionLoc).Last().ProductionLoc;
            Assert.True(withoutLast < 60_000, $"{group.Key} overshot — it kept drawing past the target");
        }
    }

    /// <summary>
    /// ★ A pre-registered LoC ceiling keeps a single enormous repository out. Expressed in LoC and NEVER
    /// in findings: a finding cap is selection on the outcome, and it moved csharp −15.5 points when it
    /// was tried.
    /// </summary>
    [Fact]
    public void STAR_the_ceiling_is_expressed_in_LoC_and_excludes_the_untractable()
    {
        List<HoldoutCandidate> pool =
        [
            new("go/huge", "go", 5_000_000, "MIT", new string('a', 40)),
            .. Pool(20, "go"),
        ];

        var drawn = HoldoutSampler.Draw("s", pool, Rules(ceiling: 400_000));

        Assert.DoesNotContain(drawn, r => r.RepoId == "go/huge");
    }

    /// <summary>
    /// ★ Cluster floors. 500 findings from one repository carry far less information than 500 from
    /// twenty — same codebase, same conventions, correlated errors — so a language must reach a minimum
    /// repository count even after its LoC target is met.
    /// </summary>
    [Fact]
    public void STAR_a_language_meets_its_repository_floor_even_once_the_LoC_target_is_met()
    {
        // Three repos would cover the target on size alone; the floor forces more.
        List<HoldoutCandidate> pool =
        [
            .. Enumerable.Range(0, 20).Select(i =>
                new HoldoutCandidate($"go/r{i:D2}", "go", 30_000, "MIT", $"go{i:D2}".PadRight(40, '0'))),
        ];

        var drawn = HoldoutSampler.Draw("s", pool, Rules(targetLoc: 60_000));

        Assert.True(drawn.Count >= 3, $"drew {drawn.Count}, below the per-language floor");
    }

    /// <summary>A language that cannot reach its floor is reported as short, never silently thinned.</summary>
    [Fact]
    public void A_language_that_cannot_fill_its_floor_draws_what_it_has()
    {
        List<HoldoutCandidate> pool =
        [
            new("vbnet/a", "vbnet", 5_000, "MIT", new string('b', 40)),
            new("vbnet/b", "vbnet", 5_000, "MIT", new string('c', 40)),
        ];

        var drawn = HoldoutSampler.Draw("s", pool, Rules());

        Assert.Equal(2, drawn.Count);
    }

    // ── Blindness ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ NOTHING ABOUT ANY SCANNER'S OUTPUT MAY REACH THE DRAW, asserted structurally so a future field
    /// cannot slip in. A candidate carries objective attributes only — language, size, licence, sha.
    /// </summary>
    [Fact]
    public void STAR_a_candidate_carries_no_outcome_of_any_kind()
    {
        var names = typeof(HoldoutCandidate).GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(["RepoId", "Language", "ProductionLoc", "Licence", "PinnedSha"], names);

        foreach (var w in new[] { "Noise", "Finding", "Verdict", "Score", "Judged", "Rate" })
        {
            Assert.DoesNotContain(names, n => n.Contains(w, StringComparison.Ordinal));
        }
    }
}
