namespace Cai.Web.Noise;

/// <summary>
/// How falsifiable a finding's claim is — the declaration without which two tools' rates are not comparable.
/// </summary>
/// <remarks>
/// <para>★★ A NOISE RATE PENALISES SPECIFICITY UNLESS THIS IS DECLARED. "Line 42 dereferences a value that
/// may be null" can be opened and shown false. "This file is a hotspot with declining code health" cannot —
/// a reader may disagree with it but has no way to falsify it, so it has no clean false-positive state. The
/// more checkable a tool's output, the more of it can be shown wrong, and the worse it looks beside a
/// competitor whose claims are softer. That is backwards, and it is the actual shape of this market:
/// behavioural-analysis vendors publish research on whether their metrics predict outcomes and publish no
/// false-positive rate at all; rule-based scanners document rules exhaustively and publish no measured error
/// rate either. <b>The first vendor to publish a noise rate is compared against silence, and silence reads
/// as clean.</b></para>
///
/// <para>★★ So the rate publishes PER CLASS and never only pooled. A tool that is 95 % pointwise and one
/// that is 80 % statistical do not have comparable pooled rates, and presenting them side by side is a
/// category error the standard refuses to make.</para>
/// </remarks>
public enum ClaimClass
{
    /// <summary>A claim about a specific location. Fully falsifiable — "line 42 dereferences null".</summary>
    Pointwise,

    /// <summary>A claim about a named artefact's shape. Mostly falsifiable — "this class has 14 dependencies".</summary>
    Structural,

    /// <summary>
    /// A claim about attention or risk. NOT directly falsifiable — "this file is a hotspot".
    /// </summary>
    /// <remarks>
    /// ★ A tool whose output is entirely this gets no noise rate, and the standard says so rather than
    /// scoring it zero. "Not measurable under this method" is an honest cell in a table; a blank that reads
    /// as clean is not.
    /// </remarks>
    Statistical,

    /// <summary>
    /// A recommendation resting on any of the above — scored SEPARATELY from the finding it rests on.
    /// </summary>
    /// <remarks>
    /// ★★ A detection can be right while its recommendation is wrong. That is the <c>valid-not-actionable</c>
    /// verdict, and for behavioural dimensions it is the USUAL failure mode rather than an edge case:
    /// "split that area out of the file" told to a file that is one function. Pooled with the detection it
    /// reads as a false positive; scored separately it reads as thin advice on a true finding, which is a
    /// different defect with a different fix.
    /// </remarks>
    Advisory,
}

/// <summary>One class's share of a run, and the rate measured within it.</summary>
/// <param name="Class">Which class.</param>
/// <param name="Judged">Findings of this class that reached a verdict.</param>
/// <param name="Noise">Of those, how many were noise.</param>
public sealed record ClaimClassTally(ClaimClass Class, int Judged, int Noise)
{
    /// <summary>
    /// The noise rate within this class, or null where the class does not admit one.
    /// </summary>
    /// <remarks>
    /// ★★ NULL for <see cref="ClaimClass.Statistical"/>, always — not zero, and not a number computed
    /// anyway. A statistical claim has no false-positive state, so a rate over it is a number about the
    /// raters' opinions rather than about the tool. Reported as "not measurable under this method".
    /// </remarks>
    public double? NoiseRate =>
        Class == ClaimClass.Statistical || Judged == 0 ? null : (double)Noise / Judged;

    /// <summary>Whether this class admits a noise rate at all.</summary>
    public bool Measurable => Class != ClaimClass.Statistical;
}

/// <summary>
/// The comparability rules: what a rate may be compared against, and what must be verified first.
/// </summary>
public static class ClaimSpecificity
{
    /// <summary>Parse a wire value, or null when the vocabulary does not know it.</summary>
    public static ClaimClass? ParseOrNull(string? value) =>
        (value ?? "").Trim().ToLowerInvariant().Replace("-", "").Replace("_", "") switch
        {
            "pointwise" => ClaimClass.Pointwise,
            "structural" => ClaimClass.Structural,
            "statistical" => ClaimClass.Statistical,
            "advisory" => ClaimClass.Advisory,
            _ => null,
        };

    /// <summary>The wire value.</summary>
    public static string Wire(ClaimClass value) => value switch
    {
        ClaimClass.Pointwise => "pointwise",
        ClaimClass.Structural => "structural",
        ClaimClass.Statistical => "statistical",
        _ => "advisory",
    };

    /// <summary>What the class asserts, published so a reader can check the declaration against the output.</summary>
    public static string Describes(ClaimClass value) => value switch
    {
        ClaimClass.Pointwise => "A claim about a specific location. Fully falsifiable.",
        ClaimClass.Structural => "A claim about a named artefact's shape. Mostly falsifiable.",
        ClaimClass.Statistical =>
            "A claim about attention or risk. NOT directly falsifiable, so it gets no noise rate.",
        _ => "A recommendation resting on a finding. Scored separately from the finding it rests on.",
    };

    /// <summary>
    /// The share of a run that is measurable at all — pointwise, structural and advisory over everything.
    /// </summary>
    /// <remarks>
    /// ★ Published beside every pooled rate, because a pooled rate over a run that is mostly statistical is
    /// a rate over a minority of the output. A reader given only the percentage cannot tell.
    /// </remarks>
    public static double? MeasurableShare(IReadOnlyCollection<ClaimClassTally> tallies)
    {
        ArgumentNullException.ThrowIfNull(tallies);

        var all = tallies.Sum(t => t.Judged);
        return all == 0 ? null : (double)tallies.Where(t => t.Measurable).Sum(t => t.Judged) / all;
    }

    /// <summary>
    /// True when a run has no falsifiable output at all, and therefore gets NO noise rate.
    /// </summary>
    /// <remarks>
    /// ★★ The standard says "not measurable under this method" rather than scoring such a tool zero. A tool
    /// that only ever says "this area deserves attention" is not a clean tool; it is a tool this instrument
    /// cannot read, and publishing 0 % for it would be the single most misleading number the standard could
    /// produce.
    /// </remarks>
    public static bool NothingFalsifiable(IReadOnlyCollection<ClaimClassTally> tallies)
    {
        ArgumentNullException.ThrowIfNull(tallies);
        return tallies.Count > 0 && tallies.Where(t => t.Measurable).Sum(t => t.Judged) == 0;
    }
}
