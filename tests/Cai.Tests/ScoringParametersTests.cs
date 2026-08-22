using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The score-moving constants are rubric DATA, not compile-time constants of whichever build happens to be scoring.
///
/// <para>ADR-0004 requires that "a catalog must pin every input that can move a score," and the OWA decays, the
/// critical gate, the architecture surface floor and the band cutlines are exactly such inputs — but they lived only
/// in <c>Cai.Scoring</c>, so verifying a two-month-old report ran today's values. These tests hold the contract:</para>
/// <list type="bullet">
///   <item>a catalog publishing no <c>scoring</c> block folds exactly as before, so every already-published rubric
///     version keeps verifying to the same number;</item>
///   <item>a catalog that publishes one GOVERNS the fold — the rubric version selects the semantics;</item>
///   <item>and the block survives the catalog round-trip, so an archived catalog carries what it meant.</item>
/// </list>
/// </summary>
public sealed class ScoringParametersTests
{
    private static EvidenceBundle Evidence() => new()
    {
        RubricVersion = "rubric-test",
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

    private static RubricCatalog Catalog(ScoringParameters? scoring) =>
        new() { RubricVersion = "rubric-test", Scoring = scoring };

    [Fact]
    public void A_catalog_without_a_scoring_block_folds_exactly_as_the_scorer_always_did()
    {
        // BACKWARD COMPAT, and the whole reason the fallback exists: every rubric version published before the block
        // must keep reproducing its original number.
        var noCatalog = CaiScorer.Score(Evidence());
        var emptyCatalog = CaiScorer.Score(Evidence(), Catalog(null));

        Assert.Equal(noCatalog.Headline, emptyCatalog.Headline);
        Assert.Equal(noCatalog.Band, emptyCatalog.Band);
        Assert.Equal(noCatalog.Lenses.Count, emptyCatalog.Lenses.Count);
    }

    [Fact]
    public void The_defaults_are_the_constants_the_scorer_has_always_published()
    {
        var p = ScoringParameters.Default;

        Assert.Equal(CaiScorer.WithinLensQ, p.WithinLensQ);
        Assert.Equal(CaiScorer.AcrossLensQ, p.AcrossLensQ);
        Assert.Equal(CaiScorer.CriticalGate, p.CriticalGate);
        Assert.Equal(90.0, p.Bands.Exemplary);
        Assert.Equal(70.0, p.Bands.Healthy);
        Assert.Equal(50.0, p.Bands.Fair);
        Assert.Equal(25.0, p.Bands.Poor);
        Assert.Equal(2, p.ArchitectureSurface.MinProjects);
        Assert.Equal(1500, p.ArchitectureSurface.MinProductionLoc);
        Assert.Equal(69.0, p.ArchitectureSurface.LowSurfaceCap);
    }

    [Fact]
    public void A_catalogs_OWA_decay_governs_the_fold()
    {
        var baseline = CaiScorer.Score(Evidence(), Catalog(null)).Headline;

        var sharper = CaiScorer.Score(Evidence(), Catalog(new ScoringParameters { AcrossLensQ = 0.40 })).Headline;
        var flatter = CaiScorer.Score(Evidence(), Catalog(new ScoringParameters { AcrossLensQ = 0.75 })).Headline;

        // Worst-first: a sharper decay leans harder on the weakest lens, a flatter one spreads the weight.
        Assert.True(sharper < baseline, $"sharper {sharper} should sit below baseline {baseline}");
        Assert.True(flatter > baseline, $"flatter {flatter} should sit above baseline {baseline}");
    }

    [Fact]
    public void A_catalogs_CUTLINES_govern_the_published_word() // the reason cutlines are rubric data
    {
        var evidence = Evidence();
        var headline = CaiScorer.Score(evidence, Catalog(null)).Headline;

        // Same evidence, same number — banded through two different published cutline sets.
        var strict = CaiScorer.Score(evidence, Catalog(new ScoringParameters
        {
            Bands = new BandCutlines { Exemplary = 99, Healthy = 95, Fair = 90, Poor = 80 },
        }));
        var lenient = CaiScorer.Score(evidence, Catalog(new ScoringParameters
        {
            Bands = new BandCutlines { Exemplary = 60, Healthy = 40, Fair = 20, Poor = 10 },
        }));

        Assert.Equal(headline, strict.Headline);
        Assert.Equal(headline, lenient.Headline);
        Assert.Equal(Band.Critical, strict.Band);
        Assert.Equal(Band.Exemplary, lenient.Band);
    }

    [Fact]
    public void A_catalogs_critical_gate_governs_which_contributors_gate_a_lens()
    {
        // D9 sits at 7.0. A gate above it makes it critical; the default gate (4.0) does not.
        var ungated = CaiScorer.Score(Evidence(), Catalog(null));
        var gated = CaiScorer.Score(Evidence(), Catalog(new ScoringParameters { CriticalGate = 7.5 }));

        Assert.DoesNotContain(ungated.Lenses, l => l.CriticalGated);
        Assert.Contains(gated.Lenses, l => l.CriticalGated && l.CriticalContributors.Contains("D9"));

        // The gate moves the band, never the number.
        Assert.Equal(ungated.Headline, gated.Headline);
    }

    [Fact]
    public void A_catalogs_architecture_surface_floor_governs_the_cap()
    {
        // 3 projects / 4000 LoC clears the default bar. Raise the bar past it and the lens caps.
        var uncapped = CaiScorer.Score(Evidence(), Catalog(null));
        var capped = CaiScorer.Score(Evidence(), Catalog(new ScoringParameters
        {
            ArchitectureSurface = new ArchitectureSurfaceParameters
            {
                MinProjects = 10, MinProductionLoc = 100_000, LowSurfaceCap = 30.0,
            },
        }));

        Assert.Equal(30.0, capped.Lenses.Single(l => l.Lens == "architecture").Score);
        Assert.True(capped.Headline < uncapped.Headline);
    }

    [Fact]
    public void A_catalogs_quality_bar_offsets_govern_the_lens_band()
    {
        var evidence = Evidence() with { QualityBar = "prototype" };

        var standard = CaiScorer.Score(evidence, Catalog(null));
        var noShift = CaiScorer.Score(evidence, Catalog(new ScoringParameters
        {
            QualityBar = new QualityBarParameters
            {
                Offsets = new Dictionary<string, double>(StringComparer.Ordinal) { [QualityBarTiers.Prototype] = 0.0 },
            },
        }));

        // A prototype's lens lines sit lower by default, so removing the offset can only band the same score equal or
        // stricter — and the score itself never moves.
        Assert.Equal(standard.Headline, noShift.Headline);
        Assert.All(standard.Lenses.Zip(noShift.Lenses), pair => Assert.True(pair.First.Band >= pair.Second.Band));
    }

    [Fact]
    public void Quality_bar_aliases_all_normalise_to_their_canonical_tier()
    {
        // The catalog keys offsets by canonical tier, so the alias set stays a parsing concern, never a scoring one.
        Assert.All(new[] { "template", "poc", "one-off", "prototype", "PROTOTYPE", " One Off " },
            a => Assert.Equal(QualityBarTiers.Prototype, QualityBarTiers.Canonical(a)));
        Assert.All(new[] { "preview", "alpha", "beta" },
            a => Assert.Equal(QualityBarTiers.Preview, QualityBarTiers.Canonical(a)));
        Assert.Equal(QualityBarTiers.MissionCritical, QualityBarTiers.Canonical("missioncritical"));

        // An absent or unrecognised bar is the BASELINE, never a lenient one.
        Assert.Equal(QualityBarTiers.Production, QualityBarTiers.Canonical(null));
        Assert.Equal(QualityBarTiers.Production, QualityBarTiers.Canonical("whatever"));
    }

    [Fact]
    public void The_scoring_block_survives_the_catalog_round_trip()
    {
        // An archived catalog must carry what it meant, or pinning the parameters proves nothing.
        var catalog = Catalog(new ScoringParameters
        {
            WithinLensQ = 0.72,
            AcrossLensQ = 0.51,
            CriticalGate = 3.5,
            Bands = new BandCutlines { Exemplary = 92, Healthy = 71, Fair = 51, Poor = 26 },
            ArchitectureSurface = new ArchitectureSurfaceParameters { MinProjects = 3, MinProductionLoc = 2000, LowSurfaceCap = 65 },
        });

        var reparsed = RubricCatalog.Parse(catalog.ToJson());

        Assert.NotNull(reparsed.Scoring);
        Assert.Equal(0.72, reparsed.Scoring!.WithinLensQ);
        Assert.Equal(0.51, reparsed.Scoring.AcrossLensQ);
        Assert.Equal(3.5, reparsed.Scoring.CriticalGate);
        Assert.Equal(71, reparsed.Scoring.Bands.Healthy);
        Assert.Equal(2000, reparsed.Scoring.ArchitectureSurface.MinProductionLoc);

        // And it still governs after the round-trip.
        Assert.Equal(
            CaiScorer.Score(Evidence(), catalog).Headline,
            CaiScorer.Score(Evidence(), reparsed).Headline);
    }

    [Fact]
    public void A_catalog_with_no_scoring_block_omits_it_from_the_serialized_form()
    {
        // Catalogs already published must not gain a field, or their content digest would change.
        Assert.DoesNotContain("scoring", Catalog(null).ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_rubric_versions_replay_their_OWN_parameters_from_the_same_evidence() // the point of the exercise
    {
        var evidence = Evidence();

        var oldRubric = Catalog(new ScoringParameters { AcrossLensQ = 0.40 });
        var newRubric = Catalog(new ScoringParameters { AcrossLensQ = 0.75 });

        var underOld = CaiScorer.Score(evidence, oldRubric).Headline;
        var underNew = CaiScorer.Score(evidence, newRubric).Headline;

        Assert.NotEqual(underOld, underNew);

        // Re-scoring the old rubric after the new one exists still yields the old number: the semantics travel with
        // the catalog, so no version-dispatch code and no historical code path is needed.
        Assert.Equal(underOld, CaiScorer.Score(evidence, oldRubric).Headline);
    }
}
