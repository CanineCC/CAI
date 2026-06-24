namespace Cai.Scoring;

/// <summary>One lens's result in a CAI: the rolled-up 0–100 score, its band (capped at Fair when critical-gated), how
/// many dimensions folded into it, and its share of the headline.</summary>
public sealed record LensResult(string Lens, double Score, Band Band, bool CriticalGated, int DimensionCount, double Weight, double Contribution);

/// <summary>A computed CAI: the 0–100 headline, its band, the rubric version, and the per-lens roll-up.</summary>
public sealed record CaiScore(double Headline, Band Band, string RubricVersion, IReadOnlyList<LensResult> Lenses)
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
/// The CAI reference scorer — the real two-stage rank-weighted ordered-weighted-average fold from the scoring spec.
/// Each of the ~124 dimensions scores 0–10. WITHIN a lens, its dimensions fold by a worst-first OWA (the i-th worst
/// weighs <c>q^(i-1)</c>, <c>q = 0.75</c>) into a 0–100 lens score. ACROSS lenses, the lens scores fold by a sharper
/// worst-first OWA (<c>q = 0.55</c>, ≈46% on the worst of six) into the 0–100 headline. The weights are DERIVED from
/// the rank ordering, so the headline is computed from the dimension scores alone — nothing opaque.
///
/// A measured, non-advisory dimension below <c>4.0/10</c> CRITICAL-GATES its lens: the lens's displayed band is capped
/// at Fair (the gate changes the band, never the number). LLM-advisory dimensions are excluded from the number.
/// </summary>
public static class CaiScorer
{
    public const double WithinLensQ = 0.75;
    public const double AcrossLensQ = 0.55;
    public const double CriticalGate = 4.0; // /10
    private const double ShippedWeightTolerance = 0.02;

    public static CaiScore Score(EvidenceBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.Dimensions.Count > 0)
        {
            return ScoreFromDimensions(bundle);
        }

        if (bundle.Lenses.Count > 0)
        {
            return ScoreFromLensScores(bundle);
        }

        throw new ArgumentException("Evidence bundle carries neither dimensions nor lens scores.", nameof(bundle));
    }

    /// <summary>The real fold: dimensions → lenses (within-lens OWA) → headline (across-lens OWA).</summary>
    private static CaiScore ScoreFromDimensions(EvidenceBundle bundle)
    {
        var measured = bundle.Dimensions.Where(d => d.Confidence > 0 && !d.Advisory).ToList();
        if (measured.Count == 0)
        {
            throw new ArgumentException("No measured, non-advisory dimensions to score.", nameof(bundle));
        }

        foreach (var d in measured.Where(d => d.ScoreZeroToTen is < 0 or > 10))
        {
            throw new ArgumentException($"Dimension '{d.Id}' score {d.ScoreZeroToTen} is outside 0–10.", nameof(bundle));
        }

        // Within each lens: worst-first OWA of the dimension scores (0–10) → a 0–100 lens score.
        var lenses = measured.GroupBy(d => d.Lens, StringComparer.Ordinal)
            .Select(g =>
            {
                var scores10 = g.Select(d => d.ScoreZeroToTen).ToList();
                var lens100 = RankWeightedOwa(scores10, WithinLensQ) * 10.0;
                var gated = scores10.Any(s => s < CriticalGate);
                return (Lens: g.Key, Score: lens100, Count: g.Count(), Gated: gated);
            })
            .OrderBy(l => LensOrder(l.Lens))
            .ToList();

        var weights = OwaWeights(lenses.Select(l => l.Score).ToList(), AcrossLensQ);
        var results = lenses.Zip(weights, (l, w) => new LensResult(
            l.Lens, l.Score, CapBand(Bands.For(l.Score), l.Gated), l.Gated, l.Count, w, l.Score * w)).ToList();
        var headline = results.Sum(r => r.Contribution);
        return new CaiScore(headline, Bands.For(headline), bundle.RubricVersion, results);
    }

    /// <summary>Fallback: a bundle that carries lens scores but no dimensions (e.g. a thin sidecar). Uses the shipped
    /// OWA weights when present (exact reproduction), else derives the across-lens worst-first weights.</summary>
    private static CaiScore ScoreFromLensScores(EvidenceBundle bundle)
    {
        foreach (var l in bundle.Lenses.Where(l => l.Score is < 0 or > 100))
        {
            throw new ArgumentException($"Lens '{l.Lens}' score {l.Score} is outside 0–100.", nameof(bundle));
        }

        var ordered = bundle.Lenses.OrderBy(l => LensOrder(l.Lens)).ToList();
        var shippedSum = ordered.Sum(l => l.OwaWeight);
        var weights = Math.Abs(shippedSum - 1.0) <= ShippedWeightTolerance && ordered.All(l => l.OwaWeight > 0)
            ? ordered.Select(l => l.OwaWeight).ToList()
            : OwaWeights(ordered.Select(l => l.Score).ToList(), AcrossLensQ);
        var results = ordered.Zip(weights, (l, w) => new LensResult(
            l.Lens, l.Score, Bands.For(l.Score), CriticalGated: false, DimensionCount: 0, w, l.Score * w)).ToList();
        var headline = results.Sum(r => r.Contribution);
        return new CaiScore(headline, Bands.For(headline), bundle.RubricVersion, results);
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

    private static int LensOrder(string lens)
    {
        for (var i = 0; i < LensCatalog.All.Count; i++)
        {
            if (string.Equals(LensCatalog.All[i].Key, lens, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
