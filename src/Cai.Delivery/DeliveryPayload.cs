using System.Text.Json.Serialization;
using Cai.Scoring;

namespace Cai.Delivery;

/// <summary>
/// The signed content of a CAI-delivery package — the point-in-time, self-contained record that cai.canine.dev attests.
/// Everything under <see cref="DeliveryPackage.Payload"/> is covered by the Ed25519 signature; editing any field breaks
/// verification. It carries the CAI verdict cai RECOMPUTED (never one it was handed), the evidence that verdict folds
/// from (so a consumer can reproduce it), and the provenance that says who measured what, when, under which ruleset.
///
/// The signature attests: "cai.canine.dev folded THIS evidence under rubric <c>rubricVersion</c> to CAI
/// <c>Verdict.Cai</c> for <c>Subject</c>, as submitted by <c>Producer</c>, at <c>IssuedAt</c>." It does NOT attest that
/// the evidence is a truthful measurement of the real repository — that is the producer's claim (closed-loop: the
/// producer is the trusted Watchdog surveyor; 3rd-party producer conformance is deferred).
/// </summary>
public sealed record DeliveryPayload
{
    /// <summary>The package format version — <c>MAJOR.MINOR</c> (see <see cref="DeliverySchema"/>). A verifier MUST
    /// reject a MAJOR it does not implement; a newer MINOR is forward-compatible (unknown fields are ignored). Covered by
    /// the signature, so the version cannot be downgraded on the wire. Distinct from <see cref="RubricVersion"/>: this
    /// versions the WIRE SHAPE, the rubric versions the MATH.</summary>
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; init; } = DeliverySchema.Current;

    /// <summary>cai's stable identifier for this delivery (assigned at push time), e.g. <c>cd_2f8a…</c>. Lets a grant,
    /// an access request or a report reference the exact artifact.</summary>
    [JsonPropertyName("deliveryId")] public string DeliveryId { get; init; } = "";

    /// <summary>When cai signed this delivery — RFC 3339 / ISO 8601 UTC (e.g. <c>2026-07-01T10:32:04Z</c>). Provenance;
    /// not part of the score.</summary>
    [JsonPropertyName("issuedAt")] public string IssuedAt { get; init; } = "";

    /// <summary>Who signed — cai — and with which key. The <see cref="DeliveryIssuer.KeyId"/> selects the public key a
    /// verifier resolves (offline, from a pinned key set, or from cai's published key set).</summary>
    [JsonPropertyName("issuer")] public DeliveryIssuer Issuer { get; init; } = new();

    /// <summary>Who MEASURED the code (produced the evidence). Stamped by cai from the authenticated producer identity at
    /// push time, so a sharer cannot forge a false producer. Closed-loop: the Watchdog surveyor.</summary>
    [JsonPropertyName("producer")] public DeliveryProducer Producer { get; init; } = new();

    /// <summary>What was measured — repository identity + the commit. Git-independent provenance carried IN the artifact,
    /// so the package stands alone once shared (no repo access required to read or verify it).</summary>
    [JsonPropertyName("subject")] public DeliverySubject Subject { get; init; } = new();

    /// <summary>The frozen rubric version the verdict was computed under (e.g. <c>rubric-2026.08.15</c>). Same evidence +
    /// same rubric ⇒ the same number — the reproducibility anchor (ADR-0002 / ADR-0004).</summary>
    [JsonPropertyName("rubricVersion")] public string RubricVersion { get; init; } = "";

    /// <summary>The quality bar the repo was judged against (shifts band cutlines only, never the score). Absent ⇒
    /// production baseline. Mirrors <see cref="EvidenceBundle.QualityBar"/>.</summary>
    [JsonPropertyName("qualityBar")] public string? QualityBar { get; init; }

    /// <summary>Measurement provenance metrics (scanned LoC, project counts, scan time) — context for a reader, not
    /// inputs to the fold beyond what <see cref="Evidence"/> already carries.</summary>
    [JsonPropertyName("measurement")] public DeliveryMeasurement Measurement { get; init; } = new();

    /// <summary>The CAI verdict cai recomputed from <see cref="Evidence"/> — the headline, band and per-lens/per-category
    /// roll-up. This is what a reader consumes; <see cref="Evidence"/> is what lets them reproduce it.</summary>
    [JsonPropertyName("verdict")] public DeliveryVerdict Verdict { get; init; } = new();

    /// <summary>The embedded evidence bundle — the open record the verdict folds from (ADR-0002). Its presence is what
    /// makes the package self-verifying: a consumer can re-run <see cref="CaiScorer.Score"/> and confirm the headline
    /// matches <see cref="DeliveryVerdict.Cai"/>, independent of the signature.</summary>
    [JsonPropertyName("evidence")] public EvidenceBundle Evidence { get; init; } = new();
}

/// <summary>Who signed a delivery (cai) and with which key.</summary>
public sealed record DeliveryIssuer
{
    /// <summary>The issuer identity — always cai for a valid delivery.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "cai.canine.dev";

    /// <summary>The signing key id — selects the public key a verifier uses. Retained forever once used, so old
    /// deliveries verify after key rotation (frozen-key policy, analogous to frozen rubrics).</summary>
    [JsonPropertyName("keyId")] public string KeyId { get; init; } = "";
}

/// <summary>Who measured the code (produced the evidence). Stamped by cai from the authenticated producer.</summary>
public sealed record DeliveryProducer
{
    /// <summary>The producer's account/identity, e.g. <c>watchdog.canine.dev</c>.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "";

    /// <summary>The scanner/analyzer that produced the evidence, e.g. <c>watchdog-surveyor</c>.</summary>
    [JsonPropertyName("scanner")] public string? Scanner { get; init; }

    /// <summary>The scanner version — part of provenance so a reader knows which analyzer build measured the code.</summary>
    [JsonPropertyName("scannerVersion")] public string? ScannerVersion { get; init; }
}

/// <summary>The repository the delivery is about — carried in the artifact so it is git-independent and portable.</summary>
public sealed record DeliverySubject
{
    /// <summary>The repository identity as the producer names it, e.g. <c>acme/checkout-api</c>.</summary>
    [JsonPropertyName("repository")] public string Repository { get; init; } = "";

    /// <summary>The commit the evidence was measured at (mirrors <see cref="EvidenceBundle.Commit"/>).</summary>
    [JsonPropertyName("commit")] public string? Commit { get; init; }

    /// <summary>Optional host/forge (e.g. <c>github.com</c>) — provenance only.</summary>
    [JsonPropertyName("host")] public string? Host { get; init; }
}

/// <summary>Measurement-scale provenance shown to a reader (never re-folded into the score).</summary>
public sealed record DeliveryMeasurement
{
    /// <summary>Total measured lines of code (the size the package attests, feeding the LoC price bucket downstream).</summary>
    [JsonPropertyName("measuredLoc")] public long? MeasuredLoc { get; init; }

    /// <summary>Hand-written production LoC (mirrors <see cref="EvidenceBundle.ProductionLoc"/>).</summary>
    [JsonPropertyName("productionLoc")] public int ProductionLoc { get; init; }

    /// <summary>Production (non-test, non-tooling) project count (mirrors <see cref="EvidenceBundle.AnalyzableProjects"/>).</summary>
    [JsonPropertyName("analyzableProjects")] public int AnalyzableProjects { get; init; }

    /// <summary>When the code was scanned — RFC 3339 UTC. May precede <see cref="DeliveryPayload.IssuedAt"/> (sign time).</summary>
    [JsonPropertyName("scannedAt")] public string? ScannedAt { get; init; }
}

/// <summary>The recomputed CAI verdict carried in a delivery — the headline the reader trusts, with the per-lens and
/// per-category roll-up. Built from a <see cref="CaiScore"/> by <see cref="DeliveryBuilder"/>; all values are rounded to
/// the delivery's fixed precision so the canonical form is stable across machines.</summary>
public sealed record DeliveryVerdict
{
    /// <summary>The 0–100 headline CAI.</summary>
    [JsonPropertyName("cai")] public double Cai { get; init; }

    /// <summary>The published band label (Exemplary / Strong / Adequate / Weak / Critical).</summary>
    [JsonPropertyName("band")] public string Band { get; init; } = "";

    /// <summary>The legacy equal-weight category aggregate (transparency; not the headline).</summary>
    [JsonPropertyName("aggregate")] public double Aggregate { get; init; }

    /// <summary>The unweighted category mean (null when no category layer was folded).</summary>
    [JsonPropertyName("categoryMean")] public double? CategoryMean { get; init; }

    /// <summary>Non-empty when the headline band was capped so it does not out-promise the weakest category.</summary>
    [JsonPropertyName("coherenceNote")] public string CoherenceNote { get; init; } = "";

    /// <summary>The per-lens roll-up, in canonical lens order.</summary>
    [JsonPropertyName("lenses")] public IReadOnlyList<DeliveryLens> Lenses { get; init; } = [];

    /// <summary>The per-category breakdown.</summary>
    [JsonPropertyName("categories")] public IReadOnlyList<DeliveryCategory> Categories { get; init; } = [];
}

/// <summary>One lens's result in a delivery verdict.</summary>
public sealed record DeliveryLens
{
    /// <summary>The lens key (e.g. <c>codeHealth</c>).</summary>
    [JsonPropertyName("lens")] public string Lens { get; init; } = "";

    /// <summary>The lens's rolled-up 0–100 score.</summary>
    [JsonPropertyName("score")] public double Score { get; init; }

    /// <summary>The lens's published band label.</summary>
    [JsonPropertyName("band")] public string Band { get; init; } = "";

    /// <summary>The lens's worst-first OWA share of the headline.</summary>
    [JsonPropertyName("weight")] public double Weight { get; init; }

    /// <summary>The lens's weight × score contribution to the headline.</summary>
    [JsonPropertyName("contribution")] public double Contribution { get; init; }

    /// <summary>True when a measured, non-advisory contributor below the critical gate capped this lens's band at Fair.</summary>
    [JsonPropertyName("criticalGated")] public bool CriticalGated { get; init; }

    /// <summary>The ids of the contributors that gated the band (so a reader can name WHY), e.g. <c>["C1"]</c>.</summary>
    [JsonPropertyName("criticalContributors")] public IReadOnlyList<string> CriticalContributors { get; init; } = [];

    /// <summary>How many items (categories + meta-dimensions) folded into this lens.</summary>
    [JsonPropertyName("itemCount")] public int ItemCount { get; init; }
}

/// <summary>One category's roll-up in a delivery verdict (score null when nothing deterministic measured it).</summary>
public sealed record DeliveryCategory
{
    /// <summary>The category key (e.g. <c>code-quality</c>).</summary>
    [JsonPropertyName("category")] public string Category { get; init; } = "";

    /// <summary>The lens this category feeds.</summary>
    [JsonPropertyName("lens")] public string Lens { get; init; } = "";

    /// <summary>The category's confidence-weighted 0–100 score, or null when nothing deterministic measured it.</summary>
    [JsonPropertyName("score")] public double? Score { get; init; }

    /// <summary>How many dimensions folded into this category.</summary>
    [JsonPropertyName("dimensionCount")] public int DimensionCount { get; init; }
}
