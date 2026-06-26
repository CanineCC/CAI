using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cai.Scoring;

/// <summary>
/// A CAI evidence bundle — the open, portable record a score is computed from. It is a documented SUBSET of an
/// analyzer's run sidecar: enough to recompute and verify the headline, nothing engine-specific. Producing a bundle
/// (measuring the code) is the analyzer's job; SCORING a bundle (this library) is open and reproducible — the same
/// evidence under the same rubric yields the same number, on anyone's machine.
///
/// The primary input is <see cref="Dimensions"/> (each deterministic dimension's raw 0–10 score, its category, and the
/// confidence/coverage it was measured at) plus <see cref="MetaDimensions"/> (the language-agnostic R/M/P/… signals
/// that feed a lens directly). <see cref="AnalyzableProjects"/> and <see cref="ProductionLoc"/> let the scorer apply
/// the architecture surface floor; <see cref="QualityBar"/> shifts the band cutlines for the repo's criticality. A
/// thin bundle that carries only pre-computed <see cref="Lenses"/> is also accepted (the legacy fallback path).
/// </summary>
public sealed record EvidenceBundle
{
    /// <summary>The frozen rubric version this evidence was produced under (e.g. "rubric-2026.08.15"). A score is only
    /// meaningful with its rubric version — same evidence + same rubric ⇒ the same number.</summary>
    [JsonPropertyName("rubricVersion")] public string RubricVersion { get; init; } = "";

    /// <summary>The commit the evidence was measured at (provenance; not part of the math).</summary>
    [JsonPropertyName("commit")] public string? Commit { get; init; }

    /// <summary>The quality bar the repo is judged against — "production" (default/baseline), "template"/"poc"/
    /// "prototype" or "preview"/"alpha"/"beta" (leaner), or "mission-critical" (stricter). Shifts the band cutlines
    /// only; never the score. Absent ⇒ production baseline.</summary>
    [JsonPropertyName("qualityBar")] public string? QualityBar { get; init; }

    /// <summary>Production (non-test, non-tooling) project count — input to the architecture surface floor: 0 drops the
    /// Architecture lens (nothing to grade), and a thin single-project surface caps it.</summary>
    [JsonPropertyName("analyzableProjects")] public int AnalyzableProjects { get; init; }

    /// <summary>Hand-written production lines of code — the second input to the architecture surface floor (a single
    /// big library clears the bar on LoC alone).</summary>
    [JsonPropertyName("productionLoc")] public int ProductionLoc { get; init; }

    /// <summary>The PUBLISHED headline (0–100) this evidence claims — what <see cref="CaiScorer.Verify"/> reproduces.
    /// Optional.</summary>
    [JsonPropertyName("headlineScore")] public double? HeadlineScore { get; init; }

    /// <summary>The measured deterministic dimensions — the PRIMARY evidence. The scorer folds them into category
    /// scores (confidence-weighted), then lenses, then the headline. A dimension measured at confidence 0 must be
    /// ABSENT, never a raw 0.0.</summary>
    [JsonPropertyName("dimensions")] public IReadOnlyList<DimensionScore> Dimensions { get; init; } = [];

    /// <summary>The language-agnostic meta-dimensions (R/M/P/AX/DM/…) — each feeds a lens DIRECTLY at score×10, beside
    /// that lens's categories. Null-scored (unmeasured) and advisory meta-dimensions are excluded from the number.</summary>
    [JsonPropertyName("metaDimensions")] public IReadOnlyList<MetaDimensionScore> MetaDimensions { get; init; } = [];

    /// <summary>Pre-computed lens scores — the FALLBACK input when a bundle carries no dimensions or meta-dimensions
    /// (e.g. a thin sidecar). When present with OWA weights they reproduce a published headline exactly; otherwise the
    /// across-lens fold derives the weights. Ignored when <see cref="Dimensions"/>/<see cref="MetaDimensions"/> carry
    /// evidence (those are folded instead).</summary>
    [JsonPropertyName("lenses")] public IReadOnlyList<LensInput> Lenses { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    /// <summary>Parse a bundle from its JSON wire form (case-insensitive, comments tolerated). Throws
    /// <see cref="JsonException"/> on malformed input or a null result. Round-trips with <see cref="ToJson"/>.</summary>
    public static EvidenceBundle Parse(string json) =>
        JsonSerializer.Deserialize<EvidenceBundle>(json, Options)
        ?? throw new JsonException("Evidence bundle deserialized to null.");

    /// <summary>Serialize this bundle to its indented JSON wire form — the inverse of <see cref="Parse"/>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);
}

/// <summary>One deterministic dimension's measured result: its raw 0–10 score, the category it rolls into, the coverage
/// the measurement reached, the confidence it was measured at, and whether it's an LLM-advisory dimension. A dimension
/// measured at confidence 0 is NOT measured — it must be ABSENT from the bundle, never a raw 0.0 (that would read as
/// "failed" instead of "not assessed"). An advisory dimension is shown band-only and excluded from the number.</summary>
public readonly record struct DimensionScore(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("score")] double ScoreZeroToTen,
    [property: JsonPropertyName("confidence")] double Confidence)
{
    /// <summary>Coverage fraction (0–1) the measurement reached — the effective score is <c>score × coverage</c>.
    /// Absent ⇒ full coverage (1.0).</summary>
    [JsonPropertyName("coverage")] public double Coverage { get; init; } = 1.0;

    /// <summary>True for an LLM-scored dimension — advisory, excluded from the deterministic fold. Absent ⇒ false.</summary>
    [JsonPropertyName("advisory")] public bool Advisory { get; init; }
}

/// <summary>One meta-dimension's measured result: its 0–10 score (null ⇒ not measured) and the lens it feeds directly.
/// Advisory and null-scored meta-dimensions are excluded from the number.</summary>
public readonly record struct MetaDimensionScore(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("lens")] string Lens,
    [property: JsonPropertyName("score")] double? ScoreZeroToTen)
{
    /// <summary>True for an advisory meta-dimension — shown band-only, excluded from the number. Absent ⇒ false.</summary>
    [JsonPropertyName("advisory")] public bool Advisory { get; init; }
}

/// <summary>One lens's pre-computed contribution for the thin-sidecar fallback: its 0–100 score and its OWA weight (the
/// worst-first share of the headline this lens carries). Present weights sum to 1.</summary>
public readonly record struct LensInput(
    [property: JsonPropertyName("lens")] string Lens,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("owaWeight")] double OwaWeight);
