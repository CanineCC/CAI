namespace Cai.Scoring;

/// <summary>One category's roll-up: its confidence-weighted 0–100 score (null when nothing deterministic measured it),
/// the lens it feeds, and how many dimensions folded into it.</summary>
public sealed record CategoryResult(string Category, string Lens, double? Score, int DimensionCount);

/// <summary>One lens's result in a CAI: the rolled-up 0–100 score, its band (quality-bar-adjusted, capped at Fair when
/// critical-gated), whether a critical contributor gated it, how many items folded into it, and its share of the
/// headline (worst-first OWA weight, and weight×score contribution).</summary>
public sealed record LensResult(string Lens, double Score, Band Band, bool CriticalGated, int ItemCount, double Weight, double Contribution);

/// <summary>A computed CAI: the 0–100 headline, its (coherence-capped) band, the rubric version, the per-lens roll-up,
/// the per-category breakdown, the legacy equal-weight aggregate, the unweighted category mean, and the coherence note
/// (non-empty when the headline band was capped to not out-promise the weakest category).</summary>
public sealed record CaiScore(
    double Headline,
    Band Band,
    string RubricVersion,
    IReadOnlyList<LensResult> Lenses,
    IReadOnlyList<CategoryResult> Categories,
    double Aggregate,
    double? CategoryMean,
    string CoherenceNote)
{
    /// <summary>Back-compat view: the across-lens contributions (lens, score, weight, contribution).</summary>
    public IReadOnlyList<LensContribution> Contributions =>
        Lenses.Select(l => new LensContribution(l.Lens, l.Score, l.Weight, l.Contribution)).ToList();
}

/// <summary>One lens's across-lens contribution to the headline.</summary>
public sealed record LensContribution(string Lens, double Score, double Weight, double Contribution);

/// <summary>The outcome of reproducing a published headline from its evidence.</summary>
public sealed record VerifyResult(bool Reproduced, double Computed, double Claimed, double Delta, double Tolerance);

/// <summary>
/// The CAI reference scorer — the open, reproducible rubric fold. Measuring a codebase (producing an
/// <see cref="EvidenceBundle"/>) is the analyzer's job; turning that evidence into the headline + ten lens scores is
/// this library's, and it is the SINGLE authority: anyone with the same bundle and rubric gets the same number.
///
/// The fold is three stages, worst-first throughout (a weak area drags the result instead of being averaged away):
/// <list type="number">
/// <item>Each deterministic dimension's <c>effective = score × coverage</c> rolls into its CATEGORY as a
///   confidence-weighted mean (0–100); LLM-advisory dimensions are excluded from the number.</item>
/// <item>Each LENS is the worst-first OWA (<c>q = 0.75</c>) of its category scores plus its meta-dimensions (score×10).
///   The Architecture lens is then floored by analyzable surface (dropped on a 0-project repo). A measured,
///   non-advisory contributor below <c>4.0/10</c> critical-gates the lens — its band caps at Fair, never its number.</item>
/// <item>The HEADLINE is the sharper worst-first OWA (<c>q = 0.55</c>) of the measured lens scores. Its band is capped
///   so it never out-promises the weakest category (<see cref="BandCoherence"/>); lens bands follow the quality bar
///   (<see cref="QualityBarBands"/>).</item>
/// </list>
/// </summary>
public static class CaiScorer
{
    /// <summary>Geometric decay of the WITHIN-lens worst-first OWA: a lens carries many items (15+), so a sharp decay
    /// would let the best ten weigh almost nothing. q = 0.75 keeps the worst item dominant while the rest still count.</summary>
    public const double WithinLensQ = 0.75;

    /// <summary>Geometric decay of the ACROSS-lens worst-first OWA: the headline folds only 4–8 lenses, so a sharper
    /// q = 0.55 (≈46% on the worst of six) makes the weakest area dominate without the min()-degeneracy.</summary>
    public const double AcrossLensQ = 0.55;

    /// <summary>A measured, non-advisory contributor below this (0–10) is Critical and gates its lens's displayed band
    /// at Fair — the gate changes the band, never the number.</summary>
    public const double CriticalGate = 4.0;

    private const double ShippedWeightTolerance = 0.02;

    /// <summary>Score an evidence bundle. Folds dimensions + meta-dimensions when present (the primary path); falls back
    /// to pre-computed lens scores for a thin bundle.</summary>
    public static CaiScore Score(EvidenceBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.Dimensions.Count > 0 || bundle.MetaDimensions.Count > 0)
        {
            return ScoreFromEvidence(bundle);
        }

        if (bundle.Lenses.Count > 0)
        {
            return ScoreFromLensScores(bundle);
        }

        throw new ArgumentException("Evidence bundle carries no dimensions, meta-dimensions or lens scores.", nameof(bundle));
    }

    /// <summary>The full fold: dimensions → categories → lenses (with the architecture surface floor) → headline.</summary>
    private static CaiScore ScoreFromEvidence(EvidenceBundle bundle)
    {
        Validate(bundle);

        // ── Stage 1: per-category confidence-weighted roll-up (0–100). Advisory dimensions are kept out of the number.
        // gatedDimsByLens records the critical (<4.0 effective) contributors so a lens with one can cap its band.
        var categories = new List<CategoryResult>();
        var categoryScoresByLens = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var gatedByLens = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var group in bundle.Dimensions.GroupBy(d => Categories.Parse(d.Category)))
        {
            var lens = Categories.LensOf(group.Key);
            var measured = group.Where(d => !d.Advisory).ToList();
            var confidenceSum = measured.Sum(d => d.Confidence);
            double? score = confidenceSum <= 0
                ? null
                : measured.Sum(d => Effective(d) * d.Confidence) / confidenceSum * 10.0;

            categories.Add(new CategoryResult(group.Key.ToString(), lens, score, group.Count()));

            if (score is { } s)
            {
                Bucket(categoryScoresByLens, lens).Add(s);
            }

            foreach (var d in measured.Where(d => d.Confidence > 0 && Effective(d) < CriticalGate))
            {
                Bucket(gatedByLens, lens).Add(d.Id);
            }
        }

        // Meta-dimensions feed their lens directly at score×10 (measured, non-advisory only).
        var metaScoresByLens = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var meta in bundle.MetaDimensions.Where(m => m is { Advisory: false, ScoreZeroToTen: not null }))
        {
            Bucket(metaScoresByLens, meta.Lens).Add(meta.ScoreZeroToTen!.Value * 10.0);
            if (meta.ScoreZeroToTen.Value < CriticalGate)
            {
                Bucket(gatedByLens, meta.Lens).Add(meta.Id);
            }
        }

        // ── Stage 2: fold each lens (worst-first OWA q=0.75 over its categories + meta), then floor Architecture.
        var folded = new List<(string Lens, double? Score, int Items, bool Gated)>();
        foreach (var lens in LensCatalog.All.Select(l => l.Key))
        {
            var items = new List<double>();
            if (categoryScoresByLens.TryGetValue(lens, out var cats))
            {
                items.AddRange(cats);
            }

            if (metaScoresByLens.TryGetValue(lens, out var metas))
            {
                items.AddRange(metas);
            }

            double? lensScore = items.Count > 0 ? RankWeightedOwa(items, WithinLensQ) : null;
            if (string.Equals(lens, "architecture", StringComparison.Ordinal))
            {
                lensScore = ArchitectureSurfaceFloor.Apply(lensScore, bundle.AnalyzableProjects, bundle.ProductionLoc);
            }

            var gated = lensScore is not null && gatedByLens.TryGetValue(lens, out var ids) && ids.Count > 0;
            folded.Add((lens, lensScore, items.Count, gated));
        }

        // ── Stage 3: headline = across-lens worst-first OWA (q=0.55) over the measured lenses.
        var measuredLenses = folded.Where(f => f.Score is not null)
            .Select(f => (f.Lens, Score: f.Score!.Value, f.Items, f.Gated)).ToList();
        if (measuredLenses.Count == 0)
        {
            throw new ArgumentException("No measured lenses to score.", nameof(bundle));
        }

        var weights = OwaWeights(measuredLenses.Select(l => l.Score).ToList(), AcrossLensQ);
        var lensResults = measuredLenses.Zip(weights, (l, w) =>
        {
            var band = QualityBarBands.ForLens(l.Score, bundle.QualityBar, l.Lens);
            return new LensResult(l.Lens, l.Score, CapBand(band, l.Gated), l.Gated, l.Items, w, l.Score * w);
        }).ToList();

        var headline = lensResults.Sum(r => r.Contribution);

        // Bands + transparency from the measured category scores (the coherence gate's reference set).
        var measuredCategoryScores = categories.Where(c => c.Score is not null).Select(c => c.Score!.Value).ToList();
        var (headlineBand, coherenceNote) = BandCoherence.Cap(Bands.For(headline), measuredCategoryScores);
        var aggregate = measuredCategoryScores.Count > 0 ? measuredCategoryScores.Average() : 0.0;

        return new CaiScore(
            headline, headlineBand, bundle.RubricVersion,
            lensResults.OrderBy(r => LensCatalog.Order(r.Lens)).ToList(),
            categories.OrderBy(c => c.Category, StringComparer.Ordinal).ToList(),
            aggregate, BandCoherence.CategoryMean(measuredCategoryScores), coherenceNote);
    }

    /// <summary>Fallback: a bundle that carries pre-computed lens scores but no dimension evidence (a thin sidecar).
    /// Uses the shipped OWA weights when present (exact reproduction), else derives the across-lens worst-first
    /// weights. Carries no category layer, so the legacy aggregate and coherence note are empty.</summary>
    private static CaiScore ScoreFromLensScores(EvidenceBundle bundle)
    {
        foreach (var l in bundle.Lenses.Where(l => l.Score is < 0 or > 100))
        {
            throw new ArgumentException($"Lens '{l.Lens}' score {l.Score} is outside 0–100.", nameof(bundle));
        }

        var ordered = bundle.Lenses.OrderBy(l => LensCatalog.Order(l.Lens)).ToList();
        var shippedSum = ordered.Sum(l => l.OwaWeight);
        var weights = Math.Abs(shippedSum - 1.0) <= ShippedWeightTolerance && ordered.All(l => l.OwaWeight > 0)
            ? ordered.Select(l => l.OwaWeight).ToList()
            : OwaWeights(ordered.Select(l => l.Score).ToList(), AcrossLensQ);
        var results = ordered.Zip(weights, (l, w) => new LensResult(
            l.Lens, l.Score, Bands.For(l.Score), CriticalGated: false, ItemCount: 0, w, l.Score * w)).ToList();
        var headline = results.Sum(r => r.Contribution);
        return new CaiScore(headline, Bands.For(headline), bundle.RubricVersion, results, [], 0.0, null, "");
    }

    /// <summary>Reproduce a published headline from its evidence — recompute and compare to the claimed
    /// <see cref="EvidenceBundle.HeadlineScore"/> within <paramref name="tolerance"/> (default ±0.5).</summary>
    public static VerifyResult Verify(EvidenceBundle bundle, double tolerance = 0.5)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.HeadlineScore is not { } claimed)
        {
            throw new ArgumentException("Evidence bundle carries no headlineScore to verify against.", nameof(bundle));
        }

        var computed = Score(bundle).Headline;
        var delta = Math.Abs(computed - claimed);
        return new VerifyResult(delta <= tolerance, computed, claimed, delta, tolerance);
    }

    /// <summary>A dimension's effective contribution to its category: <c>base × coverage</c> (the enforcement rung no
    /// longer penalises the score).</summary>
    private static double Effective(DimensionScore d) => d.ScoreZeroToTen * d.Coverage;

    /// <summary>The worst-first OWA value of <paramref name="scores"/> with decay <paramref name="q"/>.</summary>
    public static double RankWeightedOwa(IReadOnlyList<double> scores, double q)
    {
        var w = OwaWeights(scores, q);
        return scores.Zip(w, (s, wi) => s * wi).Sum();
    }

    /// <summary>The worst-first OWA weight vector for <paramref name="scores"/>: rank worst→best, the rank-r item weighs
    /// <c>q^r</c>, normalized to sum to 1. Returned aligned to the INPUT order. Stable: equal scores keep input order.</summary>
    public static IReadOnlyList<double> OwaWeights(IReadOnlyList<double> scores, double q)
    {
        ArgumentNullException.ThrowIfNull(scores);
        var n = scores.Count;
        if (n == 0)
        {
            return [];
        }

        var order = Enumerable.Range(0, n).OrderBy(i => scores[i]).ThenBy(i => i).ToList(); // worst first
        var raw = new double[n];
        for (var rank = 0; rank < n; rank++)
        {
            raw[order[rank]] = Math.Pow(q, rank);
        }

        var total = raw.Sum();
        return raw.Select(w => w / total).ToList();
    }

    /// <summary>The critical gate: a gated lens's displayed band is capped at Fair.</summary>
    private static Band CapBand(Band band, bool gated) => gated && band > Band.Fair ? Band.Fair : band;

    private static List<double> Bucket(Dictionary<string, List<double>> map, string key) =>
        map.TryGetValue(key, out var list) ? list : map[key] = [];

    private static List<string> Bucket(Dictionary<string, List<string>> map, string key) =>
        map.TryGetValue(key, out var list) ? list : map[key] = [];

    private static void Validate(EvidenceBundle bundle)
    {
        foreach (var d in bundle.Dimensions.Where(d => d.ScoreZeroToTen is < 0 or > 10))
        {
            throw new ArgumentException($"Dimension '{d.Id}' score {d.ScoreZeroToTen} is outside 0–10.", nameof(bundle));
        }

        foreach (var d in bundle.Dimensions.Where(d => d.Coverage is < 0 or > 1))
        {
            throw new ArgumentException($"Dimension '{d.Id}' coverage {d.Coverage} is outside 0–1.", nameof(bundle));
        }

        foreach (var m in bundle.MetaDimensions.Where(m => m.ScoreZeroToTen is < 0 or > 10))
        {
            throw new ArgumentException($"Meta-dimension '{m.Id}' score {m.ScoreZeroToTen} is outside 0–10.", nameof(bundle));
        }
    }
}
