namespace Cai.Web.Noise;

/// <summary>One tool's finding, with the adjudication that decided whether it was real.</summary>
/// <param name="HasFixPairOracle">
/// ★★ Whether a real before/after fix pair establishes this defect independently of anybody's agreement. Those
/// findings LEAVE the pool: a commit is evidence where a pool is only consensus, and blending the two would
/// publish the blend under the stronger name. Excluded and counted, so the scope of the pseudo-oracle is visible.
/// </param>
public sealed record PooledFinding(
    string Tool, string RepoId, string FilePath, int Line, bool Valid, bool HasFixPairOracle = false);

/// <summary>What one tool did against the pooled reference.</summary>
/// <param name="Precision">
/// ★★ NULL when the tool reported nothing. Zero over zero is not 100%, and "no noise" from a tool that
/// found nothing is the flattering reading this whole metric has to be defended against.
/// </param>
/// <param name="PooledRecall">
/// ★★ Named for what it is. "Recall" would be a claim about the defects that exist; this is a claim about
/// the defects somebody found, and the two differ by exactly the blind spot every participant shares.
/// </param>
/// <param name="UniqueContribution">
/// ★ How much of the union only this tool found — the figure showing the pool is not one tool's opinion
/// echoed back, and the reason a tool with middling recall can still be worth having.
/// </param>
/// <param name="LeaveOneOutReferenceSize">
/// ★★ THIS TOOL'S OWN DENOMINATOR: the number of pooled defects at least one OTHER tool found. Every tool is
/// measured against a different reference, so a single shared union size would be the wrong denominator for
/// everybody — and the figure could not be checked by the reader it is published for.
/// </param>
/// <param name="PooledRecallUnavailable">
/// Why there is no recall figure, when there is none. ★ Stated rather than left as a blank: a missing number and
/// a low one are opposite claims.
/// </param>
/// <param name="WithoutCoordinate">
/// ★★ How many of this tool's findings could not enter the union because they carry no usable coordinate (#21).
/// PER TOOL, not only as a total: a tool whose output is mostly repo-level is being measured on a fraction of
/// what it reported, and its recall figure means much less than the same figure from a tool whose findings all
/// carry a line. A single total hides exactly that difference — which is the difference this axis exists to show.
/// </param>
public sealed record PooledToolResult(
    string Tool, int Reported, int Valid, int MatchedUnion, int UniqueContribution,
    double? Precision, double? PooledRecall,
    int LeaveOneOutReferenceSize = 0, string? PooledRecallUnavailable = null,
    int WithoutCoordinate = 0);

/// <summary>The pooled reference and every participant's standing against it.</summary>
/// <param name="PooledRecallAvailable">
/// Whether the pool is large enough for the figure to mean what its name says. ★ False below
/// <see cref="PooledRecall.MinimumTools"/>.
/// </param>
/// <param name="PseudoOracle">
/// Always true, and said out loud. ★ The difference between "our recall is 62 %" and "62 % of what this pool
/// found" is the whole reading, and a reader who takes the first has been misled by the name alone.
/// </param>
/// <param name="Scope">What the figure covers, and what it deliberately does not.</param>
/// <param name="ExcludedWithFixPairOracle">
/// How many findings left the pool because a real fix pair establishes them. ★ Counted, so the scope is a
/// published number rather than an assumption.
/// </param>
/// <param name="UnmatchableWithoutCoordinate">
/// ★★ Rows that never entered the union because they carry no usable coordinate (#21) — no file, or a file with
/// no line. Measured at 26 % of a real corpus, weighted towards exactly the repo-level dimensions this axis
/// exists to cover. Published rather than dropped: a union that quietly loses a quarter of the data reports a
/// recall figure about the code-level findings every tool already agrees on.
/// </param>
/// <param name="CoordinateGapNote">Why they could not be matched — see <see cref="PooledRecall.CoordinateGap"/>.</param>
public sealed record PooledRecallSummary(
    int ParticipatingTools, int UnionSize, int LineWindow,
    IReadOnlyList<PooledToolResult> Tools, string Caveat,
    bool PooledRecallAvailable = false, bool PseudoOracle = true,
    string Scope = "", int ExcludedWithFixPairOracle = 0,
    int UnmatchableWithoutCoordinate = 0, string CoordinateGapNote = "");

/// <summary>
/// Recall, as far as it can honestly be measured without ground truth.
/// </summary>
/// <remarks>
/// <para>★★ A noise rate measures PRECISION and nothing else, and precision alone rewards under-firing: a
/// tool reporting one finding it is certain about scores a perfect 0% noise, while a tool reporting
/// everything worth knowing scores worse. Published alone it is an incentive to say less — the opposite
/// of what anybody buys a scanner for.</para>
/// <para>There is no ground truth on real repositories, so the reference is POOLED: the union of what
/// every participating tool reported and a human adjudicated as valid. This is the standard's strongest
/// argument for existing at all — one vendor cannot build it, because it requires somebody else's
/// findings on the same code.</para>
/// </remarks>
public static class PooledRecall
{
    /// <summary>
    /// How many lines apart two findings may be and still be the same defect.
    /// </summary>
    /// <remarks>
    /// ★ Declared and published. Two tools pointing at one defect rarely agree to the line, and an
    /// undeclared tolerance is a knob whoever computes the number can quietly turn.
    /// </remarks>
    public const int DefaultLineWindow = 3;

    /// <summary>
    /// The smallest pool in which this figure means what its name says.
    /// </summary>
    /// <remarks>
    /// ★★ THREE, and the reason is arithmetic rather than caution. At two tools the leave-one-out reference IS
    /// the other tool's findings, so "pooled recall" is pairwise agreement and a tool scores well by being
    /// SIMILAR to its one comparator. It computed at two before this was fixed, which made the number available
    /// exactly when it meant something else.
    /// </remarks>
    public const int MinimumTools = 3;

    /// <summary>
    /// Whether a finding can be matched across vendors at all.
    /// </summary>
    /// <remarks>
    /// ★★ A COORDINATE IS THE ONLY THING TWO VENDORS SHARE. Rule ids are each vendor's own vocabulary and the
    /// standard publishes no mapping between them, so without a file and a line there is nothing to match on.
    /// A file with NO LINE is the same case: "something in this manifest" from two tools cannot be shown to mean
    /// one defect, and the line window would match it to every other line-less row in the same file.
    /// </remarks>
    public static bool HasUsableCoordinate(PooledFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return !string.IsNullOrWhiteSpace(finding.FilePath) && finding.Line > 0;
    }

    /// <summary>Why coordinate-less rows are excluded, published beside the count.</summary>
    public const string CoordinateGap =
        "these rows carry no usable coordinate — no file, or a file with no line — so nothing can match them "
      + "across tools: a rule id is each vendor's own vocabulary and this standard publishes no mapping between "
      + "them. They are EXCLUDED and counted rather than merged: with no file and no line they all fall inside "
      + "the line window of each other, so one repository's repo-level findings would collapse into a single "
      + "pooled defect that every tool appears to have found — inflating every participant's recall on exactly "
      + "the dimensions the pool is least able to judge.";

    /// <summary>What the figure covers, and what it deliberately leaves to a better oracle.</summary>
    public const string PooledScope =
        "Findings whose reality rests on agreement. Anything a real before/after FIX PAIR establishes is "
      + "excluded and counted separately: a commit is evidence where a pool is consensus, and blending them "
      + "would publish the blend under the stronger name.";

    public const string PooledCaveat =
        "POOLED recall, not recall — a PSEUDO-ORACLE. Each tool is scored against the union of what every OTHER "
        + "participating tool reported and a human adjudicated as valid, never against a union including its "
        + "own findings: that would score a tool 100 % against a reference it wrote, which with one "
        + "participant means scoring ourselves 100 % by construction. A defect no participant found is still "
        + "invisible, so every figure here OVERSTATES. Comparable between the tools in this pool and not "
        + "comparable to any recall measured against a seeded or hand-built defect set.";

    /// <summary>
    /// Build the pooled reference and score each tool against it.
    /// </summary>
    /// <param name="silentTools">
    /// ★ Tools that submitted a run and reported nothing. Named explicitly, because otherwise they vanish
    /// from the table entirely and a tool that found nothing looks the same as one that never entered.
    /// </param>
    public static PooledRecallSummary Compute(
        IReadOnlyCollection<PooledFinding> findings,
        int lineWindow = DefaultLineWindow,
        IReadOnlyCollection<string>? silentTools = null)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentOutOfRangeException.ThrowIfNegative(lineWindow);

        // ★★ Findings with a real fix pair leave the pool before the union is built — see PooledScope. Counted,
        // so the scope is a number a reader can see rather than a sentence they have to trust.
        var fixPairBacked = findings.Count(f => f.HasFixPairOracle);
        var poolable = findings.Where(f => !f.HasFixPairOracle).ToList();

        // ★★ THE COORDINATE GAP (#21). Rows with nothing to match on leave the union before it is built, and
        // they are counted per tool — see CoordinateGap for why merging them would be worse than dropping them.
        var unmatchable = poolable.Where(f => !HasUsableCoordinate(f)).ToList();
        var matchable = poolable.Where(HasUsableCoordinate).ToList();

        // ★ Only what a human adjudicated VALID becomes a defect. Noise in the reference would reward a
        // tool for reproducing another tool's mistakes.
        var union = BuildUnion([.. matchable.Where(f => f.Valid)], lineWindow);

        var tools = poolable.Select(f => f.Tool)
            .Concat(silentTools ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

        // ★★ THE FLOOR, decided once for the whole pool. See MinimumTools.
        var enoughTools = tools.Count >= MinimumTools;
        var unavailable = enoughTools
            ? null
            : $"pooled recall needs at least {MinimumTools} participating tools and this pool has "
            + $"{tools.Count}. Below three, each tool's leave-one-out reference is essentially one other "
            + "tool's findings, so the figure is pairwise agreement — a tool scores well by being SIMILAR "
            + "rather than by being deep.";

        List<PooledToolResult> results = [];
        foreach (var tool in tools)
        {
            var mine = poolable.Where(f => string.Equals(f.Tool, tool, StringComparison.OrdinalIgnoreCase)).ToList();
            var matched = union.Where(d => d.Tools.Contains(tool, StringComparer.OrdinalIgnoreCase)).ToList();

            // ★★ LEAVE-ONE-OUT. The reference is the defects at least one OTHER tool found — this tool's own
            // are removed, so a tool cannot be credited for agreeing with itself. Against a union including
            // itself, the tool that alone found everything scores a perfect 100 % against a reference it wrote,
            // and with one participant that reference is ours: "depth" would have meant "agrees with Watchdog".
            var reference = union
                .Where(d => d.Tools.Any(t => !string.Equals(t, tool, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var found = reference.Count(d => d.Tools.Contains(tool, StringComparer.OrdinalIgnoreCase));

            results.Add(new PooledToolResult(
                Tool: tool,
                Reported: mine.Count,
                Valid: mine.Count(f => f.Valid),
                MatchedUnion: matched.Count,
                UniqueContribution: matched.Count(d => d.Tools.Count == 1),

                // ★★ Null, never 1.0, when nothing was reported.
                Precision: mine.Count > 0 ? (double)mine.Count(f => f.Valid) / mine.Count : null,

                // ★ Null when the pool is too small, or when the others found nothing at all — 0 of 0 is not
                // a recall of zero, it is the absence of a reference to have recall against.
                PooledRecall: enoughTools && reference.Count > 0
                    ? (double)found / reference.Count
                    : null,

                LeaveOneOutReferenceSize: reference.Count,
                WithoutCoordinate: unmatchable.Count(f =>
                    string.Equals(f.Tool, tool, StringComparison.OrdinalIgnoreCase)),
                PooledRecallUnavailable: enoughTools
                    ? reference.Count == 0
                        ? "no other participating tool reported a valid finding, so there is no reference "
                        + "for this tool to be measured against."
                        : null
                    : unavailable));
        }

        return new PooledRecallSummary(
            tools.Count, union.Count, lineWindow, results, PooledCaveat,
            PooledRecallAvailable: enoughTools,
            PseudoOracle: true,
            Scope: PooledScope,
            ExcludedWithFixPairOracle: fixPairBacked,
            UnmatchableWithoutCoordinate: unmatchable.Count,
            CoordinateGapNote: CoordinateGap);
    }

    private sealed record Defect(string RepoId, string FilePath, int Line, HashSet<string> Tools);

    private static List<Defect> BuildUnion(IReadOnlyList<PooledFinding> valid, int lineWindow)
    {
        List<Defect> union = [];

        // Ordered so the union is the same whatever sequence the submissions arrived in — two runs of the
        // same data producing different unions would make every recall figure unreproducible.
        foreach (var f in valid
            .OrderBy(f => f.RepoId, StringComparer.Ordinal)
            .ThenBy(f => f.FilePath, StringComparer.Ordinal)
            .ThenBy(f => f.Line)
            .ThenBy(f => f.Tool, StringComparer.Ordinal))
        {
            var existing = union.FirstOrDefault(d =>
                string.Equals(d.RepoId, f.RepoId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.FilePath, f.FilePath, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(d.Line - f.Line) <= lineWindow);

            if (existing is null)
            {
                union.Add(new Defect(f.RepoId, f.FilePath, f.Line,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { f.Tool }));
            }
            else
            {
                existing.Tools.Add(f.Tool);
            }
        }

        return union;
    }
}
