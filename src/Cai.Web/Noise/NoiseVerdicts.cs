namespace Cai.Web.Noise;

/// <summary>Who gave a verdict. The two sides answer the same question and differ in exactly one place.</summary>
public enum NoiseRater
{
    /// <summary>A person working through the audit.</summary>
    Human,

    /// <summary>A model judging in the cascade.</summary>
    Machine,
}

/// <summary>
/// The verdict set the standard defines, which every participating engine implements.
/// </summary>
/// <remarks>
/// <para>★ THE STANDARD OWNS THIS, not any scanner. Watchdog's cycle 1 had a rater answering a binary
/// while its judge answered a five-class taxonomy — agreement between two different questions is not a
/// measurement. That drift happened inside ONE organisation with one team; between organisations it is
/// certain unless the contract is published and machine-readable.</para>
/// <para>Two things a plain binary cannot express. <b>Valid but not actionable</b> separates whether a
/// finding is TRUE from whether it is USEFUL — a correct finding nobody can act on is a true positive
/// for the detector and a failure for the reader, and a rater forced to choose picks differently from
/// the next rater. And two <b>process defects</b>, which are not verdicts about the finding at all: the
/// evidence was insufficient, or the rubric has no determinate answer here.</para>
/// </remarks>
public enum NoiseVerdict
{
    /// <summary>It should not have fired. Scores as noise.</summary>
    Noise,

    /// <summary>True, and a reader can act on it. Scores as valid.</summary>
    ValidAndActionable,

    /// <summary>True, but too thin to act on. Scores as VALID; carries the failure on the actionability axis.</summary>
    ValidNotActionable,

    /// <summary>Neither the judge's reading nor its opposite is right. Scores as noise, and escalates.</summary>
    BothPositionsWrong,

    /// <summary>The evidence shown was not enough to decide. A process defect, not a verdict.</summary>
    CannotTell,

    /// <summary>The rubric has no determinate answer here. A process defect, not a verdict.</summary>
    RubricAmbiguous,
}

/// <summary>The rules each verdict carries — the single reader of what they mean.</summary>
public static class NoiseVerdicts
{
    /// <summary>The value carried on the wire, in a submission and in a published verdict record.</summary>
    public static string Wire(this NoiseVerdict v) => v switch
    {
        NoiseVerdict.Noise => "noise",
        NoiseVerdict.ValidAndActionable => "valid-actionable",
        NoiseVerdict.ValidNotActionable => "valid-not-actionable",
        NoiseVerdict.BothPositionsWrong => "both-wrong",
        NoiseVerdict.CannotTell => "cannot-tell",
        NoiseVerdict.RubricAmbiguous => "rubric-ambiguous",
        _ => throw new InvalidOperationException($"unmapped verdict {v}"),
    };

    /// <summary>A one-line statement of what the verdict asserts, for an implementer reading the contract.</summary>
    public static string Meaning(this NoiseVerdict v) => v switch
    {
        NoiseVerdict.Noise => "Should not have fired — untrue at the cited code, or not worth a reader's time.",
        NoiseVerdict.ValidAndActionable => "True, and it says enough to act on.",
        NoiseVerdict.ValidNotActionable => "True, but its reasoning or remedy is too thin to act on.",
        NoiseVerdict.BothPositionsWrong => "The finding as raised is wrong, and so is its opposite.",
        NoiseVerdict.CannotTell => "The evidence presented was insufficient to decide. Not a verdict about the finding.",
        NoiseVerdict.RubricAmbiguous => "The rubric has no determinate answer here. Not a verdict about the finding.",
        _ => throw new InvalidOperationException($"unmapped verdict {v}"),
    };

    /// <summary>Whether the verdict contributes to the rate at all.</summary>
    public static bool CountsTowardRate(this NoiseVerdict v) => !v.IsProcessDefect();

    /// <summary>Whether this reports a defect in our process rather than judging the finding.</summary>
    public static bool IsProcessDefect(this NoiseVerdict v) =>
        v is NoiseVerdict.CannotTell or NoiseVerdict.RubricAmbiguous;

    /// <summary>The binary boundary agreement is evaluated on. Meaningful only where it counts.</summary>
    /// <remarks>
    /// The noise classes overlap in practice — redundant, opinion-not-fact and shape-irrelevant are
    /// frequently three readings of one finding — so requiring class agreement between two vendors would
    /// manufacture disagreement about vocabulary rather than measuring anything.
    /// </remarks>
    public static bool IsNoise(this NoiseVerdict v) =>
        v is NoiseVerdict.Noise or NoiseVerdict.BothPositionsWrong;

    /// <summary>Whether a reader could act on it. NULL where the question does not arise.</summary>
    /// <remarks>
    /// ★ Asked only of findings judged valid. A finding that should not have fired is not "unactionable",
    /// it is wrong — folding the two together would let a noisy tool read as merely unhelpful.
    /// </remarks>
    public static bool? IsActionable(this NoiseVerdict v) => v switch
    {
        NoiseVerdict.ValidAndActionable => true,
        NoiseVerdict.ValidNotActionable => false,
        _ => null,
    };

    /// <summary>
    /// ★★ THE ASYMMETRY, published rather than assumed.
    /// </summary>
    /// <remarks>
    /// A HUMAN who cannot tell has hit an evidence defect: the item leaves the rate and files the defect.
    /// A MACHINE that cannot tell must ESCALATE — excluding there hands the pipeline a way to duck its
    /// hardest cases and still report a clean rate, which is selecting on the outcome by another name.
    /// An implementer who gets this backwards publishes a flattering number in good faith, so the
    /// standard states it.
    /// </remarks>
    public static bool ExcludesFor(this NoiseVerdict v, NoiseRater rater) => v switch
    {
        NoiseVerdict.RubricAmbiguous => true,
        NoiseVerdict.CannotTell => rater == NoiseRater.Human,
        _ => false,
    };

    /// <summary>Whether this verdict, from this rater, sends the finding to a person.</summary>
    public static bool EscalatesFor(this NoiseVerdict v, NoiseRater rater) =>
        v is NoiseVerdict.BothPositionsWrong
        || (v is NoiseVerdict.CannotTell && rater == NoiseRater.Machine);

    /// <summary>Canonicalise a wire value, ignoring case and separators. Null when unrecognised.</summary>
    /// <remarks>
    /// A value the standard does not know is refused rather than guessed: a rate is compared ordinally,
    /// so an unrecognised verdict would silently count as one side and move a published number.
    /// </remarks>
    public static NoiseVerdict? ParseOrNull(string? verdict)
    {
        var squashed = new string((verdict ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return squashed switch
        {
            "" => null,
            "noise" => NoiseVerdict.Noise,
            "validactionable" or "valid" or "notnoise" => NoiseVerdict.ValidAndActionable,
            "validnotactionable" => NoiseVerdict.ValidNotActionable,
            "bothwrong" or "bothpositionswrong" => NoiseVerdict.BothPositionsWrong,
            "cannottell" or "canttell" => NoiseVerdict.CannotTell,
            "rubricambiguous" => NoiseVerdict.RubricAmbiguous,
            _ => null,
        };
    }
}
