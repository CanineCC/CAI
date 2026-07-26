using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The dimension→category map is part of the FROZEN rubric, not of whoever happens to be scoring.
///
/// <para>A dimension's category is a score-moving input: dimensions in one category average together
/// (confidence-weighted) before the lens's worst-first OWA sees them, so moving a dimension from one category to
/// another changes the number for unchanged evidence. The standard promises that any such change mints a new rubric
/// version — but the category was never published in <c>rubric-catalog.json</c> (it was collapsed into the dimension's
/// <c>lens</c> at emit time and otherwise lived only in the producer's own code), so a re-homing could move scores
/// while the rubric version stood still. These tests hold the gap shut:</para>
/// <list type="bullet">
///   <item>the published catalog pins a category for every scored dimension, consistent with the lens it declares;</item>
///   <item>the scorer folds a dimension under the category the CATALOG publishes;</item>
///   <item>evidence that contradicts the frozen map is refused rather than scored under an unpublished map;</item>
///   <item>and publishing the map moved no number — a catalog with categories scores identically to one without.</item>
/// </list>
/// </summary>
public sealed class RubricCategoryContractTests
{
    /// <summary>The first rubric version whose catalog publishes the dimension→category map. Earlier versions are
    /// legitimately category-less and must keep verifying through the bundle-declared fallback.</summary>
    private const string FirstVersionWithCategories = "rubric-2026.08.18";

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cai.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new DirectoryNotFoundException("could not locate repo root (Cai.slnx)");
        }
    }

    private static RubricCatalogStore Store() => new(Path.Combine(RepoRoot, "rubrics"));

    private static RubricCatalog Latest() =>
        Store().Get(Store().Latest() ?? throw new InvalidOperationException("no rubric versions published"))
        ?? throw new InvalidOperationException("the latest rubric version has no servable catalog");

    /// <summary>The catalog entries that actually fold through a category — the deterministic D-dimensions. A
    /// meta-dimension feeds its lens directly and has no category.</summary>
    private static IEnumerable<CatalogDimension> Scored(RubricCatalog catalog) =>
        catalog.Dimensions.Where(d => string.Equals(d.Family, "dimension", StringComparison.Ordinal));

    // ── What the published archive must say ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_published_map_carries_a_category_for_every_scored_dimension()
    {
        var catalog = Latest();

        var missing = Scored(catalog).Where(d => string.IsNullOrWhiteSpace(d.Category)).Select(d => d.Id).ToList();

        Assert.True(
            missing.Count == 0,
            "These dimensions fold through a category but the published catalog does not say which — so re-homing " +
            "them would move scores with nothing in the frozen rubric to contradict it. Regenerate the catalog from " +
            "an engine build that emits `category` (tools/rubric/publish-rubric-catalog.sh):\n  " +
            string.Join(", ", missing));
    }

    [Fact]
    public void A_meta_dimension_publishes_no_category_because_it_has_none()
    {
        // A meta feeds its lens directly, beside the categories. Publishing a category for one would invent a
        // roll-up step the fold does not perform.
        var strays = Latest().Dimensions
            .Where(d => !string.Equals(d.Family, "dimension", StringComparison.Ordinal))
            .Where(d => !string.IsNullOrWhiteSpace(d.Category))
            .Select(d => d.Id)
            .ToList();

        Assert.Empty(strays);
    }

    [Fact]
    public void Every_published_category_folds_into_the_lens_the_same_entry_declares()
    {
        // The catalog states both halves of the ladder (dimension→category and, implicitly, category→lens). If they
        // disagree, the catalog is describing a fold nobody performs — and a consumer reading `lens` and a consumer
        // reading `category` would get different answers from the same published document.
        var mismatched = Scored(Latest())
            .Select(d => (d.Id, d.Category, Declared: d.Lens, Folds: Categories.LensOf(Categories.Parse(d.Category!))))
            .Where(x => !string.Equals(x.Declared, x.Folds, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            mismatched.Count == 0,
            "The published category and the published lens disagree for:\n  " +
            string.Join("\n  ", mismatched.Select(x => $"{x.Id}: category '{x.Category}' folds into '{x.Folds}', but the catalog declares lens '{x.Declared}'")));
    }

    [Fact]
    public void The_archive_publishes_the_first_version_that_freezes_the_category_map()
    {
        Assert.Contains(FirstVersionWithCategories, Store().Versions());
    }

    // ── What the scorer must do with it ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_scorer_folds_every_dimension_under_the_category_the_catalog_publishes()
    {
        // The round trip: take the published map, feed it back as evidence, and confirm the fold reproduces exactly
        // the grouping the catalog describes — same categories, each under the lens the catalog assigns it.
        var catalog = Latest();
        var bundle = BundleFrom(catalog);

        var score = CaiScorer.Score(bundle, catalog);

        var expected = Scored(catalog)
            .GroupBy(d => Categories.Parse(d.Category!))
            .ToDictionary(g => g.Key.ToString(), g => (Lens: Categories.LensOf(g.Key), Count: g.Count()));

        Assert.Equal(expected.Count, score.Categories.Count);
        foreach (var actual in score.Categories)
        {
            var want = expected[actual.Category];
            Assert.Equal(want.Lens, actual.Lens);
            Assert.Equal(want.Count, actual.DimensionCount);
        }
    }

    [Fact]
    public void Publishing_the_map_moved_no_number()
    {
        // The behaviour-preservation proof, and the reason this could ship without a scoring change: the categories
        // written into the catalog are exactly the ones the evidence already declared, so folding WITH the frozen map
        // and folding without it produce the same headline, the same lenses and the same categories.
        var catalog = Latest();
        var bundle = BundleFrom(catalog);

        var pinned = CaiScorer.Score(bundle, catalog);
        var legacy = CaiScorer.Score(bundle);

        Assert.Equal(legacy.Headline, pinned.Headline, 10);
        Assert.Equal(legacy.Band, pinned.Band);
        Assert.Equal(
            legacy.Lenses.Select(l => (l.Lens, l.Score)),
            pinned.Lenses.Select(l => (l.Lens, l.Score)));
        Assert.Equal(
            legacy.Categories.Select(c => (c.Category, c.Lens, c.Score)),
            pinned.Categories.Select(c => (c.Category, c.Lens, c.Score)));
    }

    [Fact]
    public void Re_homing_a_dimension_without_minting_a_rubric_version_is_refused()
    {
        // THE GUARD. A producer whose internal map has drifted from the rubric it names sends evidence that puts a
        // dimension in a category the frozen catalog does not. Scoring it under either map would publish a number
        // computed under criteria nobody can fetch — so it is refused, loudly, naming both sides.
        var catalog = Latest();
        var moved = Scored(catalog).First(d => Categories.Parse(d.Category!) != DimensionCategory.SecurityCompliance);
        var bundle = new EvidenceBundle
        {
            RubricVersion = catalog.RubricVersion,
            AnalyzableProjects = 5,
            ProductionLoc = 5000,
            Dimensions = [new DimensionScore(moved.Id, "security-compliance", 7.0, 1.0)],
        };

        var boom = Assert.Throws<ArgumentException>(() => CaiScorer.Score(bundle, catalog));

        Assert.Contains(moved.Id, boom.Message, StringComparison.Ordinal);
        Assert.Contains("security-compliance", boom.Message, StringComparison.Ordinal);
        Assert.Contains(moved.Category!, boom.Message, StringComparison.Ordinal);
        Assert.Contains(catalog.RubricVersion, boom.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_category_the_scorer_does_not_implement_fails_closed()
    {
        // Fail CLOSED: a catalog naming a category this build cannot fold describes a rubric it cannot honour.
        // Falling back to the bundle's own category would quietly score under the producer's map instead.
        var catalog = new RubricCatalog
        {
            RubricVersion = "rubric-2099.01.1",
            Lenses = [new CatalogLens("codeHealth", "Code Health")],
            Dimensions = [new CatalogDimension { Id = "D1", Name = "X", Lens = "codeHealth", Category = "vibes", Family = "dimension" }],
        };
        var bundle = new EvidenceBundle
        {
            RubricVersion = "rubric-2099.01.1",
            AnalyzableProjects = 5,
            ProductionLoc = 5000,
            Dimensions = [new DimensionScore("D1", "code-quality", 7.0, 1.0)],
        };

        var boom = Assert.Throws<ArgumentException>(() => CaiScorer.Score(bundle, catalog));

        Assert.Contains("vibes", boom.Message, StringComparison.Ordinal);
        Assert.Contains("rubric-2099.01.1", boom.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_catalog_published_before_the_map_was_frozen_still_verifies()
    {
        // Backward compatibility is the whole point of an archive: a consumer pinned to a pre-.18 version must still
        // reproduce its number. Those catalogs carry no category, so the fold falls back to the bundle's own — which
        // is exactly how those scores were computed when they were published.
        var older = Store().Versions()
            .Select(v => Store().Get(v)!)
            .First(c => Scored(c).All(d => string.IsNullOrWhiteSpace(d.Category)));

        var bundle = new EvidenceBundle
        {
            RubricVersion = older.RubricVersion,
            AnalyzableProjects = 5,
            ProductionLoc = 5000,
            Dimensions =
            [
                new DimensionScore("D1", "code-quality", 8.0, 1.0),
                new DimensionScore("D5", "architecture", 6.0, 1.0),
                new DimensionScore("D28", "security-compliance", 5.0, 1.0),
            ],
        };

        Assert.Empty(older.CategoryMap());
        Assert.Equal(CaiScorer.Score(bundle).Headline, CaiScorer.Score(bundle, older).Headline, 10);
    }

    [Fact]
    public void No_catalog_may_silently_lose_its_categories_on_the_way_out_of_the_api()
    {
        // The API serves catalogs by re-serializing the parsed model, so a field the model does not carry is dropped
        // between the archive and the consumer — which would hand a reader a catalog with no category map while the
        // archive holds one.
        var catalog = Latest();

        var served = RubricCatalog.Parse(catalog.ToJson());

        Assert.Equal(
            Scored(catalog).Select(d => (d.Id, d.Category, d.Lens, d.ScoringPolarity)),
            Scored(served).Select(d => (d.Id, d.Category, d.Lens, d.ScoringPolarity)));
    }

    [Fact]
    public void Every_category_the_scorer_implements_has_a_stable_wire_name()
    {
        // The wire name is what a catalog and a bundle both write; it must round-trip, or a published map could not
        // be read back as the category it names.
        foreach (var category in Categories.All)
        {
            var wire = Categories.WireName(category);
            Assert.Equal(category, Categories.Parse(wire));
            Assert.DoesNotContain(wire, char.IsUpper);
        }
    }

    /// <summary>Evidence that exercises every scored dimension in a catalog, declaring the category the catalog
    /// publishes, with spread-out scores so the fold is non-degenerate.</summary>
    private static EvidenceBundle BundleFrom(RubricCatalog catalog) => new()
    {
        RubricVersion = catalog.RubricVersion,
        AnalyzableProjects = 5,
        ProductionLoc = 5000,
        Dimensions = [.. Scored(catalog).Select((d, i) => new DimensionScore(d.Id, d.Category!, 3.0 + (i % 8), 1.0))],
    };
}
