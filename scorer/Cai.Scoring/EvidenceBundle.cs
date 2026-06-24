using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cai.Scoring;

/// <summary>
/// A CAI evidence bundle — the open, portable record a score is computed from. It is a documented SUBSET of the
/// Watchdog analyzer's run sidecar: enough to recompute and verify the headline, nothing engine-specific. Producing a
/// bundle (measuring the code) is the analyzer's job; SCORING a bundle (this library) is open and reproducible.
/// </summary>
public sealed record EvidenceBundle
{
    /// <summary>The frozen rubric version this evidence was produced under (e.g. "rubric-2026.06.0"). A score is only
    /// meaningful with its rubric version — same evidence + same rubric ⇒ the same number.</summary>
    [JsonPropertyName("rubricVersion")] public string RubricVersion { get; init; } = "";

    /// <summary>The commit the evidence was measured at (provenance; not part of the math).</summary>
    [JsonPropertyName("commit")] public string? Commit { get; init; }

    /// <summary>The PUBLISHED headline (0–100) this evidence claims — what <see cref="CaiScorer.Verify"/> reproduces.
    /// Optional: a bundle may carry only the lenses and let the scorer compute the headline fresh.</summary>
    [JsonPropertyName("headlineScore")] public double? HeadlineScore { get; init; }

    /// <summary>The measured dimensions (0–10 each) — the PRIMARY evidence. The scorer folds these into lens scores and
    /// then the headline. A dimension measured at confidence 0 must be ABSENT, never a raw 0.0.</summary>
    [JsonPropertyName("dimensions")] public IReadOnlyList<DimensionScore> Dimensions { get; init; } = [];

    /// <summary>Pre-computed lens scores — the FALLBACK input when a bundle carries no dimensions (e.g. a thin sidecar).
    /// When present with OWA weights, they reproduce a published headline exactly; otherwise the across-lens fold derives
    /// the weights. Ignored when <see cref="Dimensions"/> is non-empty (the dimensions are folded instead).</summary>
    [JsonPropertyName("lenses")] public IReadOnlyList<LensScore> Lenses { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    public static EvidenceBundle Parse(string json) =>
        JsonSerializer.Deserialize<EvidenceBundle>(json, Options)
        ?? throw new JsonException("Evidence bundle deserialized to null.");

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}

/// <summary>One lens's contribution: its 0–100 score and its OWA weight — the worst-first ordered-weighted-average
/// share of the headline this lens carries. The weights of the present lenses sum to 1.</summary>
public sealed record LensScore(
    [property: JsonPropertyName("lens")] string Lens,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("owaWeight")] double OwaWeight);

/// <summary>One dimension's measured result (0–10), with the confidence it was measured at and whether it's an
/// LLM-advisory dimension. A dimension measured at confidence 0 is NOT measured — it must be ABSENT from the bundle,
/// never a raw 0.0 (that would read as "failed" instead of "not assessed"). An advisory dimension is excluded from the
/// deterministic number.</summary>
public sealed record DimensionScore(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("lens")] string Lens,
    [property: JsonPropertyName("score")] double ScoreZeroToTen,
    [property: JsonPropertyName("confidence")] double Confidence)
{
    /// <summary>True for an LLM-scored dimension — advisory, excluded from the deterministic fold. Absent ⇒ false.</summary>
    [JsonPropertyName("advisory")] public bool Advisory { get; init; }
}
