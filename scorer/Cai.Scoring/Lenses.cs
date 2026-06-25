namespace Cai.Scoring;

/// <summary>One of the CAI lenses. Five are CORE (measured on every run); five are MODEL-AWARE (present only when the
/// architecture calls for them — a web frontend lights up accessibility, an event-sourced design lights up
/// eventSourcing, and so on). The key is the canonical wire name carried in an evidence bundle.</summary>
public sealed record Lens(string Key, string DisplayName, bool Core);

/// <summary>The canonical CAI lens catalog — the ten lenses the standard measures through. This is the open list; a
/// rubric version weights them, an evidence bundle reports the ones a run actually measured.</summary>
public static class LensCatalog
{
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

    public static bool IsKnown(string key) => All.Any(l => string.Equals(l.Key, key, StringComparison.Ordinal));

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
    public static LensGroup GroupOf(string key) => key switch
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
public enum LensGroup { Foundational, Operational, Safety, Default }
