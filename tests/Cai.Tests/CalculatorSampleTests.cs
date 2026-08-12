using Cai.Scoring;
using Cai.Web;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The calculator's own worked example must fold. It did not: the shipped sample put meta-dimensions in
/// <c>dimensions</c> and gave every entry a <c>lens</c> key that a deterministic dimension does not have, so
/// "Load the sample bundle" → "Compute the CAI" answered <c>Value cannot be null. (Parameter 'wire')</c>.
///
/// <para>That is the worst place in the project for a defect: the calculator is the one interactive demonstration
/// behind "don't trust the number, reproduce it", and the sample is the first bundle anyone ever writes one from.
/// These tests fold it on every build so it cannot silently rot again — including when a future rubric renames or
/// retires a dimension the sample happens to cite.</para>
/// </summary>
public sealed class CalculatorSampleTests
{
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

    private static RubricCatalog SampleCatalog() =>
        Store().Get(CalculatorSample.RubricVersion)
        ?? throw new InvalidOperationException(
            $"the calculator sample names rubric '{CalculatorSample.RubricVersion}', which the archive does not " +
            "publish — a reader loading the sample would be told to verify against a catalog they cannot fetch");

    [Fact]
    public void The_shipped_sample_folds_to_a_headline()
    {
        var bundle = EvidenceBundle.Parse(CalculatorSample.Json);

        var score = CaiScorer.Score(bundle, SampleCatalog());

        Assert.InRange(score.Headline, 0.0, 100.0);
        Assert.NotEmpty(score.Lenses);
    }

    [Fact]
    public void The_sample_names_a_rubric_the_archive_actually_publishes()
    {
        Assert.Contains(CalculatorSample.RubricVersion, Store().Versions());
    }

    [Fact]
    public void Every_dimension_the_sample_cites_exists_in_that_rubric()
    {
        // A sample citing a retired id would score (the fold does not require catalog membership) while teaching a
        // bundle format that names dimensions the rubric does not define.
        var catalog = SampleCatalog();
        var known = catalog.Dimensions.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var bundle = EvidenceBundle.Parse(CalculatorSample.Json);

        var strangers = bundle.Dimensions.Select(d => d.Id)
            .Concat(bundle.MetaDimensions.Select(m => m.Id))
            .Where(id => !known.Contains(id))
            .ToList();

        Assert.True(
            strangers.Count == 0,
            $"the sample cites ids that rubric '{catalog.RubricVersion}' does not define: {string.Join(", ", strangers)}");
    }

    [Fact]
    public void The_sample_puts_meta_dimensions_where_they_fold()
    {
        // The original defect, stated as a rule: a meta feeds its lens directly and has no category, so it cannot sit
        // in `dimensions` — and a deterministic dimension cannot sit in `metaDimensions`.
        var catalog = SampleCatalog();
        var family = catalog.Dimensions.ToDictionary(d => d.Id, d => d.Family, StringComparer.Ordinal);
        var bundle = EvidenceBundle.Parse(CalculatorSample.Json);

        Assert.All(bundle.Dimensions, d => Assert.Equal("dimension", family[d.Id]));
        Assert.All(bundle.MetaDimensions, m => Assert.Equal("meta", family[m.Id]));
    }

    // ── The category is derivable, which is why the sample need not restate it ─────────────────────────────────────

    [Fact]
    public void A_bundle_that_omits_a_derivable_category_scores_identically_to_one_that_states_it()
    {
        // The published catalog froze the assignment, so restating it is redundant. Requiring it rejected every
        // bundle written from the documented examples, none of which show a category.
        var catalog = SampleCatalog();
        var omitted = EvidenceBundle.Parse(CalculatorSample.Json);
        var byId = catalog.Dimensions.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var stated = omitted with
        {
            Dimensions =
            [
                .. omitted.Dimensions.Select(d =>
                    new DimensionScore(d.Id, byId[d.Id].Category!, d.ScoreZeroToTen, d.Confidence) { Coverage = d.Coverage }),
            ],
        };

        Assert.Equal(
            CaiScorer.Score(stated, catalog).Headline,
            CaiScorer.Score(omitted, catalog).Headline,
            10);
    }

    [Fact]
    public void Omitting_a_category_the_rubric_does_not_publish_says_what_to_do_about_it()
    {
        // Pre-.18 catalogs carry no map, so there the bundle IS the only source. That must read as an instruction,
        // not as the ArgumentNullException the reader used to get.
        var older = Store().Versions()
            .Select(v => Store().Get(v)!)
            .First(c => c.Dimensions
                .Where(d => string.Equals(d.Family, "dimension", StringComparison.Ordinal))
                .All(d => string.IsNullOrWhiteSpace(d.Category)));

        var bundle = new EvidenceBundle
        {
            RubricVersion = older.RubricVersion,
            AnalyzableProjects = 5,
            ProductionLoc = 5000,
            Dimensions = [new DimensionScore("D1", "", 7.0, 1.0)],
        };

        var boom = Assert.Throws<ArgumentException>(() => CaiScorer.Score(bundle, older));

        Assert.Contains("D1", boom.Message, StringComparison.Ordinal);
        Assert.Contains(older.RubricVersion, boom.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter 'wire'", boom.Message, StringComparison.Ordinal);
    }
}
