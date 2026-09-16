using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Two sets of scoring parameters with the same VALUES are the same parameters.
///
/// <para>These are records, which promises value semantics, and the question "did this rubric version change what it
/// pins?" is asked by comparing them. But <see cref="QualityBarParameters.Offsets"/> and
/// <see cref="QualityBarParameters.LensGroupFactors"/> are dictionaries, and a record's generated equality compares
/// members with <c>EqualityComparer&lt;T&gt;.Default</c> — which for a dictionary is REFERENCE equality. So a parsed
/// catalog's parameters never equalled the identical in-memory ones, and any check of the form "are these the same
/// rules?" silently answered no.</para>
/// </summary>
public sealed class ScoringParametersEqualityTests
{
    private static ScoringParameters RoundTripped() =>
        RubricCatalog.Parse(
            new RubricCatalog { RubricVersion = "rubric-test", Scoring = ScoringParameters.Default }.ToJson())
            .Scoring!;

    [Fact]
    public void Parameters_that_round_trip_through_a_catalog_equal_the_ones_that_went_in()
    {
        Assert.Equal(ScoringParameters.Default, RoundTripped());
    }

    [Fact]
    public void The_quality_bar_tables_compare_by_content_not_by_reference()
    {
        var a = ScoringParameters.Default.QualityBar;
        var b = RoundTripped().QualityBar;

        Assert.False(ReferenceEquals(a.Offsets, b.Offsets)); // genuinely different dictionary instances
        Assert.Equal(a, b);
    }

    [Fact]
    public void Parameters_that_differ_in_a_single_offset_are_not_equal()
    {
        // The other direction, so "equal" is not achieved by comparing nothing.
        var shifted = ScoringParameters.Default with
        {
            QualityBar = ScoringParameters.Default.QualityBar with
            {
                Offsets = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    [QualityBarTiers.Prototype] = -17.0, // was -18
                    [QualityBarTiers.Preview] = -8.0,
                    [QualityBarTiers.Production] = 0.0,
                    [QualityBarTiers.MissionCritical] = 6.0,
                },
            },
        };

        Assert.NotEqual(ScoringParameters.Default, shifted);
    }

    [Fact]
    public void Parameters_that_differ_in_a_cutline_are_not_equal()
    {
        var shifted = ScoringParameters.Default with { Bands = new BandCutlines { Exemplary = 93.0 } };

        Assert.NotEqual(ScoringParameters.Default, shifted);
    }
}
