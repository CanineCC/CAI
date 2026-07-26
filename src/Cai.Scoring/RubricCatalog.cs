using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cai.Scoring;

/// <summary>One dimension in a rubric catalog — a definition, not a score. Mirrors the Watchdog engine's
/// rubric-catalog.json contract so a catalog produced by the engine round-trips through the standard unchanged.</summary>
public sealed record CatalogDimension
{
    /// <summary>The dimension's stable id (e.g. "D#"), matching the id an evidence bundle reports.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    /// <summary>The lens key this dimension feeds.</summary>
    [JsonPropertyName("lens")] public string Lens { get; init; } = "";
    /// <summary>
    /// The scoring CATEGORY this dimension folds into ("code-quality", "explicit-debt", "git-mining",
    /// "security-compliance", …) — the intermediate roll-up between the dimension and its <see cref="Lens"/>.
    /// <para>This is part of the fold, not decoration: dimensions in the same category average together (confidence
    /// weighted) before the lens's worst-first OWA sees them, so RE-HOMING a dimension from one category to another
    /// moves the score for unchanged evidence. Publishing it here is what makes that a visible, versioned change to the
    /// standard rather than an invisible edit to a producer's internal map (ADR-0004).</para>
    /// <para>Null for a meta-family dimension (a meta feeds its lens directly, with no category), and null on every
    /// catalog published before the category joined the schema — for those, the scorer falls back to the category the
    /// evidence bundle declares, so previously-published rubric versions keep verifying unchanged.</para>
    /// </summary>
    [JsonPropertyName("category")] public string? Category { get; init; }
    /// <summary>"tool" (deterministic) or "llm" (advisory).</summary>
    [JsonPropertyName("evaluator")] public string Evaluator { get; init; } = "";
    /// <summary>A short description of what the dimension assesses.</summary>
    [JsonPropertyName("whatItMeasures")] public string WhatItMeasures { get; init; } = "";
    /// <summary>"dimension" or "meta".</summary>
    [JsonPropertyName("family")] public string Family { get; init; } = "dimension";
    /// <summary>The strongest enforcement rung this dimension can reach (Documented / Verified / Prevented).</summary>
    [JsonPropertyName("ceilingRung")] public string? CeilingRung { get; init; }
    /// <summary>True when the dimension is only measured under a deep scan (off in a standard run).</summary>
    [JsonPropertyName("deepScan")] public bool DeepScan { get; init; }
    /// <summary>How the dimension's 0–10 score is arrived at — "deduction" (start at 10, deduct for findings) or
    /// "credit". Descriptive metadata the engine emits; modelled so serving a catalog round-trips it instead of
    /// silently dropping it.</summary>
    [JsonPropertyName("scoringPolarity")] public string? ScoringPolarity { get; init; }
}

/// <summary>One lens in a rubric catalog.</summary>
public sealed record CatalogLens(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label")] string Label);

/// <summary>A whole rubric catalog at one version — every lens and dimension that version defines. This is the
/// versioned, archived definition of the standard: what is measured, by what kind of evaluator, in which lens.</summary>
public sealed record RubricCatalog
{
    /// <summary>The version this catalog defines (e.g. "rubric-2026.08.15") — the frozen identity of the standard.</summary>
    [JsonPropertyName("rubricVersion")] public string RubricVersion { get; init; } = "";
    /// <summary>Every lens this version defines.</summary>
    [JsonPropertyName("lenses")] public IReadOnlyList<CatalogLens> Lenses { get; init; } = [];
    /// <summary>Every dimension this version defines, across all lenses.</summary>
    [JsonPropertyName("dimensions")] public IReadOnlyList<CatalogDimension> Dimensions { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Parse a catalog from its JSON wire form (case-insensitive, comments tolerated). Throws
    /// <see cref="JsonException"/> on malformed input or a null result. Round-trips with <see cref="ToJson"/>.</summary>
    public static RubricCatalog Parse(string json) =>
        JsonSerializer.Deserialize<RubricCatalog>(json, Options)
        ?? throw new JsonException("Rubric catalog deserialized to null.");

    /// <summary>Serialize this catalog to its indented JSON wire form (null fields omitted) — the inverse of
    /// <see cref="Parse"/>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Dimensions in a lens.</summary>
    public IEnumerable<CatalogDimension> InLens(string lensKey) =>
        Dimensions.Where(d => string.Equals(d.Lens, lensKey, StringComparison.Ordinal));

    /// <summary>
    /// The FROZEN dimension→category map this rubric version publishes, keyed by dimension id. Only entries that
    /// declare a <see cref="CatalogDimension.Category"/> appear; a catalog published before the category joined the
    /// schema yields an empty map, and the scorer then falls back to the category each bundle declares.
    /// </summary>
    /// <exception cref="ArgumentException">A catalog declares a category this scorer does not implement. That fails
    /// CLOSED and loudly: the rubric describes a fold this build cannot perform, and guessing would produce a number
    /// under criteria nobody published.</exception>
    internal IReadOnlyDictionary<string, DimensionCategory> CategoryMap()
    {
        var map = new Dictionary<string, DimensionCategory>(StringComparer.Ordinal);
        foreach (var d in Dimensions.Where(d => !string.IsNullOrWhiteSpace(d.Category)))
        {
            try
            {
                map[d.Id] = Categories.Parse(d.Category!);
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException(
                    $"Rubric catalog '{RubricVersion}' assigns dimension '{d.Id}' to category '{d.Category}', which " +
                    "this scorer does not implement. Refusing to score: the published rubric describes a fold this " +
                    "build cannot perform.", e);
            }
        }

        return map;
    }
}
