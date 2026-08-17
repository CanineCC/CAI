using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Cai.Web.Noise;

/// <summary>What a second, independent judging pass over the sample found.</summary>
/// <param name="SampleSize">How many findings the standard asked to have re-judged.</param>
/// <param name="Compared">How many could actually be compared on the binary fold.</param>
/// <param name="Disagreements">Of those, how many crossed the noise boundary.</param>
/// <param name="DisagreementRate">
/// Disagreements over <paramref name="Compared"/>. ★ Null when nothing was comparable: 0 of 0 is 0 %, which
/// reads as a perfect result, and "nothing was re-judged" is the opposite claim from "the re-judge agreed".
/// </param>
/// <param name="Unjudged">
/// Sampled findings the second pass never answered. ★★ Named, and they BLOCK the tolerance: re-judge twenty,
/// answer the three that agree, and a rate over "compared" reports 0 % on a sample of three.
/// </param>
/// <param name="Excluded">
/// Sampled findings where either pass returned a process defect (<c>cannot-tell</c>, <c>rubric-ambiguous</c>).
/// ★ Those items already leave the rate, so they leave this comparison too — counted as neither agreement nor
/// disagreement, and named so the shrinking sample is visible.
/// </param>
/// <param name="Unusable">
/// Sampled findings where a verdict could not be parsed. ★ Refused rather than defaulted: an unrecognised
/// verdict silently folded into not-noise makes a typo look like agreement.
/// </param>
public sealed record RejudgeOutcome(
    int SampleSize,
    int Compared,
    int Disagreements,
    double? DisagreementRate,
    IReadOnlyList<string> Unjudged,
    IReadOnlyList<string> Excluded,
    IReadOnlyList<string> Unusable)
{
    /// <summary>
    /// Whether the second pass reproduced the first closely enough to call the instrument stable.
    /// </summary>
    /// <remarks>
    /// ★★ THREE WAYS TO FAIL, and only one of them is "the answers differed". An empty comparison and an
    /// incomplete one both fail, because both would otherwise report a flattering rate over a sample that
    /// shrank until it agreed.
    /// </remarks>
    public bool WithinTolerance =>
        Compared > 0
        && Unjudged.Count == 0
        && DisagreementRate is { } rate
        && rate <= Rejudge.Tolerance;
}

/// <summary>
/// The re-judge: the check that points at the standard's own judging rather than at a vendor's run.
/// </summary>
/// <remarks>
/// <para>★★ EVERY OTHER VERIFICATION ASKS WHETHER A RUN ANSWERED ITS HOLDOUT. This one asks whether the
/// INSTRUMENT is stable — judge a sample again, independently, and see whether the second pass reaches the same
/// answers. A rate produced by a process that does not reproduce is not a measurement however carefully the
/// corpus was drawn, and CAI owns the judging, so this is the check a critic asks for first.</para>
///
/// <para>★★ THE SAMPLE COMES FROM THE SEED. A sample the judged party picks is not a check, and neither is one
/// picked by whoever runs the re-judge. It is derived from the period's own published holdout seed, so a third
/// party can reproduce it from published values and nobody can steer it toward the findings that agree.</para>
/// </remarks>
public static class Rejudge
{
    /// <summary>How many findings a period's re-judge covers, when there are that many judged.</summary>
    /// <remarks>
    /// ★ Stated as a number rather than "a sample". Small enough to be affordable by hand, large enough that a
    /// single disagreement does not put a period outside tolerance on its own — at 30, one disagreement is
    /// 3.3 % against a 10 % ceiling.
    /// </remarks>
    public const int DefaultSampleSize = 30;

    /// <summary>The published disagreement ceiling.</summary>
    public const double Tolerance = 0.10;

    /// <summary>What agreement is measured on, stated because a reader would otherwise assume class-level.</summary>
    public const string Fold =
        "noise vs not-noise. The noise KINDS are folded together, and so are the two valid ones: the rate is "
      + "taken over \"noise or not\", so two passes that agree about that and differ about the cause agree "
      + "about the number. Requiring class agreement would manufacture instability out of vocabulary.";

    /// <summary>Why the ceiling is where it is — published beside the number.</summary>
    public const string ToleranceRationale =
        "Ten per cent on the binary noise/not-noise fold. Judging a borderline finding two ways is ordinary "
      + "disagreement between careful readers, and a ceiling tight enough to forbid it would make the check "
      + "fail on honest variation. Above it, the instrument is moving the published number by more than the "
      + "moves the number is used to argue about — a two-point improvement cannot be read off a process that "
      + "disagrees with itself by ten.";

    /// <summary>
    /// The findings to re-judge, drawn deterministically from the period's seed.
    /// </summary>
    /// <remarks>
    /// <para>★★ ORDER-INDEPENDENT. Each candidate is keyed by a hash of (seed, period, findingId) and the
    /// lowest keys win, so the sample is a property of the SET rather than of the query that listed it. Shuffling
    /// a list with a seeded PRNG would look equivalent and would not be: whoever controls the ordering of the
    /// input controls which findings get re-judged, which is a steerable sample wearing a seed's clothes.</para>
    ///
    /// <para>★ The period is in the key, so a fixed seed does not re-judge the same findings for ever — a
    /// judging drift affecting only the rest of the corpus would otherwise be invisible.</para>
    /// </remarks>
    public static IReadOnlyList<string> SelectSample(
        string seed, string period, IReadOnlyList<string> judgedFindingIds, int size = DefaultSampleSize)
    {
        ArgumentNullException.ThrowIfNull(judgedFindingIds);

        if (judgedFindingIds.Count == 0 || size <= 0)
        {
            return [];
        }

        return [.. judgedFindingIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => (Id: id, Key: Key(seed, period, id)))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)   // ties broken by the id, never by arrival
            .Take(size)
            .Select(x => x.Id)
            .OrderBy(id => id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Compare a second pass against the first, on the binary fold.
    /// </summary>
    /// <param name="sample">The findings the standard asked to have re-judged.</param>
    /// <param name="original">The verdict each finding settled at first time, keyed by finding id.</param>
    /// <param name="second">The independent pass's verdicts, keyed by finding id.</param>
    /// <remarks>
    /// ★★ NOISE VS NOT-NOISE, and this is ratified: the noise KINDS are beside the point for a rate taken over
    /// "noise or not". <c>noise</c> against <c>both-wrong</c> agrees about the number and differs about the
    /// cause; <c>valid-actionable</c> against <c>valid-not-actionable</c> likewise. Requiring class agreement
    /// would manufacture instability out of vocabulary.
    /// </remarks>
    public static RejudgeOutcome Compare(
        IReadOnlyList<string> sample,
        IReadOnlyDictionary<string, string> original,
        IReadOnlyDictionary<string, string> second)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(second);

        List<string> unjudged = [], excluded = [], unusable = [];
        var compared = 0;
        var disagreements = 0;

        foreach (var id in sample)
        {
            if (!second.TryGetValue(id, out var secondVerdict) || string.IsNullOrWhiteSpace(secondVerdict))
            {
                unjudged.Add(id);
                continue;
            }

            if (!original.TryGetValue(id, out var firstVerdict) || string.IsNullOrWhiteSpace(firstVerdict))
            {
                // ★ The FIRST pass is missing, which is a defect in the record rather than in the re-judge —
                // but it is still a sampled finding nobody can compare, so it is named the same way.
                unjudged.Add(id);
                continue;
            }

            if (NoiseVerdicts.ParseOrNull(firstVerdict) is not { } a
                || NoiseVerdicts.ParseOrNull(secondVerdict) is not { } b)
            {
                unusable.Add(id);
                continue;
            }

            // ★★ A process defect on either side leaves the comparison. Those items already leave the rate:
            // counting one as agreement would let a pass that gave up on half the sample read as stable, and
            // counting it as disagreement would report our own thin evidence as an unstable instrument.
            if (a.IsProcessDefect() || b.IsProcessDefect())
            {
                excluded.Add(id);
                continue;
            }

            compared++;
            if (a.IsNoise() != b.IsNoise())
            {
                disagreements++;
            }
        }

        return new RejudgeOutcome(
            SampleSize: sample.Count,
            Compared: compared,
            Disagreements: disagreements,
            DisagreementRate: compared > 0 ? (double)disagreements / compared : null,
            Unjudged: unjudged,
            Excluded: excluded,
            Unusable: unusable);
    }

    private static string Key(string seed, string period, string findingId)
    {
        var material = string.Create(
            CultureInfo.InvariantCulture, $"{seed}{period}rejudge{findingId}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
