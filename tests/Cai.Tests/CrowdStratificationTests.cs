using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Who the crowd actually is.
/// </summary>
/// <remarks>
/// <para>★★ Agreement statistics are blind to shared bias. Ten raters who all work in one language, or
/// all work for the vendor, will agree with each other at a rate that looks like reliability and is
/// nothing of the kind — κ measures whether raters agree, never whether the thing they agree on is true.
/// The only defence is to publish WHO answered, so a reader can see the concentration for themselves.</para>
/// <para>Nothing here excludes anyone. Dropping raters by who they are would be selection on a variable
/// correlated with the outcome; showing the composition costs nothing and lets the reader discount.</para>
/// </remarks>
public sealed class CrowdStratificationTests
{
    private static CrowdAnswer Answer(string rater, string finding = "f1") =>
        new(finding, rater, NoiseVerdict.Noise, MachineVerdict: null);

    private static RaterStratum Stratum(string rater, RaterAffiliation affiliation, string language = "csharp") =>
        new(rater, language, affiliation);

    /// <summary>
    /// ★★ VENDOR-AFFILIATED ANSWERS ARE REPORTED APART. A vendor rating its own tool's findings is the
    /// precise conflict the standard exists to remove, and merging those answers into one figure hides it
    /// behind an average — the same failure as merging scored and advisory dimensions into one rate.
    /// </summary>
    [Fact]
    public void STAR_vendor_affiliated_answers_are_never_merged_into_the_independent_figure()
    {
        var summary = CrowdStratification.Summarise(
            [Answer("indie-1"), Answer("indie-2"), Answer("vendor-1")],
            [
                Stratum("indie-1", RaterAffiliation.Independent),
                Stratum("indie-2", RaterAffiliation.Independent),
                Stratum("vendor-1", RaterAffiliation.VendorEmployed),
            ]);

        Assert.Equal(2, summary.Independent);
        Assert.Equal(1, summary.VendorAffiliated);
    }

    /// <summary>
    /// ★ An undeclared affiliation is NOT independence. Counting unknowns as independent lets the most
    /// interesting bias in the pool hide in the default — absence of a declaration is absence of evidence,
    /// not evidence of absence.
    /// </summary>
    [Fact]
    public void STAR_an_undeclared_affiliation_is_its_own_bucket()
    {
        var summary = CrowdStratification.Summarise([Answer("mystery")], []);

        Assert.Equal(0, summary.Independent);
        Assert.Equal(0, summary.VendorAffiliated);
        Assert.Equal(1, summary.Undeclared);
    }

    /// <summary>
    /// ★★ Concentration is published. A crowd that is 90% one language is a crowd whose agreement says
    /// something about that language's conventions rather than about the tool.
    /// </summary>
    [Fact]
    public void STAR_a_dominated_crowd_is_flagged_with_its_share()
    {
        List<CrowdAnswer> answers = [.. Enumerable.Range(0, 10).Select(i => Answer($"r{i}"))];
        List<RaterStratum> strata =
        [
            .. Enumerable.Range(0, 9).Select(i => Stratum($"r{i}", RaterAffiliation.Independent, "csharp")),
            Stratum("r9", RaterAffiliation.Independent, "python"),
        ];

        var summary = CrowdStratification.Summarise(answers, strata);

        Assert.True(summary.Dominated);
        Assert.Equal(0.9, summary.LargestLanguageShare!.Value, 3);
        Assert.Equal("csharp", summary.LargestLanguage);
    }

    [Fact]
    public void A_balanced_crowd_is_not_flagged()
    {
        List<CrowdAnswer> answers = [.. Enumerable.Range(0, 10).Select(i => Answer($"r{i}"))];
        List<RaterStratum> strata =
        [
            .. Enumerable.Range(0, 5).Select(i => Stratum($"r{i}", RaterAffiliation.Independent, "csharp")),
            .. Enumerable.Range(5, 5).Select(i => Stratum($"r{i}", RaterAffiliation.Independent, "python")),
        ];

        Assert.False(CrowdStratification.Summarise(answers, strata).Dominated);
    }

    /// <summary>
    /// ★ The composition is counted in ANSWERS, not raters. One person answering forty questions shapes a
    /// round far more than ten people answering one each, and a head-count would hide that entirely.
    /// </summary>
    [Fact]
    public void STAR_composition_is_weighted_by_answers_not_by_head_count()
    {
        List<CrowdAnswer> answers =
        [
            .. Enumerable.Range(0, 20).Select(i => Answer("prolific", $"f{i}")),
            Answer("occasional", "f99"),
        ];
        List<RaterStratum> strata =
        [
            Stratum("prolific", RaterAffiliation.VendorEmployed),
            Stratum("occasional", RaterAffiliation.Independent),
        ];

        var summary = CrowdStratification.Summarise(answers, strata);

        Assert.Equal(20, summary.VendorAffiliated);
        Assert.Equal(1, summary.Independent);
    }

    /// <summary>
    /// ★★ A RATER PAID IN PRODUCT IS NOT AN INDEPENDENT RATER. A cohort granted a paid tier in exchange
    /// for answering a daily question is compensated by the vendor — in kind rather than in cash, which
    /// changes the accounting and not the incentive. Counting them as independent would let a vendor
    /// manufacture its own independent bucket, which is the most valuable number on the page and the
    /// cheapest to fake.
    /// </summary>
    [Fact]
    public void STAR_a_rater_compensated_in_product_is_counted_apart_from_the_independent()
    {
        var summary = CrowdStratification.Summarise(
            [Answer("indie"), Answer("contributor", "f2")],
            [
                Stratum("indie", RaterAffiliation.Independent),
                Stratum("contributor", RaterAffiliation.CompensatedInProduct),
            ]);

        Assert.Equal(1, summary.Independent);
        Assert.Equal(1, summary.Compensated);
    }

    /// <summary>
    /// ★ Compensated is NOT vendor-affiliated either. Someone earning a subscription by answering is not
    /// on the vendor's staff, and folding the two together would overstate the conflict as surely as
    /// ignoring it understates it. Three buckets, because there are three situations.
    /// </summary>
    [Fact]
    public void STAR_compensated_is_neither_independent_nor_vendor_staff()
    {
        var summary = CrowdStratification.Summarise(
            [Answer("contributor")],
            [Stratum("contributor", RaterAffiliation.CompensatedInProduct)]);

        Assert.Equal(0, summary.Independent);
        Assert.Equal(0, summary.VendorAffiliated);
        Assert.Equal(1, summary.Compensated);
    }

    /// <summary>
    /// ★★ Nothing is excluded. Dropping raters by who they are selects on a variable correlated with the
    /// outcome; publishing the composition costs nothing and leaves the reader to discount.
    /// </summary>
    [Fact]
    public void STAR_no_answer_is_dropped_for_who_gave_it()
    {
        List<CrowdAnswer> answers = [Answer("vendor-1"), Answer("indie-1", "f2")];

        var kept = CrowdStratification.Retain(
            answers, [Stratum("vendor-1", RaterAffiliation.VendorEmployed)]);

        Assert.Equal(2, kept.Count);
    }
}
