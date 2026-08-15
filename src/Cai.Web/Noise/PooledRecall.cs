namespace Cai.Web.Noise;

/// <summary>One tool's finding, with the adjudication that decided whether it was real.</summary>
public sealed record PooledFinding(string Tool, string RepoId, string FilePath, int Line, bool Valid);

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
public sealed record PooledToolResult(
    string Tool, int Reported, int Valid, int MatchedUnion, int UniqueContribution,
    double? Precision, double? PooledRecall);

/// <summary>The pooled reference and every participant's standing against it.</summary>
public sealed record PooledRecallSummary(
    int ParticipatingTools, int UnionSize, int LineWindow,
    IReadOnlyList<PooledToolResult> Tools, string Caveat);

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

    public const string PooledCaveat =
        "POOLED recall, not recall. The reference is the union of what participating tools reported and a "
        + "human adjudicated as valid, so a defect no participant found is invisible to it and every "
        + "figure here OVERSTATES. It is comparable between the tools in this pool and not comparable to "
        + "any recall measured against a seeded or hand-built defect set.";

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

        // ★ Only what a human adjudicated VALID becomes a defect. Noise in the reference would reward a
        // tool for reproducing another tool's mistakes.
        var union = BuildUnion([.. findings.Where(f => f.Valid)], lineWindow);

        var tools = findings.Select(f => f.Tool)
            .Concat(silentTools ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

        List<PooledToolResult> results = [];
        foreach (var tool in tools)
        {
            var mine = findings.Where(f => string.Equals(f.Tool, tool, StringComparison.OrdinalIgnoreCase)).ToList();
            var matched = union.Where(d => d.Tools.Contains(tool, StringComparer.OrdinalIgnoreCase)).ToList();

            results.Add(new PooledToolResult(
                Tool: tool,
                Reported: mine.Count,
                Valid: mine.Count(f => f.Valid),
                MatchedUnion: matched.Count,
                UniqueContribution: matched.Count(d => d.Tools.Count == 1),

                // ★★ Null, never 1.0, when nothing was reported.
                Precision: mine.Count > 0 ? (double)mine.Count(f => f.Valid) / mine.Count : null,

                // ★★ Null when there is no pool: one tool's recall against a union it alone defines is
                // 100% by construction — a number that says nothing and reads as everything.
                PooledRecall: tools.Count >= 2 && union.Count > 0 ? (double)matched.Count / union.Count : null));
        }

        return new PooledRecallSummary(tools.Count, union.Count, lineWindow, results, PooledCaveat);
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
