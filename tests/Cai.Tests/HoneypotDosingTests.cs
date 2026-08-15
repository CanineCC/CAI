using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// How often a rater is handed a honeypot.
/// </summary>
/// <remarks>
/// <para>★★ Found by RUNNING it. Honeypots were handed out breadth-first like every other item, so a
/// rater accumulated them only by chance: in the live round both raters answered four and neither reached
/// the minimum sample, and with five honeypots planted among five hundred findings a person answering one
/// question a day would never be calibrated at all. Accuracy would be null for everyone forever — a gate
/// that fires and tells nobody.</para>
/// <para>So dosing is deliberate: while a rater is uncalibrated they meet honeypots at a target rate, and
/// once calibrated they stop, leaving the honeypot budget to the people it can still measure.</para>
/// </remarks>
public sealed class HoneypotDosingTests
{
    /// <summary>
    /// ★★ A rater who keeps answering DOES become calibrated. This is the whole point: the minimum sample
    /// has to be reachable, or publishing it is decoration.
    /// </summary>
    [Fact]
    public void STAR_a_rater_answering_steadily_reaches_the_minimum_sample()
    {
        var honeypots = 0;
        for (var answered = 0; answered < 40 && honeypots < RaterCalibration.MinimumSample; answered++)
        {
            if (HoneypotDosing.IsDue("rater-1", answered, honeypots))
            {
                honeypots++;
            }
        }

        Assert.Equal(RaterCalibration.MinimumSample, honeypots);
    }

    /// <summary>
    /// ★ Once calibrated, the dosing stops. Further honeypots buy no accuracy that the count does not
    /// already carry, and each one costs a real finding an answer.
    /// </summary>
    [Fact]
    public void STAR_a_calibrated_rater_is_not_dosed_again()
    {
        for (var answered = 0; answered < 200; answered++)
        {
            Assert.False(HoneypotDosing.IsDue("rater-1", answered, RaterCalibration.MinimumSample));
        }
    }

    /// <summary>
    /// ★★ Not every fourth question — the position varies BY RATER. A fixed cadence is learnable, and a
    /// rater who can predict which question is the test answers that one differently, which measures how
    /// carefully somebody answers while watched.
    /// </summary>
    [Fact]
    public void STAR_the_cadence_is_not_a_fixed_position()
    {
        List<int> firstDose = [];
        for (var r = 0; r < 12; r++)
        {
            for (var answered = 0; answered < 60; answered++)
            {
                if (HoneypotDosing.IsDue($"rater-{r}", answered, 0))
                {
                    firstDose.Add(answered);
                    break;
                }
            }
        }

        Assert.True(firstDose.Distinct().Count() > 1,
            "every rater meets their first honeypot at the same question — the cadence is learnable");
    }

    [Fact]
    public void The_decision_is_deterministic_for_a_rater_and_a_position()
    {
        Assert.Equal(
            HoneypotDosing.IsDue("rater-7", 13, 2),
            HoneypotDosing.IsDue("rater-7", 13, 2));
    }

    /// <summary>
    /// ★ Roughly one question in four, not one in two. Calibration that eats half the round measures the
    /// raters well and the tool barely at all.
    /// </summary>
    [Fact]
    public void The_dose_is_a_minority_of_a_raters_questions()
    {
        var dosed = 0;
        for (var r = 0; r < 200; r++)
        {
            if (HoneypotDosing.IsDue($"rater-{r}", 0, 0))
            {
                dosed++;
            }
        }

        Assert.InRange(dosed, 20, 90); // ~1 in 4 of 200, with room for the hash not being a fair coin
    }

    /// <summary>
    /// ★★ A honeypot is exempt from the per-item answer cap. Capped at three like a real finding, six
    /// planted honeypots would supply eighteen answers in total and could calibrate three people ever —
    /// whereas each rater needs their OWN five, and a honeypot's answer is already known so extra answers
    /// cost the measurement nothing.
    /// </summary>
    [Fact]
    public void STAR_a_honeypot_is_exempt_from_the_per_item_cap()
    {
        var queue = CrowdQueue.Build(
            [new CrowdCandidate("h1", CascadeState.NeedsHuman, "acme")], "s", spotCheck: 0);
        var load = new Dictionary<string, int> { ["h1"] = 99 };

        var next = CrowdQueue.Next(queue, "rater-1", [], load, honeypots: ["h1"]);

        Assert.Equal("h1", next!.FindingId);
    }

    /// <summary>A rater still never sees the same honeypot twice — theirs is answered, and that is that.</summary>
    [Fact]
    public void A_honeypot_already_answered_by_this_rater_is_not_repeated()
    {
        var queue = CrowdQueue.Build(
            [new CrowdCandidate("h1", CascadeState.NeedsHuman, "acme")], "s", spotCheck: 0);

        Assert.Null(CrowdQueue.Next(queue, "rater-1", ["h1"], load: null, honeypots: ["h1"]));
    }
}
