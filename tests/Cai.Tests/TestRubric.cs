using Cai.Delivery;
using Cai.Scoring;

namespace Cai.Tests;

/// <summary>
/// Resolves the rubric a delivery test folds under — from the REAL published archive wherever it serves the version,
/// so a test mints exactly as production does.
///
/// <para>This matters for more than fidelity. A package witnesses the digest of the catalog it was folded under, and
/// the endpoints verify against the archive they serve; a synthetic stand-in would digest differently and be refused
/// for a reason that has nothing to do with what the test is asking. Falling back to an empty catalog keeps tests that
/// name a version the archive does not publish (fixtures, invented versions) working — such a catalog carries no
/// <c>scoring</c> block, so it folds under <see cref="ScoringParameters.Default"/>, which is what those tests assumed
/// when the fold took no catalog at all.</para>
/// </summary>
internal static class TestRubric
{
    private static readonly RubricCatalogStore? Archive = FindArchive();

    /// <summary>The published rubric for <paramref name="version"/>, or an empty catalog naming it.</summary>
    internal static ResolvedRubric For(string version) =>
        (Archive is null ? null : ResolvedRubric.FromStore(Archive, version))
        ?? ResolvedRubric.FromCatalog(new RubricCatalog { RubricVersion = version });

    /// <summary>The rubric an evidence bundle names.</summary>
    internal static ResolvedRubric For(EvidenceBundle evidence) => For(evidence.RubricVersion);

    /// <summary>The rubric a package names.</summary>
    internal static ResolvedRubric For(DeliveryPackage package) => For(package.Payload.RubricVersion);

    /// <summary>Walk up from the test assembly to the repo's rubrics/ directory, the same archive the app defaults to.</summary>
    private static RubricCatalogStore? FindArchive()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "rubrics");
            if (Directory.Exists(candidate)) return new RubricCatalogStore(candidate);
            dir = dir.Parent;
        }

        return null;
    }
}

/// <summary>
/// The delivery build/verify pair with the ambient rubric supplied, for tests whose subject is something ELSE —
/// tamper-evidence, key retirement, schema majors, wire shape. Tests whose subject IS the rubric-governed fold call
/// <see cref="DeliveryBuilder"/> and <see cref="DeliveryVerifier"/> directly, so the requirement stays visible where
/// it is the thing under test.
/// </summary>
internal static class DeliveryTestHelp
{
    internal static DeliveryPayload Build(EvidenceBundle evidence, DeliveryBuildRequest request) =>
        DeliveryBuilder.Build(evidence, TestRubric.For(evidence), request);

    internal static DeliveryVerification Verify(
        DeliveryPackage package, DeliveryPublicKeySet keys, double tolerance = 0.5) =>
        DeliveryVerifier.Verify(package, keys, TestRubric.For(package), tolerance);
}

/// <summary>
/// The fold with an ambient catalog supplied, for tests whose subject is the ARITHMETIC — OWA weighting, critical
/// gating, the surface floor, coherence, wire round-trips. They predate the catalog being required and were folding
/// under <see cref="ScoringParameters.Default"/> implicitly; an empty catalog naming the bundle's own version is that
/// same fold, now said out loud.
///
/// <para>Tests whose subject IS the rubric governing the fold call <see cref="CaiScorer"/> directly, so the
/// requirement stays visible exactly where it is the thing under test.</para>
/// </summary>
internal static class Fold
{
    /// <summary>Fold under a catalog that pins nothing — the shape of every rubric version published to date.</summary>
    internal static CaiScore Score(EvidenceBundle bundle) => CaiScorer.Score(bundle, Empty(bundle));

    /// <summary>Fold under a specific catalog.</summary>
    internal static CaiScore Score(EvidenceBundle bundle, RubricCatalog catalog) => CaiScorer.Score(bundle, catalog);

    /// <summary>Reproduce the claimed headline under a catalog that pins nothing.</summary>
    internal static VerifyResult Verify(EvidenceBundle bundle, double tolerance = 0.5) =>
        CaiScorer.Verify(bundle, Empty(bundle), tolerance);

    /// <summary>Reproduce the claimed headline under a specific catalog.</summary>
    internal static VerifyResult Verify(EvidenceBundle bundle, RubricCatalog catalog, double tolerance = 0.5) =>
        CaiScorer.Verify(bundle, catalog, tolerance);

    private static RubricCatalog Empty(EvidenceBundle bundle) => new() { RubricVersion = bundle.RubricVersion };
}
