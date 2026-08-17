namespace Cai.Web.Noise;

/// <summary>The four facts a mark is decided on, as CAI already holds them.</summary>
public sealed record MarkInputs(
    bool RanAgainstTheHoldout,
    bool SubmittedBeforeTheDeadline,
    bool RunReproduces,
    bool NumbersPublishedInFull);

/// <summary>One condition, and the fact that decides it.</summary>
/// <param name="Reads">
/// ★★ WHAT CAI LOOKS AT. A condition worded as a judgement — "the run looks credible" — could not be checked by
/// the tool it was applied to, and that is the difference between a mechanical state and an unreviewable veto.
/// </param>
public sealed record MarkCondition(string Name, string Reads);

/// <summary>A condition that failed, with why.</summary>
public sealed record MarkFailure(string Condition, string Why);

/// <summary>One tool's mark for one period.</summary>
public sealed record MarkState(
    string Tool,
    string Period,
    bool Granted,
    IReadOnlyList<MarkCondition> Conditions,
    IReadOnlyList<MarkFailure> Failing,
    string Statement,
    string AppealRoute);

/// <summary>
/// The compliance mark: mechanical, revocable, free, and never the word "certified".
/// </summary>
/// <remarks>
/// <para>★★ A MARK THAT CAN BE PULLED IS POWER OVER A COMPETITOR'S MARKETING, and pulling one is far more
/// newsworthy than granting one. Every condition here is a fact CAI already holds, so a mark is granted and
/// revoked by arithmetic rather than by anybody's opinion — a judgement call in this file would be an
/// unreviewable veto held by a participant over its rivals.</para>
///
/// <para>★★ "CAI-MEASURED", NEVER "CAI-CERTIFIED". Certification implies an audit of the tool; this is a record
/// that a run was measured under a published method, and the difference is the whole liability position. A test
/// asserts the word appears in nothing this class publishes, because that is exactly the word that leaks out of
/// one careless label and into everyone's marketing.</para>
///
/// <para>★★ AND THREE OF THE FOUR CONDITIONS ARE ABOUT PROCESS. A tool with a terrible noise rate that ran the
/// right corpus, in time, and published in full earns the mark — the mark says the measurement happened properly
/// and the RATE says how it went. Conflating them would turn it into a quality badge nobody voted for.</para>
/// </remarks>
public static class ComplianceMark
{
    /// <summary>The words the mark is published under.</summary>
    public const string Label = "CAI-measured";

    public const string RanAgainstTheHoldout = "ran-against-the-published-holdout";
    public const string SubmittedBeforeTheDeadline = "submitted-before-the-deadline";
    public const string RunReproduces = "the-run-reproduces";
    public const string NumbersPublishedInFull = "numbers-published-in-full";

    /// <summary>Whether earning the mark costs anything. It does not.</summary>
    public const bool Free = true;

    /// <summary>
    /// When a change to the above may take effect.
    /// </summary>
    /// <remarks>
    /// ★★ NOT "we intend to keep it free" — a rule about WHEN. A fee introduced against a period participants have
    /// already run is a fee they cannot decline: they have spent the compute, and withdrawing is not permitted.
    /// </remarks>
    public const string ChangeRule =
        "The mark is free to earn. Any change to that — a fee, or a new condition — takes effect no earlier than "
      + "one full published period after it is announced, and never for a period already opened. A change that "
      + "reached an open period would bind participants who had already run it and cannot withdraw.";

    /// <summary>The wording rule, published so it binds our own copy as much as anybody's.</summary>
    public const string WordingRule =
        "The mark is 'CAI-measured'. It is never 'CAI-approved' and never the c-word for an audit: this is a "
      + "record that a run was measured under a published method, not an examination of the tool that produced "
      + "it. The distinction is the whole liability position, and it binds what CAI publishes first.";

    /// <summary>Where a withheld or revoked mark is argued.</summary>
    /// <remarks>
    /// ★ The SAME path as a contested verdict, deliberately: a separate appeals process for the mark would be one
    /// more thing a participant has to discover, and the dispute mechanism already publishes either way.
    /// </remarks>
    public const string AppealRoute =
        "A withheld or revoked mark is appealed the same way a verdict is contested: raise a dispute with a "
      + "reason at POST /api/noise/verdicts/{findingId}/dispute, or for the mark itself write to the standard "
      + "naming the condition and why you believe it holds. The answer publishes either way.";

    /// <summary>The four conditions, in the order they are read.</summary>
    public static IReadOnlyList<MarkCondition> Conditions { get; } =
    [
        new(RanAgainstTheHoldout,
            "the submission's findings are on repositories in this period's published draw, at the pinned shas — "
          + "the membership and sha checks the receipt already reports."),
        new(SubmittedBeforeTheDeadline,
            "a submission exists for this tool and period, received before the period's published deadline. "
          + "Withdrawal is not possible, so there is nothing else to check here."),
        new(RunReproduces,
            "the receipt is ACCEPTED — every verification check passed, including the finding count and the "
          + "re-judge of the period's seed-drawn sample within its published tolerance."),
        new(NumbersPublishedInFull,
            "a publication exists for this period and met the contract: the census balances, the claim classes "
          + "are declared, and the absolutes are published beside the rate."),
    ];

    /// <summary>Decide the mark from the four facts.</summary>
    public static MarkState Evaluate(string tool, string period, MarkInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var failing = new List<MarkFailure>();

        void Check(bool met, string condition, string why)
        {
            if (!met)
            {
                failing.Add(new MarkFailure(condition, why));
            }
        }

        Check(inputs.RanAgainstTheHoldout, RanAgainstTheHoldout,
            "this tool has no accepted run over this period's published draw, so there is nothing the mark could "
          + "describe.");
        Check(inputs.SubmittedBeforeTheDeadline, SubmittedBeforeTheDeadline,
            "no submission for this tool arrived before the period's published deadline. The deadline is what "
          + "stops a result being assembled after seeing everybody else's.");
        Check(inputs.RunReproduces, RunReproduces,
            "the run did not pass every verification check — see the receipt's problems, which name each one.");
        Check(inputs.NumbersPublishedInFull, NumbersPublishedInFull,
            "no result meeting the publication contract exists for this period. A measurement that is not "
          + "published in full is one nobody can check.");

        var granted = failing.Count == 0;

        return new MarkState(
            Tool: tool,
            Period: period,
            Granted: granted,
            Conditions: Conditions,
            Failing: failing,
            Statement: granted
                ? $"{tool} is {Label} for {period}: it ran the published draw, submitted in time, passed every "
                + "verification check, and published its numbers in full. That is a statement about the "
                + "MEASUREMENT — it is not a statement about how good the tool is, which is what the published "
                + "rate is for."
                : $"{tool} is not {Label} for {period}. The conditions that did not hold are named above, each "
                + "with the fact it reads. This is not a statement about how good the tool is.",
            AppealRoute: AppealRoute);
    }
}
