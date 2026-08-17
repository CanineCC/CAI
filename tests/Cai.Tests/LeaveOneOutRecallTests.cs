using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Pooled recall, measured against the union of everyone ELSE.
/// </summary>
/// <remarks>
/// <para>★★ THE MOST ATTACKABLE LINE THE METHOD COULD CONTAIN, and it was in it. Scoring a tool against a union
/// that includes its own findings makes the tool that found everything alone score 100 % against a reference it
/// wrote — and since Watchdog is the only participant today, that reference is ours. "Depth" would have meant
/// "how much you agree with Watchdog", and we would have scored 100 % by construction. Leave-one-out removes the
/// designated baseline without needing anybody to be trusted.</para>
///
/// <para>★★ AND IT REFUSES BELOW THREE TOOLS. At N=2 the leave-one-out reference IS the other tool's findings,
/// so "recall" is pairwise agreement and a tool looks deep by being SIMILAR. That is not a weaker version of the
/// measurement, it is a different one wearing its name.</para>
///
/// <para>★ A PSEUDO-ORACLE, labelled. The reference is what somebody found and a human adjudicated, so a defect
/// no participant found is invisible and every figure OVERSTATES. Where a real before/after fix pair exists it
/// is a better oracle than any pool, so those findings leave the pool rather than diluting it.</para>
/// </remarks>
public sealed class LeaveOneOutRecallTests
{
    private static PooledFinding F(
        string tool, string repo, int line, bool valid = true, bool fixPair = false) =>
        new(tool, repo, "src/Thing.cs", line, valid, fixPair);

    // ── The leave-one-out reference ────────────────────────────────────────────────────────────────

    [Fact]
    public void STAR_A_Tool_That_Alone_Found_Everything_Scores_ZERO_Not_A_Hundred()
    {
        // ★★ THE DEFECT THIS ITEM EXISTS FOR. Against a union including itself, `alone` matches every defect and
        // scores 100 % — a perfect depth score awarded for being the only participant. Against the union of the
        // OTHERS it found nothing anybody else found, which is 0: an honest statement that the pool has no
        // evidence this tool covers what the others cover.
        var summary = PooledRecall.Compute(
        [
            F("alone", "repo-a", 10), F("alone", "repo-a", 50), F("alone", "repo-a", 90),
            F("other-1", "repo-b", 10),
            F("other-2", "repo-b", 10),
        ]);

        var alone = summary.Tools.Single(t => t.Tool == "alone");
        Assert.Equal(0d, alone.PooledRecall);
        Assert.Null(alone.PooledRecallUnavailable);

        // ★ And its unique contribution is still 3 — the pool is not one tool's opinion echoed back, and a
        // tool with no overlap can still be the only reason three defects are in the union at all.
        Assert.Equal(3, alone.UniqueContribution);
    }

    [Fact]
    public void STAR_The_Reference_Is_Sized_Per_TOOL_Not_Once_For_The_Pool()
    {
        // ★★ Each tool is measured against a DIFFERENT reference — its own findings removed — so the
        // denominator has to be published per tool. One shared union size would silently be the wrong
        // denominator for everybody.
        var summary = PooledRecall.Compute(
        [
            F("a", "repo", 10), F("b", "repo", 10), F("c", "repo", 10),   // one shared defect
            F("a", "repo", 200),                                          // a's alone
        ]);

        var a = summary.Tools.Single(t => t.Tool == "a");
        var b = summary.Tools.Single(t => t.Tool == "b");

        Assert.Equal(2, summary.UnionSize);              // two defects in the pool
        Assert.Equal(1, a.LeaveOneOutReferenceSize);     // b and c found only the shared one
        Assert.Equal(2, b.LeaveOneOutReferenceSize);     // a and c found both between them
        Assert.Equal(1d, a.PooledRecall);                // a matched the one the others had
        Assert.Equal(0.5, b.PooledRecall);               // b matched one of the two
    }

    [Fact]
    public void STAR_A_Tool_Is_Never_Credited_For_A_Defect_Only_IT_Found()
    {
        // ★ The same rule from the other side: a defect nobody else reported is not in this tool's reference,
        // so it can neither help nor hurt its recall. It shows up as unique contribution instead.
        var summary = PooledRecall.Compute(
        [
            F("a", "repo", 10), F("b", "repo", 10), F("c", "repo", 10),
            F("a", "repo", 500),
        ]);

        var a = summary.Tools.Single(t => t.Tool == "a");
        Assert.Equal(1, a.LeaveOneOutReferenceSize);
        Assert.Equal(1, a.UniqueContribution);
    }

    // ── The N ≥ 3 floor ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void STAR_TWO_Tools_Is_Refused_With_A_Stated_Reason()
    {
        // ★★ At N=2 the reference is literally the other tool's findings, so this figure is pairwise agreement
        // and a tool looks deep by being SIMILAR. It computed at 2 before this item, which made the number
        // available exactly when it meant something else.
        var summary = PooledRecall.Compute([F("a", "repo", 10), F("b", "repo", 10)]);

        Assert.All(summary.Tools, t =>
        {
            Assert.Null(t.PooledRecall);
            Assert.NotNull(t.PooledRecallUnavailable);
            Assert.Contains("three", t.PooledRecallUnavailable!, StringComparison.OrdinalIgnoreCase);
        });

        Assert.False(summary.PooledRecallAvailable);
        Assert.Equal(PooledRecall.MinimumTools, 3);
    }

    [Fact]
    public void ONE_Tool_Is_Refused_Too_And_Precision_Still_Publishes()
    {
        // ★ Precision needs no pool: it is this tool's valid share of its own findings. Withholding it along
        // with recall would leave a single participant with nothing measured at all.
        var summary = PooledRecall.Compute([F("a", "repo", 10), F("a", "repo", 50, valid: false)]);

        var a = summary.Tools.Single();
        Assert.Null(a.PooledRecall);
        Assert.Equal(0.5, a.Precision);
    }

    [Fact]
    public void THREE_Tools_Computes()
    {
        var summary = PooledRecall.Compute(
            [F("a", "repo", 10), F("b", "repo", 10), F("c", "repo", 10)]);

        Assert.True(summary.PooledRecallAvailable);
        Assert.All(summary.Tools, t => Assert.NotNull(t.PooledRecall));
    }

    // ── Scope and labelling ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void STAR_A_Finding_With_A_Real_FIX_PAIR_Oracle_Leaves_The_Pool()
    {
        // ★★ A before/after commit is a better oracle than any pool — it is evidence rather than agreement —
        // so pooling those findings would dilute a real oracle with a pseudo one and publish the blend under
        // the stronger name. They are excluded and COUNTED, so the scope is visible rather than assumed.
        var summary = PooledRecall.Compute(
        [
            F("a", "repo", 10), F("b", "repo", 10), F("c", "repo", 10),
            F("a", "repo", 400, fixPair: true),
            F("b", "repo", 400, fixPair: true),
            F("c", "repo", 400, fixPair: true),
        ]);

        Assert.Equal(1, summary.UnionSize);
        Assert.Equal(3, summary.ExcludedWithFixPairOracle);
        Assert.Contains("fix pair", summary.Scope, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void STAR_It_Says_It_Is_A_PSEUDO_ORACLE()
    {
        // ★ The label is the difference between "our recall is 62 %" and "62 % of what this pool found". A
        // reader who takes the first reading has been misled by the name alone.
        var summary = PooledRecall.Compute(
            [F("a", "repo", 10), F("b", "repo", 10), F("c", "repo", 10)]);

        Assert.True(summary.PseudoOracle);
        Assert.Contains("OVERSTATES", summary.Caveat, StringComparison.Ordinal);
        Assert.Contains("union of what every OTHER", summary.Caveat, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Silent_Tool_Is_In_The_Table_With_Zero_Rather_Than_Missing()
    {
        // ★ A tool that submitted and reported nothing has a real recall of 0 against the others' union.
        // Dropping it would make "found nothing" indistinguishable from "never entered".
        var summary = PooledRecall.Compute(
            [F("a", "repo", 10), F("b", "repo", 10), F("c", "repo", 10)],
            silentTools: ["quiet"]);

        var quiet = summary.Tools.Single(t => t.Tool == "quiet");
        Assert.Equal(0, quiet.Reported);
        Assert.Equal(0d, quiet.PooledRecall);
        Assert.Null(quiet.Precision);
    }
}
