namespace Cai.Web;

/// <summary>
/// Per-lens "why it matters / what it protects against" prose for /lenses. The sentences are the canonical,
/// founder-authored copy from the surveyor's document layer (Watchdog.Api/Documents/LensProse.cs, the EN "Covers"
/// line) — ported verbatim, NOT invented. "what it measures" is the dimensions under each lens (linked to the
/// catalog); this adds the protective rationale. A lens with no entry (e.g. performance) shows no claim rather than a
/// fabricated one.
/// </summary>
public static class CaiLensProse
{
    public static readonly IReadOnlyDictionary<string, string> Protects =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["codeHealth"] = "Including code health protects you against tangled, duplicated, hard-to-change code that quietly slows every future change and raises the cost of every fix.",
            ["architecture"] = "Including architecture protects you against a system whose parts are wired together so tightly that one change breaks several others and the structure cannot grow.",
            ["maturity"] = "Including maturity protects you against an unfinished product — missing tests, documentation and the disciplines that show the code is ready to be handed over.",
            ["productionReadiness"] = "Including production readiness protects you against software that cannot be safely run in the real world — no automated deployment, no backups, no way to see when it breaks.",
            ["securityCompliance"] = "Including security & compliance protects you against shipping known vulnerabilities and leaked credentials that an attacker can use against you.",
            ["domainModelling"] = "Including domain modelling protects you against code that does not reflect your business rules, where the language of the system and the language of your business drift apart.",
            ["eventDriven"] = "Including event-driven design protects you against a system whose moving parts coordinate in fragile, hidden ways that break under load or change.",
            ["eventSourcing"] = "Including event sourcing protects you against losing the trustworthy, replayable record of what happened — the audit trail your business may depend on.",
            ["accessibility"] = "Including accessibility protects you against a web product that people with disabilities — and, under the EU Accessibility Act, the law — cannot use: images with no text alternative, forms with no labels, controls no keyboard can reach.",
        };

    public static string? For(string lensKey) => Protects.TryGetValue(lensKey, out var s) ? s : null;
}
