using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cai.Web;

/// <summary>
/// The CAI standard's ubiquitous vocabulary rendered as a schema.org <c>DefinedTermSet</c> (JSON-LD) — the canonical,
/// CITABLE definition of the CAI and its companion terms, so search engines and LLMs can cite "CAI" as a noun with a
/// fixed meaning (the "referenceable" pillar). Terms are grounded in /spec, /lenses, the bands, and the reference
/// scorer — not invented. Served at /glossary.jsonld and embedded inline on the home page head.
/// </summary>
public static class CaiGlossary
{
    public sealed record Term(string Key, string En, string? Da, string Gloss);

    // Grounded in the live site: /spec (the fold + evidence bundle + rubric versioning), /lenses (core vs model-aware),
    // the bands (scorer/Cai.Scoring/Band.cs), and the README (method open / judgment sold).
    public static readonly IReadOnlyList<Term> Terms =
    [
        new("cai", "Codebase Assurance Index (CAI)", "energimærke for software",
            "An open, reproducible 0–100 score for the health of a codebase, computed deterministically from evidence under a frozen, versioned rubric. Same evidence and rubric version always yield the same number — a measurement, not an opinion."),
        new("lens", "Lens", null,
            "A grouping of related dimensions (for example Code Health, Architecture, Security & Compliance). Five lenses are core and always scored; the rest are model-aware and light up only when the architecture calls for them."),
        new("dimension", "Dimension", null,
            "A single measured aspect of a codebase, scored 0–10 from evidence. Dimensions roll up into their lens."),
        new("band", "Band", null,
            "The qualitative tier a CAI falls into: Exemplary (90–100), Strong (70–89), Adequate (50–69), Weak (25–49), Critical (0–24). A fixed worst-to-best valence ladder."),
        new("evidence-bundle", "Evidence bundle", null,
            "The open input record a CAI is computed from: the measured dimensions and the rank weights, not the source code. Anyone can fold a bundle through the open scorer to reproduce the score."),
        new("rubric-version", "Rubric version", null,
            "The frozen, versioned definition of the criteria. Any change that can move a score for unchanged evidence mints a new version; old versions are retained, so a score is always reproducible to the exact criteria it was computed under."),
        new("owa-fold", "Ordered weighted average (the fold)", null,
            "The rank-weighted, worst-first roll-up that folds dimensions into a lens and lenses into the headline — the i-th worst input weighs q^(i-1), so the weakest areas drag hardest. Never an equal-weight mean."),
        new("firewall", "The firewall", null,
            "The boundary between the open, deterministic measurement (score, findings, algorithm) and the surveyor's paid advisory judgment (deductions and non-score enhancements). It is also the free/paid line."),
        new("survey", "Survey", null,
            "An independent, signed CAI assessment of a codebase issued by a surveyor (watchdog.canine.dev): the CAI plus the deductions and what to do about them. The standard is free; the survey is the service."),
        new("salgsopstilling", "Salgsopstilling (listing)", "salgsopstilling",
            "A surveyor-issued sell-sheet that packages a codebase's independently measured strengths for a buyer — honest by construction, because its numbers are the survey's numbers."),
        new("reproducibility", "Reproducibility", null,
            "The property that the same evidence under the same rubric version always folds to the same CAI, so any published number can be independently recomputed and verified — or falsified."),
    ];

    public static string Build()
    {
        var terms = new JsonArray();
        foreach (var t in Terms)
        {
            var term = new JsonObject
            {
                ["@type"] = "DefinedTerm",
                ["name"] = t.En,
                ["termCode"] = t.Key,
                ["description"] = t.Gloss,
            };
            if (t.Da is not null)
            {
                term["alternateName"] = t.Da; // the paired Danish coinage, where one exists (translate the role, not the word)
            }

            terms.Add(term);
        }

        var set = new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "DefinedTermSet",
            ["name"] = "Codebase Assurance Index (CAI) glossary",
            ["description"] = "The canonical vocabulary of the CAI standard — the open reference for the CAI and its companion terms.",
            ["url"] = "https://cai.canine.dev/glossary.jsonld",
            ["hasDefinedTerm"] = terms,
        };

        return set.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
