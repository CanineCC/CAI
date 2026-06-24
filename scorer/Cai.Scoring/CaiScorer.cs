namespace Cai.Scoring;

/// <summary>A computed CAI: the 0–100 headline, its band, the rubric version, and each lens's contribution.</summary>
public sealed record CaiScore(double Headline, Band Band, string RubricVersion, IReadOnlyList<LensContribution> Contributions);

/// <summary>One lens's share of the headline: its score, the weight applied, and score×weight.</summary>
public sealed record LensContribution(string Lens, double Score, double Weight, double Contribution);

/// <summary>The outcome of reproducing a published headline from its evidence.</summary>
public sealed record VerifyResult(bool Reproduced, double Computed, double Claimed, double Delta, double Tolerance);

/// <summary>
/// The CAI reference scorer. The headline is an Ordered-Weighted-Average fold of the measured lens scores:
/// <c>headline = Σ (lensScore × owaWeight)</c>. The weights are WORST-FIRST — the weakest lenses carry the most weight,
/// so a codebase cannot average a serious weakness away behind its strengths.
///
/// Reproducibility has two honest tiers:
///  • A bundle that carries each lens's <c>owaWeight</c> (a Watchdog sidecar does) is reproduced EXACTLY — the weights
///    are published in the evidence, so anyone folds them and gets the same number, no engine required.
///  • A bundle that carries only lens SCORES is scored with the documented reference worst-first vector below — the
///    open default a rubric version pins. (The canonical weights are always shipped in the evidence; this vector is the
///    transparent fallback, not a competing source of truth.)
/// </summary>
public static class CaiScorer
{
    /// <summary>Weights are treated as "shipped" when the present lenses' OWA weights sum to ~1.</summary>
    private const double WeightSumTolerance = 0.02;

    public static CaiScore Score(EvidenceBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.Lenses.Count == 0)
        {
            throw new ArgumentException("Evidence bundle has no measured lenses.", nameof(bundle));
        }

        foreach (var l in bundle.Lenses)
        {
            if (l.Score is < 0 or > 100)
            {
                throw new ArgumentException($"Lens '{l.Lens}' score {l.Score} is outside 0–100.", nameof(bundle));
            }
        }

        var weights = ResolveWeights(bundle.Lenses);
        var contributions = bundle.Lenses
            .Select((l, i) => new LensContribution(l.Lens, l.Score, weights[i], l.Score * weights[i]))
            .ToList();
        var headline = contributions.Sum(c => c.Contribution);
        return new CaiScore(headline, Bands.For(headline), bundle.RubricVersion, contributions);
    }

    /// <summary>Reproduce a published headline from its evidence. The headline recomputed from the bundle must match the
    /// claimed <see cref="EvidenceBundle.HeadlineScore"/> within <paramref name="tolerance"/> (default ±0.5, i.e. it
    /// rounds to the same integer). A mismatch is falsifiable proof the published number does not follow from the evidence.</summary>
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

    /// <summary>The weights to apply: the bundle's own OWA weights when they're present and normalized (exact
    /// reproduction), else the documented reference worst-first vector.</summary>
    private static IReadOnlyList<double> ResolveWeights(IReadOnlyList<LensScore> lenses)
    {
        var shipped = lenses.Sum(l => l.OwaWeight);
        if (Math.Abs(shipped - 1.0) <= WeightSumTolerance && lenses.All(l => l.OwaWeight > 0))
        {
            return lenses.Select(l => l.OwaWeight).ToList();
        }

        return ReferenceWorstFirstWeights(lenses.Select(l => l.Score).ToList());
    }

    /// <summary>The reference WORST-FIRST OWA weight vector for N lenses: rank the lenses worst→best, then apply a
    /// linearly-descending weight (rank N gets N, … best gets 1), normalized to sum to 1. The weakest lens therefore
    /// carries the largest share. Deterministic; ties broken by the lens's position in the bundle.</summary>
    public static IReadOnlyList<double> ReferenceWorstFirstWeights(IReadOnlyList<double> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        var n = scores.Count;
        if (n == 0)
        {
            return [];
        }

        // Ascending rank by score (worst = rank 0). Stable: equal scores keep bundle order.
        var order = Enumerable.Range(0, n).OrderBy(i => scores[i]).ThenBy(i => i).ToList();
        var raw = new double[n];
        for (var rank = 0; rank < n; rank++)
        {
            raw[order[rank]] = n - rank; // worst (rank 0) → n, best → 1
        }

        var total = (double)(n * (n + 1) / 2);
        return raw.Select(w => w / total).ToList();
    }
}
