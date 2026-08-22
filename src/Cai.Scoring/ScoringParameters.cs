using System.Text.Json.Serialization;

namespace Cai.Scoring;

/// <summary>
/// The score-moving constants of the fold, as DATA rather than compile-time constants — so a rubric version can pin
/// them and an old report replays under the parameters it was actually computed with.
/// <para>ADR-0004 requires that "a catalog must pin every input that can move a score." These are those inputs: the two
/// OWA decays, the critical gate, the architecture surface floor, the band cutlines and the quality-bar shift. Holding
/// them here means <see cref="RubricCatalog"/> can carry them, and the scorer never needs a version-dispatch switch —
/// the semantics travel with the rubric instead of being reconstructed from its name.</para>
/// <para>A catalog that publishes no <c>scoring</c> block resolves to <see cref="Default"/>, whose values are exactly
/// the constants the scorer has always used, so every previously-published rubric version keeps verifying to the same
/// number. This is the same fallback the frozen dimension→category map uses for catalogs published before it existed.</para>
/// </summary>
public sealed record ScoringParameters
{
    /// <summary>Geometric decay of the WITHIN-lens worst-first OWA. A lens carries many items (15+), so a sharp decay
    /// would let the best ten weigh almost nothing.</summary>
    [JsonPropertyName("withinLensQ")] public double WithinLensQ { get; init; } = CaiScorer.WithinLensQ;

    /// <summary>Geometric decay of the ACROSS-lens worst-first OWA. The headline folds only 4–8 lenses, so a sharper
    /// decay makes the weakest area dominate without the min()-degeneracy.</summary>
    [JsonPropertyName("acrossLensQ")] public double AcrossLensQ { get; init; } = CaiScorer.AcrossLensQ;

    /// <summary>A measured, non-advisory contributor below this (0–10) is Critical and caps its lens's displayed band
    /// at Fair. The gate changes the band, never the number.</summary>
    [JsonPropertyName("criticalGate")] public double CriticalGate { get; init; } = CaiScorer.CriticalGate;

    /// <summary>The analyzable-surface floor applied to the Architecture lens.</summary>
    [JsonPropertyName("architectureSurface")] public ArchitectureSurfaceParameters ArchitectureSurface { get; init; } = new();

    /// <summary>The baseline band cutlines a 0–100 score is read through.</summary>
    [JsonPropertyName("bands")] public BandCutlines Bands { get; init; } = new();

    /// <summary>How the quality bar shifts the cutlines. The bar moves the band lines, never the score.</summary>
    [JsonPropertyName("qualityBar")] public QualityBarParameters QualityBar { get; init; } = new();

    /// <summary>The parameters the scorer has always used — the resolution for any catalog that publishes no
    /// <c>scoring</c> block, so historical rubric versions fold exactly as they did.</summary>
    public static ScoringParameters Default { get; } = new();
}

/// <summary>The architecture surface floor's thresholds. Cross-project architecture metrics are vacuously perfect on a
/// repo with almost no analyzable surface, so the lens is capped below the bar and dropped when nothing applies.</summary>
public sealed record ArchitectureSurfaceParameters
{
    /// <summary>Production projects needed before cross-project architecture metrics carry real signal.</summary>
    [JsonPropertyName("minProjects")] public int MinProjects { get; init; } = 2;

    /// <summary>Hand-written production LoC needed before those metrics carry real signal (a single big library can
    /// clear the bar on LoC alone).</summary>
    [JsonPropertyName("minProductionLoc")] public int MinProductionLoc { get; init; } = 1500;

    /// <summary>Cap (0–100) applied to the Architecture lens when surface is below the bar.</summary>
    [JsonPropertyName("lowSurfaceCap")] public double LowSurfaceCap { get; init; } = 69.0;
}

/// <summary>The four baseline band cutlines (top-down). A score at or above <see cref="Exemplary"/> is Exemplary, and
/// below <see cref="Poor"/> is Critical. These are the lines the published WORD is read off, which is why they are a
/// pinned input rather than a presentation detail.</summary>
public sealed record BandCutlines
{
    /// <summary>At or above this ⇒ Exemplary.</summary>
    [JsonPropertyName("exemplary")] public double Exemplary { get; init; } = 90.0;

    /// <summary>At or above this ⇒ Strong (Healthy).</summary>
    [JsonPropertyName("healthy")] public double Healthy { get; init; } = 70.0;

    /// <summary>At or above this ⇒ Adequate (Fair).</summary>
    [JsonPropertyName("fair")] public double Fair { get; init; } = 50.0;

    /// <summary>At or above this ⇒ Weak (Poor); below it ⇒ Critical.</summary>
    [JsonPropertyName("poor")] public double Poor { get; init; } = 25.0;

    /// <summary>The band a 0–100 score falls in under these cutlines.</summary>
    public Band For(double scoreZeroToOneHundred) =>
        scoreZeroToOneHundred >= Exemplary ? Band.Exemplary
        : scoreZeroToOneHundred >= Healthy ? Band.Healthy
        : scoreZeroToOneHundred >= Fair ? Band.Fair
        : scoreZeroToOneHundred >= Poor ? Band.Poor
        : Band.Critical;
}

/// <summary>
/// How the quality bar shifts the band cutlines (D-374): a per-bar offset scaled by a per-lens-group factor moves all
/// four lines together. The SCORE never changes — only where the colour bands fall — so the same code always scores the
/// same and stays comparable across repos; the bar changes how strict "green" is for the repo's criticality.
/// </summary>
public sealed record QualityBarParameters
{
    /// <summary>Offset added to the baseline cutlines per canonical bar tier (see <see cref="QualityBarTiers"/>).
    /// Lenient tiers subtract so "green" is easier; stricter tiers add. An unlisted tier shifts nothing.</summary>
    [JsonPropertyName("offsets")] public IReadOnlyDictionary<string, double> Offsets { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [QualityBarTiers.Prototype] = -18.0,
            [QualityBarTiers.Preview] = -8.0,
            [QualityBarTiers.Production] = 0.0,
            [QualityBarTiers.MissionCritical] = 6.0,
        };

    /// <summary>How fully each lens group follows the bar offset. Foundational code/architecture stay near-strict even
    /// for a prototype; operational maturity/readiness follow the bar fully; safety stays near-strict everywhere.</summary>
    [JsonPropertyName("lensGroupFactors")] public IReadOnlyDictionary<string, double> LensGroupFactors { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["foundational"] = 0.4,
            ["operational"] = 1.0,
            ["safety"] = 0.25,
            ["default"] = 0.7,
        };

    /// <summary>Highest cutline the shift may reach, so the lines stay sane and ordered at the extremes.</summary>
    [JsonPropertyName("exemplaryCeiling")] public double ExemplaryCeiling { get; init; } = 98.0;

    /// <summary>Lowest cutline the shift may reach.</summary>
    [JsonPropertyName("poorFloor")] public double PoorFloor { get; init; } = 5.0;

    /// <summary>The offset for a bar tier as written on the wire (aliases normalised); 0 when unlisted.</summary>
    public double OffsetFor(string? barTier) =>
        Offsets.TryGetValue(QualityBarTiers.Canonical(barTier), out var o) ? o : 0.0;

    /// <summary>The follow-the-bar factor for a lens group; 0.7 when unlisted. Internal because
    /// <see cref="LensGroup"/> is: the wire form is the string key in <see cref="LensGroupFactors"/>.</summary>
    internal double FactorFor(LensGroup group) =>
        LensGroupFactors.TryGetValue(NameOf(group), out var f) ? f
        : LensGroupFactors.TryGetValue("default", out var d) ? d
        : 0.7;

    private static string NameOf(LensGroup group) => group switch
    {
        LensGroup.Foundational => "foundational",
        LensGroup.Operational => "operational",
        LensGroup.Safety => "safety",
        _ => "default",
    };
}

/// <summary>The canonical quality-bar tiers. The wire accepts several spellings per tier ("template", "poc",
/// "one-off" and "prototype" are one tier); the catalog keys its offsets by the canonical name so the alias set stays
/// a parsing concern and never a scoring one.</summary>
public static class QualityBarTiers
{
    /// <summary>Leanest bar — a template, PoC, one-off or prototype.</summary>
    public const string Prototype = "prototype";

    /// <summary>Pre-release — preview, alpha or beta.</summary>
    public const string Preview = "preview";

    /// <summary>The baseline bar, and the resolution for an absent or unrecognised one.</summary>
    public const string Production = "production";

    /// <summary>Strictest bar.</summary>
    public const string MissionCritical = "mission-critical";

    /// <summary>The canonical tier for a bar as written on the wire. An absent or unrecognised bar is
    /// <see cref="Production"/> — the baseline, never a lenient one.</summary>
    public static string Canonical(string? barTier) =>
        (barTier ?? "").Trim().ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal)
            .Replace("oneoff", "one-off", StringComparison.Ordinal)
            .Replace("missioncritical", "mission-critical", StringComparison.Ordinal)
        switch
        {
            "template" or "poc" or "one-off" or "prototype" => Prototype,
            "preview" or "alpha" or "beta" => Preview,
            "mission-critical" => MissionCritical,
            _ => Production,
        };
}
