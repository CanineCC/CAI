using Cai.Scoring;

namespace Cai.Delivery;

/// <summary>The provenance a producer supplies when pushing evidence — everything cai stamps into the payload EXCEPT the
/// verdict (which cai computes) and the issuer key (which the signer sets). In production cai fills <see cref="DeliveryId"/>
/// and <see cref="IssuedAt"/> itself and derives <see cref="Producer"/> from the authenticated caller.</summary>
public sealed record DeliveryBuildRequest
{
    /// <summary>cai's stable id for the delivery.</summary>
    public required string DeliveryId { get; init; }

    /// <summary>Sign time — RFC 3339 UTC.</summary>
    public required string IssuedAt { get; init; }

    /// <summary>The repository the delivery is about.</summary>
    public required DeliverySubject Subject { get; init; }

    /// <summary>Who measured the code (the authenticated producer).</summary>
    public required DeliveryProducer Producer { get; init; }

    /// <summary>Measurement-scale provenance; derived from the evidence when omitted.</summary>
    public DeliveryMeasurement? Measurement { get; init; }

    /// <summary>The issuer name stamped into the payload (defaults to cai).</summary>
    public string IssuerName { get; init; } = "cai.canine.dev";
}

/// <summary>
/// Builds the payload of a CAI-delivery — and, crucially, RECOMPUTES the verdict from the submitted evidence rather than
/// trusting any headline the producer supplied. This is the trust gate: cai signs only a number cai itself folded, so
/// the signature means "cai computed this," which is exactly why a buyer can rely on it. Runs on the registry push path,
/// just before signing.
/// </summary>
public static class DeliveryBuilder
{
    /// <summary>Fold the evidence and assemble the (unsigned) payload. The verdict is cai's own computation; the rubric
    /// version, quality bar and surface metrics are echoed from the evidence so the artifact is self-contained.</summary>
    public static DeliveryPayload Build(EvidenceBundle evidence, DeliveryBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(request);

        var score = CaiScorer.Score(evidence);

        var measurement = request.Measurement ?? new DeliveryMeasurement
        {
            ProductionLoc = evidence.ProductionLoc,
            AnalyzableProjects = evidence.AnalyzableProjects,
        };

        return new DeliveryPayload
        {
            SchemaVersion = DeliverySchema.Current,
            DeliveryId = request.DeliveryId,
            IssuedAt = request.IssuedAt,
            Issuer = new DeliveryIssuer { Name = request.IssuerName },
            Producer = request.Producer,
            Subject = request.Subject with { Commit = request.Subject.Commit ?? evidence.Commit },
            RubricVersion = evidence.RubricVersion,
            QualityBar = evidence.QualityBar,
            Measurement = measurement,
            Verdict = ToVerdict(score),
            Evidence = evidence,
        };
    }

    /// <summary>Project a computed <see cref="CaiScore"/> into the wire verdict, rounding every number to the delivery's
    /// fixed precision (the same roundings the /api/score endpoint publishes) so the canonical form is stable.</summary>
    private static DeliveryVerdict ToVerdict(CaiScore score) => new()
    {
        Cai = Math.Round(score.Headline, 2),
        Band = score.Band.Label(),
        Aggregate = Math.Round(score.Aggregate, 2),
        CategoryMean = score.CategoryMean is { } m ? Math.Round(m, 2) : null,
        CoherenceNote = score.CoherenceNote,
        Lenses = score.Lenses.Select(l => new DeliveryLens
        {
            Lens = l.Lens,
            Score = Math.Round(l.Score, 2),
            Band = l.Band.Label(),
            Weight = Math.Round(l.Weight, 4),
            Contribution = Math.Round(l.Contribution, 2),
            CriticalGated = l.CriticalGated,
            CriticalContributors = l.CriticalContributors,
            ItemCount = l.ItemCount,
        }).ToList(),
        Categories = score.Categories.Select(c => new DeliveryCategory
        {
            Category = c.Category,
            Lens = c.Lens,
            Score = c.Score is { } cs ? Math.Round(cs, 2) : null,
            DimensionCount = c.DimensionCount,
        }).ToList(),
    };
}
