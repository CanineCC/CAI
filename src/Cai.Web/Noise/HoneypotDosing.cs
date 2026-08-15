using System.Globalization;

namespace Cai.Web.Noise;

/// <summary>
/// How often an uncalibrated rater is handed a honeypot.
/// </summary>
/// <remarks>
/// <para>★★ Handing honeypots out breadth-first with everything else leaves a rater to meet them by
/// chance. The live round showed it: two people answered five questions each, hit four honeypots each,
/// and neither reached the minimum sample. With five honeypots planted among five hundred findings, a
/// person answering one question a day would never be calibrated at all — accuracy null for everyone,
/// forever, which is a gate that fires and tells nobody.</para>
/// <para>★ It is a RATE, not a position. A fixed cadence — every fourth question — is learnable, and a
/// rater who knows which question is the test answers that one differently, which measures how carefully
/// somebody answers while watched rather than how well they judge.</para>
/// </remarks>
public static class HoneypotDosing
{
    /// <summary>Roughly one question in this many is a honeypot, while the rater is uncalibrated.</summary>
    /// <remarks>
    /// ★ A minority, deliberately. Calibration that eats half the round measures the raters well and the
    /// tool barely at all — and the tool is what anyone came for.
    /// </remarks>
    public const int OneIn = 4;

    /// <summary>
    /// Whether this rater's next question should be a honeypot.
    /// </summary>
    /// <param name="answeredSoFar">How many questions this rater has answered in this round.</param>
    /// <param name="honeypotsAnswered">How many of those were honeypots.</param>
    public static bool IsDue(
        string raterId,
        int answeredSoFar,
        int honeypotsAnswered,
        int minimumSample = RaterCalibration.MinimumSample,
        int oneIn = OneIn)
    {
        // ★ Once calibrated, dosing stops. Further honeypots buy no accuracy the count does not already
        // carry, and each one costs a real finding an answer.
        if (honeypotsAnswered >= minimumSample)
        {
            return false;
        }

        // Deterministic per (rater, position), so it is reproducible and auditable — and unpredictable to
        // the rater, who knows neither the seed's construction nor their own position in it.
        var rank = HoldoutSampler.Rank(
            "honeypot-dose", string.Create(CultureInfo.InvariantCulture, $"{raterId}\0{answeredSoFar}"));

        return int.Parse(rank[..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture) % oneIn == 0;
    }
}
