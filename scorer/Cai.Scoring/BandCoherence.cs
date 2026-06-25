namespace Cai.Scoring;

/// <summary>
/// Worst-category coherence gate (D-395): the headline band may not out-promise the report's own category table. The
/// headline BAND sits at most ONE band above the worst measured category — a repo whose security category reads Fair
/// ("largest drag, prioritise here") cannot band Exemplary on the cover. The NUMBER is unchanged (lens weighting is a
/// deliberate product decision); only the label stops over-promising.
/// </summary>
public static class BandCoherence
{
    /// <summary>The headline band capped at one band above the worst measured category, with a human note when the cap
    /// bit. <paramref name="measuredCategoryScores"/> are the 0–100 scores of the categories that produced data (null
    /// categories are already excluded by the caller). An empty set leaves the headline band untouched.</summary>
    public static (Band Band, string Note) Cap(Band headline, IReadOnlyList<double> measuredCategoryScores)
    {
        ArgumentNullException.ThrowIfNull(measuredCategoryScores);
        if (measuredCategoryScores.Count == 0)
        {
            return (headline, "");
        }

        var worst = measuredCategoryScores.Min();
        var worstBand = Bands.For(worst);
        var cappedTier = (Band)Math.Min((int)headline, (int)worstBand + 1);
        if (cappedTier >= headline)
        {
            return (headline, "");
        }

        var note = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Band capped at {cappedTier.Label()}: the weakest category ({worst:0}%) reads {worstBand.Label()} — the headline never out-promises the category table.");
        return (cappedTier, note);
    }

    /// <summary>The unweighted mean of the measured category scores — disclosed beside the weighted headline so the two
    /// aggregations can be compared at a glance. Null when nothing was measured.</summary>
    public static double? CategoryMean(IReadOnlyList<double> measuredCategoryScores)
    {
        ArgumentNullException.ThrowIfNull(measuredCategoryScores);
        return measuredCategoryScores.Count > 0 ? measuredCategoryScores.Average() : null;
    }
}
