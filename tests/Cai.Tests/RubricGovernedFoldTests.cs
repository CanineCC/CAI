using Cai.Delivery;
using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// A score is only meaningful relative to the criteria it was computed under, so every path that FOLDS one must fold
/// under the rubric the artifact names — not under whichever build happens to be running.
///
/// <para>These tests exist because the opposite was true: `1d557c3` gave a catalog the power to pin the fold's
/// constants and the band cutlines, and four of the five call sites in the system kept calling the overload that
/// silently substitutes <see cref="ScoringParameters.Default"/> — including <see cref="DeliveryBuilder"/>, the
/// mint-time trust gate, and <see cref="DeliveryVerifier"/>, the consumer's reproducibility check. Nothing was
/// miscomputed while no catalog published a block; the defect was that the day one did, the issuer's own API and its
/// signed artifacts would disagree.</para>
/// </summary>
public sealed class RubricGovernedFoldTests
{
    private static EvidenceBundle Evidence() => new()
    {
        RubricVersion = "rubric-test",
        Commit = "3f9a1c2",
        QualityBar = "production",
        AnalyzableProjects = 3,
        ProductionLoc = 4000,
        Dimensions =
        [
            new DimensionScore("D1", "code-quality", 7.5, 0.95),
            new DimensionScore("D3", "code-quality", 8.2, 0.95),
            new DimensionScore("D5", "architecture", 7.1, 0.95),
            new DimensionScore("D9", "testing", 7.0, 0.85),
            new DimensionScore("D30", "security", 7.6, 0.90),
        ],
    };

    private static DeliveryBuildRequest Request() => new()
    {
        DeliveryId = "cd_test_fold",
        IssuedAt = "2026-09-16T10:00:00Z",
        Subject = new DeliverySubject { Repository = "acme/checkout-api", Commit = "3f9a1c2", Host = "github.com" },
        Producer = new DeliveryProducer { Name = "watchdog.canine.dev", Scanner = "watchdog-surveyor" },
    };

    /// <summary>A catalog carrying no block — the shape of all 38 already-published versions.</summary>
    private static ResolvedRubric BlockLess() =>
        ResolvedRubric.FromPublished(new RubricCatalog { RubricVersion = "rubric-test" }.ToJson());

    /// <summary>A catalog whose block genuinely moves the fold: a much sharper across-lens decay makes the weakest
    /// lens dominate the headline, which no re-labelling can imitate.</summary>
    private static ResolvedRubric Governing() => ResolvedRubric.FromPublished(new RubricCatalog
    {
        RubricVersion = "rubric-test",
        Scoring = ScoringParameters.Default with { AcrossLensQ = 0.05, WithinLensQ = 0.05 },
    }.ToJson());

    [Fact]
    public void The_mint_gate_folds_under_the_catalogs_block_and_not_the_scorers_defaults()
    {
        // DeliveryBuilder is the trust gate — "cai signs only a number cai itself folded". If it ignores the rubric's
        // own parameters it signs a number the standard does not endorse.
        var underDefaults = DeliveryBuilder.Build(Evidence(), BlockLess(), Request());
        var underBlock = DeliveryBuilder.Build(Evidence(), Governing(), Request());

        Assert.NotEqual(underDefaults.Verdict.Cai, underBlock.Verdict.Cai);
    }

    [Fact]
    public void A_block_less_catalog_still_folds_to_the_values_the_scorer_has_always_used()
    {
        // The ADR-0004 compatibility mechanism, at the delivery layer: the 38 published block-less catalogs must keep
        // reproducing their original numbers. This is why `?? Default` survives the cleanup.
        var viaDelivery = DeliveryBuilder.Build(Evidence(), BlockLess(), Request()).Verdict.Cai;
        var viaScorer = CaiScorer.Score(Evidence(), new RubricCatalog { RubricVersion = "rubric-test" }).Headline;

        // The verdict carries the delivery's fixed 2-dp wire precision (ToVerdict), so the comparison is against the
        // rounded fold, not the raw one.
        Assert.Equal(Math.Round(viaScorer, 2), viaDelivery, 10);
    }

    [Fact]
    public void The_payload_witnesses_the_digest_of_the_catalog_it_was_actually_folded_under()
    {
        // Deriving the digest from the resolved rubric removes the way the old request-supplied hash could disagree
        // with the document cai actually used.
        var rubric = Governing();
        var payload = DeliveryBuilder.Build(Evidence(), rubric, Request());

        Assert.Equal(rubric.ContentHash, payload.RubricContentHash);
    }

    [Fact]
    public void Verification_reproduces_the_headline_under_the_supplied_rubric()
    {
        var rubric = Governing();
        var pair = DeliveryKeyPair.Generate("cai-ed25519-test");
        using var signer = new DeliverySigner(pair);
        var package = signer.SignPackage(DeliveryBuilder.Build(Evidence(), rubric, Request()));
        var keys = new DeliveryPublicKeySet { Keys = [pair.ToPublicKey()] };

        var r = DeliveryVerifier.Verify(package, keys, rubric);

        Assert.True(r.AuthenticAndReproducing);
        Assert.True(r.Reproduced);
    }

    [Fact]
    public void Verification_refuses_a_rubric_that_is_not_the_one_the_payload_names()
    {
        // Handing the verifier a different catalog than the artifact witnesses must fail loudly. Otherwise a consumer
        // "verifies" a number against rules it was never computed under.
        var pair = DeliveryKeyPair.Generate("cai-ed25519-test");
        using var signer = new DeliverySigner(pair);
        var package = signer.SignPackage(DeliveryBuilder.Build(Evidence(), Governing(), Request()));
        var keys = new DeliveryPublicKeySet { Keys = [pair.ToPublicKey()] };

        var r = DeliveryVerifier.Verify(package, keys, BlockLess());

        Assert.False(r.AuthenticAndReproducing);
        Assert.Contains("rubric", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_signature_only_check_does_not_report_itself_as_reproducing()
    {
        // `AuthenticAndReproducing => SignatureValid && Reproduced != false` read an ABSENT measurement as a passing
        // one: a check that folded nothing still answered true. A verification that did not reproduce must not claim to.
        var pair = DeliveryKeyPair.Generate("cai-ed25519-test");
        using var signer = new DeliverySigner(pair);
        var package = signer.SignPackage(DeliveryBuilder.Build(Evidence(), Governing(), Request()));
        var keys = new DeliveryPublicKeySet { Keys = [pair.ToPublicKey()] };

        var r = DeliveryVerifier.VerifySignature(package, keys);

        Assert.True(r.SignatureValid);
        Assert.False(r.AuthenticAndReproducing);
    }
}
