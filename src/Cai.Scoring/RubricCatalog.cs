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
}
