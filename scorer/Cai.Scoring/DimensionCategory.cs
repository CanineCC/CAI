namespace Cai.Scoring;

/// <summary>
/// The nine scoring categories a deterministic (<c>D#</c>) dimension belongs to — the intermediate roll-up between a
/// dimension and its lens. A category's confidence-weighted mean (0–100) is ONE item in its lens's worst-first fold,
/// so dimensions in the same category average together before the lens sees them (a lens with one weak dimension among
/// five is not dragged as if that dimension were a whole lens). The category a dimension sits in decides which lens it
/// feeds — there is no per-dimension weighting (a dimension's influence is its category, a category's is its lens).
/// </summary>
public enum DimensionCategory
{
    CodeQuality,
    Architecture,
    Testing,
    Dependencies,
    Security,
    Docs,
    GitMining,
    ExplicitDebt,
    SecurityCompliance,
}

/// <summary>Maps a scoring category to its lens and parses the wire name a bundle carries.</summary>
public static class Categories
{
    /// <summary>The lens a category feeds (the rubric's category→lens map). Architecture is its own lens so an
    /// over-engineered-but-low-coupling codebase shows a weak Architecture slice while Code Health stays high; docs +
    /// git-mining are Maturity; tests + dependencies + presence-only security are Production Readiness; the deep-scan
    /// Security &amp; Compliance category is its own lens.</summary>
    public static string LensOf(DimensionCategory category) => category switch
    {
        DimensionCategory.CodeQuality or DimensionCategory.ExplicitDebt => "codeHealth",
        DimensionCategory.Architecture => "architecture",
        DimensionCategory.Docs or DimensionCategory.GitMining => "maturity",
        DimensionCategory.Testing or DimensionCategory.Dependencies or DimensionCategory.Security => "productionReadiness",
        DimensionCategory.SecurityCompliance => "securityCompliance",
        _ => "codeHealth",
    };

    /// <summary>Parse the category a bundle reports — tolerant of both the PascalCase enum name ("CodeQuality") and the
    /// kebab id ("code-quality", "security-compliance"). Throws on an unknown category so a malformed bundle fails loud
    /// rather than silently mis-folding.</summary>
    public static DimensionCategory Parse(string wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        var normalized = wire.Replace("-", "", StringComparison.Ordinal).Trim();
        return Enum.TryParse<DimensionCategory>(normalized, ignoreCase: true, out var category)
            ? category
            : throw new ArgumentException($"Unknown dimension category '{wire}'.", nameof(wire));
    }
}
