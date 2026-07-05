using System.Text.Json;
using System.Text.Json.Nodes;
using Cai.Delivery;
using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The CAI-delivery contract: cai signs what cai recomputed, the artifact is tamper-evident, and a consumer can verify
/// it offline — signature (authenticity) and reproduce (honest math) as two independent checks. These are the trust
/// invariants the whole seller→buyer share flow rests on.
/// </summary>
public sealed class DeliveryTests
{
    private static EvidenceBundle SampleEvidence() => new()
    {
        RubricVersion = "rubric-2026.08.15",
        Commit = "3f9a1c2",
        QualityBar = "production",
        AnalyzableProjects = 3,
        ProductionLoc = 1500,
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
        DeliveryId = "cd_test_001",
        IssuedAt = "2026-07-01T10:32:04Z",
        Subject = new DeliverySubject { Repository = "acme/checkout-api", Commit = "3f9a1c2", Host = "github.com" },
        Producer = new DeliveryProducer { Name = "watchdog.canine.dev", Scanner = "watchdog-surveyor" },
    };

    private static (DeliveryPackage Package, DeliveryPublicKeySet Keys) SignedSample()
    {
        var pair = DeliveryKeyPair.Generate("cai-ed25519-test");
        var payload = DeliveryBuilder.Build(SampleEvidence(), Request());
        using var signer = new DeliverySigner(pair);
        return (signer.SignPackage(payload), new DeliveryPublicKeySet { Keys = [pair.ToPublicKey()] });
    }

    [Fact]
    public void Signed_package_verifies_and_reproduces()
    {
        var (package, keys) = SignedSample();
        var r = DeliveryVerifier.Verify(package, keys);

        Assert.True(r.SignatureValid);
        Assert.True(r.Reproduced);
        Assert.True(r.Trustworthy);
        Assert.Null(r.Reason);
    }

    [Fact]
    public void Signer_recomputes_the_verdict_rather_than_trusting_a_handed_number()
    {
        // Even if evidence carries a bogus headlineScore, the signed verdict is cai's own fold.
        var evidence = SampleEvidence() with { HeadlineScore = 5.0 };
        var payload = DeliveryBuilder.Build(evidence, Request());
        var expected = CaiScorer.Score(evidence).Headline;

        Assert.Equal(Math.Round(expected, 2), payload.Verdict.Cai, 2);
        Assert.NotEqual(5.0, payload.Verdict.Cai);
    }

    [Fact]
    public void Issuer_and_signature_key_ids_agree()
    {
        var (package, _) = SignedSample();
        Assert.Equal(package.Payload.Issuer.KeyId, package.Signature.KeyId);
        Assert.Equal("Ed25519", package.Signature.Alg);
        Assert.Equal(CanonicalJson.Method, package.Signature.Canon);
    }

    [Fact]
    public void Tampering_any_payload_field_breaks_the_signature()
    {
        var (package, keys) = SignedSample();
        var tampered = package with { Payload = package.Payload with { Verdict = package.Payload.Verdict with { Cai = 99.0 } } };

        var r = DeliveryVerifier.Verify(tampered, keys);
        Assert.False(r.SignatureValid);
        Assert.False(r.Trustworthy);
    }

    [Fact]
    public void Reformatting_the_file_does_not_break_the_signature()
    {
        // The signature is over the CANONICAL form, so a round-trip through the pretty-printed wire form still verifies.
        var (package, keys) = SignedSample();
        var reparsed = DeliveryPackage.Parse(package.ToJson());

        Assert.True(DeliveryVerifier.Verify(reparsed, keys).SignatureValid);
    }

    [Fact]
    public void A_wrong_key_does_not_verify()
    {
        var (package, _) = SignedSample();
        var otherKeys = new DeliveryPublicKeySet
        {
            Keys = [DeliveryKeyPair.Generate(package.Signature.KeyId).ToPublicKey()], // same id, different key
        };

        Assert.False(DeliveryVerifier.Verify(package, otherKeys).SignatureValid);
    }

    [Fact]
    public void An_unknown_key_id_is_reported()
    {
        var (package, _) = SignedSample();
        var r = DeliveryVerifier.Verify(package, new DeliveryPublicKeySet { Keys = [] });

        Assert.False(r.SignatureValid);
        Assert.Contains("no public key", r.Reason);
    }

    [Fact]
    public void A_retired_key_still_verifies_old_deliveries()
    {
        var pair = DeliveryKeyPair.Generate("cai-ed25519-old");
        var payload = DeliveryBuilder.Build(SampleEvidence(), Request());
        using var signer = new DeliverySigner(pair);
        var package = signer.SignPackage(payload);

        var retired = pair.ToPublicKey() with { Status = "retired" };
        Assert.True(DeliveryVerifier.Verify(package, new DeliveryPublicKeySet { Keys = [retired] }).SignatureValid);
    }

    [Fact]
    public void An_unsupported_major_version_is_rejected()
    {
        var (package, keys) = SignedSample();
        var future = package with { Payload = package.Payload with { SchemaVersion = "2.0" } };

        var r = DeliveryVerifier.Verify(future, keys);
        Assert.False(r.SignatureValid);
        Assert.Contains("MAJOR", r.Reason);
    }

    [Fact]
    public void Signature_valid_but_verdict_that_does_not_reproduce_is_not_trustworthy()
    {
        // Sign a payload whose verdict headline was hand-altered away from what the evidence folds to. The signature is
        // valid over that (dishonest) payload, but the independent reproduce check catches the mismatch.
        var pair = DeliveryKeyPair.Generate("cai-ed25519-test");
        var payload = DeliveryBuilder.Build(SampleEvidence(), Request());
        var dishonest = payload with { Verdict = payload.Verdict with { Cai = payload.Verdict.Cai + 20.0 } };
        using var signer = new DeliverySigner(pair);
        var package = signer.SignPackage(dishonest);

        var r = DeliveryVerifier.Verify(package, new DeliveryPublicKeySet { Keys = [pair.ToPublicKey()] });
        Assert.True(r.SignatureValid);
        Assert.False(r.Reproduced);
        Assert.False(r.Trustworthy);
    }

    [Fact]
    public void Canonicalization_is_independent_of_key_order_and_whitespace()
    {
        const string a = "{ \"b\": 2, \"a\": 1, \"nested\": { \"y\": true, \"x\": [3, 2] } }";
        const string b = "{\"a\":1,\"nested\":{\"x\":[3,2],\"y\":true},\"b\":2}";

        Assert.Equal(CanonicalJson.Canonicalize(a), CanonicalJson.Canonicalize(b));
    }

    [Fact]
    public void The_shipped_sample_package_verifies_against_the_shipped_keys()
    {
        // Guards that examples/cai-delivery.sample.json stays valid against examples/cai-delivery.keys.json.
        var root = RepoRoot();
        var package = DeliveryPackage.Parse(File.ReadAllText(Path.Combine(root, "examples", "cai-delivery.sample.json")));
        var keys = DeliveryPublicKeySet.Parse(File.ReadAllText(Path.Combine(root, "examples", "cai-delivery.keys.json")));

        var r = DeliveryVerifier.Verify(package, keys);
        Assert.True(r.SignatureValid, r.Reason);
        Assert.True(r.Reproduced, r.Reason);
    }

    // ── descriptive, non-scored metrics: rebuildCost + busFactor ─────────────────────────────────────────────────
    // These are producer-worded metrics a downstream consumer (the Assay buyer report) echoes verbatim. They ride
    // INSIDE the signed evidence but must NEVER enter the CAI fold or move the headline.

    [Fact]
    public void EvidenceBundle_round_trips_the_string_form_of_rebuildCost_and_busFactor()
    {
        var bundle = SampleEvidence() with
        {
            RebuildCost = JsonNode.Parse("\"€118k–€204k\""),
            BusFactor = "2 of 11 devs",
        };

        var reparsed = EvidenceBundle.Parse(bundle.ToJson());

        Assert.NotNull(reparsed.RebuildCost);
        Assert.Equal(JsonValueKind.String, reparsed.RebuildCost!.GetValueKind());
        Assert.Equal("€118k–€204k", reparsed.RebuildCost!.GetValue<string>());
        Assert.Equal("2 of 11 devs", reparsed.BusFactor);
    }

    [Fact]
    public void EvidenceBundle_round_trips_the_object_form_of_rebuildCost_preserving_number_tokens()
    {
        var bundle = SampleEvidence() with
        {
            RebuildCost = JsonNode.Parse("""{ "low": 118000, "high": 204000, "currency": "EUR" }"""),
        };

        var reparsed = EvidenceBundle.Parse(bundle.ToJson());

        Assert.NotNull(reparsed.RebuildCost);
        Assert.Equal(JsonValueKind.Object, reparsed.RebuildCost!.GetValueKind());
        var obj = reparsed.RebuildCost!.AsObject();
        Assert.Equal(118000, obj["low"]!.GetValue<int>());
        Assert.Equal(204000, obj["high"]!.GetValue<int>());
        Assert.Equal("EUR", obj["currency"]!.GetValue<string>());
    }

    [Fact]
    public void DeliveryBuilder_carries_the_descriptive_metrics_into_payload_evidence()
    {
        var evidence = SampleEvidence() with
        {
            RebuildCost = JsonNode.Parse("""{ "low": 118000, "high": 204000, "currency": "EUR" }"""),
            BusFactor = "2 of 11 devs",
        };

        var payload = DeliveryBuilder.Build(evidence, Request());

        Assert.Equal("2 of 11 devs", payload.Evidence.BusFactor);
        Assert.NotNull(payload.Evidence.RebuildCost);
        Assert.Equal(118000, payload.Evidence.RebuildCost!.AsObject()["low"]!.GetValue<int>());
    }

    [Fact]
    public void Descriptive_metrics_never_move_the_headline() // SACROSANCT
    {
        var without = SampleEvidence();
        var with = without with
        {
            RebuildCost = JsonNode.Parse("""{ "low": 118000, "high": 204000, "currency": "EUR" }"""),
            BusFactor = "2 of 11 devs",
        };

        var a = CaiScorer.Score(without);
        var b = CaiScorer.Score(with);

        // Bit-for-bit identical: the scorer reads none of these fields, so the headline (and band) cannot shift.
        Assert.Equal(a.Headline, b.Headline);
        Assert.Equal(a.Band, b.Band);
    }

    [Fact]
    public void A_package_without_descriptive_metrics_still_verifies_and_omits_them_from_the_canonical_form()
    {
        // BACKWARD COMPAT: an evidence bundle predating these fields signs, verifies, and never emits the members into
        // the canonical (signed) bytes — so an old package's signature is wholly unaffected by the schema addition.
        var pair = DeliveryKeyPair.Generate("cai-ed25519-test");
        var payload = DeliveryBuilder.Build(SampleEvidence(), Request()); // no rebuildCost / busFactor
        using var signer = new DeliverySigner(pair);
        var package = signer.SignPackage(payload);

        var r = DeliveryVerifier.Verify(package, new DeliveryPublicKeySet { Keys = [pair.ToPublicKey()] });
        Assert.True(r.Trustworthy, r.Reason);

        var canonical = System.Text.Encoding.UTF8.GetString(CanonicalJson.Canonicalize(package.Payload));
        Assert.DoesNotContain("rebuildCost", canonical);
        Assert.DoesNotContain("busFactor", canonical);
    }

    [Fact]
    public void Descriptive_metrics_survive_the_full_sign_reparse_verify_round_trip()
    {
        var pair = DeliveryKeyPair.Generate("cai-ed25519-test");
        var evidence = SampleEvidence() with
        {
            RebuildCost = JsonNode.Parse("""{ "low": 118000, "high": 204000, "currency": "EUR" }"""),
            BusFactor = "2 of 11 devs",
        };
        using var signer = new DeliverySigner(pair);
        var package = signer.SignPackage(DeliveryBuilder.Build(evidence, Request()));

        // Round-trip through the pretty-printed wire form, then verify signature + reproduce.
        var reparsed = DeliveryPackage.Parse(package.ToJson());
        var r = DeliveryVerifier.Verify(reparsed, new DeliveryPublicKeySet { Keys = [pair.ToPublicKey()] });

        Assert.True(r.Trustworthy, r.Reason);
        Assert.Equal("2 of 11 devs", reparsed.Payload.Evidence.BusFactor);
        Assert.Equal(118000, reparsed.Payload.Evidence.RebuildCost!.AsObject()["low"]!.GetValue<int>());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cai.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("could not locate repo root (Cai.slnx)");
    }
}
