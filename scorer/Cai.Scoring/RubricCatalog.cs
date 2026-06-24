using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cai.Scoring;

/// <summary>One dimension in a rubric catalog — a definition, not a score. Mirrors the Watchdog engine's
/// rubric-catalog.json contract so a catalog produced by the engine round-trips through the standard unchanged.</summary>
public sealed record CatalogDimension
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("lens")] public string Lens { get; init; } = "";
    /// <summary>"tool" (deterministic) or "llm" (advisory).</summary>
    [JsonPropertyName("evaluator")] public string Evaluator { get; init; } = "";
    [JsonPropertyName("whatItMeasures")] public string WhatItMeasures { get; init; } = "";
    /// <summary>"dimension" or "meta".</summary>
    [JsonPropertyName("family")] public string Family { get; init; } = "dimension";
    /// <summary>The strongest enforcement rung this dimension can reach (Documented / Verified / Prevented).</summary>
    [JsonPropertyName("ceilingRung")] public string? CeilingRung { get; init; }
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
    [JsonPropertyName("rubricVersion")] public string RubricVersion { get; init; } = "";
    [JsonPropertyName("lenses")] public IReadOnlyList<CatalogLens> Lenses { get; init; } = [];
    [JsonPropertyName("dimensions")] public IReadOnlyList<CatalogDimension> Dimensions { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static RubricCatalog Parse(string json) =>
        JsonSerializer.Deserialize<RubricCatalog>(json, Options)
        ?? throw new JsonException("Rubric catalog deserialized to null.");

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Dimensions in a lens.</summary>
    public IEnumerable<CatalogDimension> InLens(string lensKey) =>
        Dimensions.Where(d => string.Equals(d.Lens, lensKey, StringComparison.Ordinal));
}
