namespace Cai.Scoring;

/// <summary>One of the CAI lenses. Five are CORE (measured on every run); five are MODEL-AWARE (present only when the
/// architecture calls for them — a web frontend lights up accessibility, an event-sourced design lights up
/// eventSourcing, and so on). The key is the canonical wire name carried in an evidence bundle.</summary>
public sealed record Lens(string Key, string DisplayName, bool Core);

/// <summary>The canonical CAI lens catalog — the ten lenses the standard measures through. This is the open list; a
/// rubric version weights them, an evidence bundle reports the ones a run actually measured.</summary>
public static class LensCatalog
{
    /// <summary>The ten lenses in canonical order: the five CORE lenses first, then the five MODEL-AWARE ones. This
    /// order is the stable display + sort order (see <see cref="Order"/>).</summary>
    public static readonly IReadOnlyList<Lens> All =
    [
        new("codeHealth", "Code Health", Core: true),
        new("architecture", "Architecture", Core: true),
        new("maturity", "Maturity", Core: true),
        new("productionReadiness", "Production-Readiness", Core: true),
        new("securityCompliance", "Security & Compliance", Core: true),
        new("domainModelling", "Domain Modelling", Core: false),
        new("eventDriven", "Event-Driven", Core: false),
        new("eventSourcing", "Event Sourcing", Core: false),
        new("accessibility", "Accessibility", Core: false),
        new("performance", "Performance", Core: false),
    ];

    /// <summary>Whether <paramref name="key"/> is one of the catalog's lens keys (exact, ordinal match).</summary>
    public static bool IsKnown(string key) => All.Any(l => string.Equals(l.Key, key, StringComparison.Ordinal));

    /// <summary>The lens for a wire key, or null when the key isn't in the catalog (ordinal match).</summary>
    public static Lens? Find(string key) => All.FirstOrDefault(l => string.Equals(l.Key, key, StringComparison.Ordinal));

    /// <summary>The catalog index of a lens (its stable display order), or <see cref="int.MaxValue"/> for an unknown
    /// key so unknown lenses sort last rather than throwing.</summary>
    public static int Order(string key)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Key, key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>The criticality GROUP a lens belongs to — how fully its band thresholds follow the quality bar
    /// (<see cref="QualityBarBands"/>). Foundational code/architecture stay near-strict even for a prototype;
    /// operational maturity/readiness follow the bar fully; safety (security) stays near-strict everywhere; everything
    /// else (the conditional lenses) takes a moderate factor.</summary>
    internal static LensGroup GroupOf(string key) => key switch
    {
        "codeHealth" or "architecture" => LensGroup.Foundational,
        "maturity" or "productionReadiness" => LensGroup.Operational,
        "securityCompliance" => LensGroup.Safety,
        _ => LensGroup.Default,
    };
}

/// <summary>How strongly a lens's red/amber/green band thresholds follow the quality bar — the bar moves the band
/// lines, never the score (a prototype need not be documented, but its code should still basically work, and even a
/// prototype must not leak secrets).</summary>
internal enum LensGroup
{
    /// <summary>Code Health + Architecture — stay near-strict even for a prototype (the basics must hold regardless).</summary>
    Foundational,

    /// <summary>Maturity + Production-Readiness — follow the quality bar fully (a prototype is held to a leaner standard).</summary>
    Operational,

    /// <summary>Security &amp; Compliance — stays near-strict everywhere (even a prototype must not leak secrets).</summary>
    Safety,

    /// <summary>Everything else (the conditional, model-aware lenses) — takes a moderate follow-the-bar factor.</summary>
    Default,
}
