using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The counterweight to the noise rate.
/// </summary>
/// <remarks>
/// <para>★★ A NOISE RATE MEASURES PRECISION AND NOTHING ELSE, and precision alone rewards under-firing:
/// a tool that reports one finding it is certain about scores a perfect 0% noise, and a tool that reports
/// everything worth knowing alongside some mistakes scores worse. Published on its own it is an incentive
/// to say less, which is the opposite of what anyone buys a scanner for.</para>
/// <para>Recall on real repositories has no ground truth, so the standard uses the POOLED reference: the
/// union of what every participating tool reported and a human adjudicated as valid. It is honest only if
/// labelled — a defect that no participant found is invisible to the pool, so pooled recall always
/// overstates, and it cannot be quoted as recall.</para>
/// </remarks>
public sealed class PooledRecallTests
{
    private static PooledFinding F(
        string tool, string repo, string file, int line, bool valid = true) =>
        new(tool, repo, file, line, valid);

    // ── When there is no pool ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ ONE TOOL IS NOT A POOL. Its recall against a union it alone defines is 100% by construction —
    /// a number that says nothing and reads as everything. Null, with the reason.
    /// </summary>
    [Fact]
    public void STAR_a_single_tool_publishes_no_pooled_recall()
    {
        var pooled = PooledRecall.Compute([F("solo", "acme/x", "a.cs", 10), F("solo", "acme/x", "b.cs", 20)]);

        var solo = pooled.Tools.Single();
        Assert.Null(solo.PooledRecall);
        Assert.Equal(1, pooled.ParticipatingTools);
    }

    [Fact]
    public void STAR_Two_Tools_Do_NOT_Make_A_Pool()
    {
        // ★★ REVERSED BY #2, deliberately. This test used to assert that two tools compute a recall figure —
        // 0.5 and 1.0 — and those numbers were pairwise agreement wearing recall's name: at N=2 each tool's
        // leave-one-out reference IS the other tool's findings, so a tool scores well by being SIMILAR. The
        // union is still built and published; only the figure that would be misread is withheld.
        var pooled = PooledRecall.Compute(
            [F("a", "acme/x", "a.cs", 10), F("b", "acme/x", "a.cs", 10), F("b", "acme/x", "b.cs", 20)]);

        Assert.Equal(2, pooled.ParticipatingTools);
        Assert.Equal(2, pooled.UnionSize);
        Assert.False(pooled.PooledRecallAvailable);
        Assert.All(pooled.Tools, t => Assert.Null(t.PooledRecall));
        Assert.All(pooled.Tools, t => Assert.NotNull(t.PooledRecallUnavailable));
    }

    // ── What is in the union ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ Only findings adjudicated VALID enter the union. Noise in the reference set would reward a tool
    /// for reproducing another tool's mistakes, which is precisely the wrong incentive.
    /// </summary>
    [Fact]
    public void STAR_noise_does_not_enter_the_union()
    {
        // ★ Three tools since #2 — the recall assertion needs a pool that qualifies. What is under test is
        // unchanged: the junk finding must not become a defect anybody can be scored against.
        var pooled = PooledRecall.Compute(
            [
                F("a", "acme/x", "a.cs", 10, valid: true),
                F("a", "acme/x", "junk.cs", 99, valid: false),
                F("b", "acme/x", "a.cs", 10, valid: true),
                F("c", "acme/x", "a.cs", 10, valid: true),
            ]);

        Assert.Equal(1, pooled.UnionSize);
        Assert.Equal(1.0, pooled.Tools.Single(t => t.Tool == "b").PooledRecall!.Value, 3);
    }

    /// <summary>
    /// ★ Two tools pointing at the same defect rarely agree to the line. The window is declared, applied,
    /// and published — an undeclared tolerance is a knob whoever computes the number can turn.
    /// </summary>
    [Fact]
    public void STAR_findings_within_the_declared_window_are_one_defect()
    {
        var pooled = PooledRecall.Compute(
            [F("a", "acme/x", "a.cs", 10), F("b", "acme/x", "a.cs", 12)], lineWindow: 3);

        Assert.Equal(1, pooled.UnionSize);
        Assert.Equal(3, pooled.LineWindow);
    }

    [Fact]
    public void Findings_beyond_the_window_are_different_defects()
    {
        var pooled = PooledRecall.Compute(
            [F("a", "acme/x", "a.cs", 10), F("b", "acme/x", "a.cs", 40)], lineWindow: 3);

        Assert.Equal(2, pooled.UnionSize);
    }

    [Fact]
    public void The_same_line_in_a_different_file_is_a_different_defect()
    {
        var pooled = PooledRecall.Compute(
            [F("a", "acme/x", "a.cs", 10), F("b", "acme/x", "b.cs", 10)], lineWindow: 3);

        Assert.Equal(2, pooled.UnionSize);
    }

    // ── What under-firing looks like ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ THE WHOLE POINT. A tool reporting one certain finding has perfect precision and dreadful
    /// recall; a tool reporting everything has middling precision and full recall. Published together the
    /// trade-off is visible, and neither strategy wins by being quiet.
    /// </summary>
    [Fact]
    public void STAR_the_quiet_tool_has_perfect_precision_and_poor_recall()
    {
        // ★ A THIRD tool since #2, mirroring `loud`, so the pool qualifies and the leave-one-out references
        // are still the ten defects. Without it this asserted a two-tool figure that no longer publishes.
        List<PooledFinding> findings =
        [
            F("quiet", "acme/x", "a.cs", 10, valid: true),
            .. Enumerable.Range(0, 9).Select(i => F("loud", "acme/x", $"f{i}.cs", 10, valid: true)),
            F("loud", "acme/x", "a.cs", 10, valid: true),
            .. Enumerable.Range(0, 5).Select(i => F("loud", "acme/x", $"n{i}.cs", 10, valid: false)),
            .. Enumerable.Range(0, 9).Select(i => F("echo", "acme/x", $"f{i}.cs", 10, valid: true)),
            F("echo", "acme/x", "a.cs", 10, valid: true),
        ];

        var pooled = PooledRecall.Compute(findings);
        var quiet = pooled.Tools.Single(t => t.Tool == "quiet");
        var loud = pooled.Tools.Single(t => t.Tool == "loud");

        Assert.Equal(1.0, quiet.Precision!.Value, 3);

        // ★★ Ten defects, all of them found by somebody other than `quiet`, and `quiet` found one: 0.1. The
        // leave-one-out reference happens to be the whole union here because nothing is quiet's alone.
        Assert.Equal(10, quiet.LeaveOneOutReferenceSize);
        Assert.Equal(0.1, quiet.PooledRecall!.Value, 3);

        Assert.Equal(10.0 / 15, loud.Precision!.Value, 3);
        Assert.Equal(1.0, loud.PooledRecall!.Value, 3);
    }

    /// <summary>
    /// ★★ A tool that reported NOTHING has undefined precision, not perfect precision. Zero over zero is
    /// not 100%, and "we found no noise" from a tool that found nothing is the flattering reading the
    /// whole metric has to be defended against.
    /// </summary>
    [Fact]
    public void STAR_a_tool_that_reported_nothing_has_no_precision_and_zero_recall()
    {
        var pooled = PooledRecall.Compute(
            [F("a", "acme/x", "a.cs", 10), F("b", "acme/x", "b.cs", 20)],
            silentTools: ["ghost"]);

        var ghost = pooled.Tools.Single(t => t.Tool == "ghost");
        Assert.Equal(0, ghost.Reported);
        Assert.Null(ghost.Precision);
        Assert.Equal(0.0, ghost.PooledRecall!.Value, 3);
    }

    /// <summary>
    /// ★ How much of the union only this tool found. A tool with mediocre pooled recall that contributes
    /// defects nobody else sees is worth more than the number alone suggests — and it is the figure that
    /// shows the pool is not just one tool's opinion echoed.
    /// </summary>
    [Fact]
    public void STAR_unique_contribution_is_published()
    {
        var pooled = PooledRecall.Compute(
            [
                F("a", "acme/x", "shared.cs", 10), F("b", "acme/x", "shared.cs", 10),
                F("a", "acme/x", "only-a.cs", 10),
            ]);

        Assert.Equal(1, pooled.Tools.Single(t => t.Tool == "a").UniqueContribution);
        Assert.Equal(0, pooled.Tools.Single(t => t.Tool == "b").UniqueContribution);
    }

    /// <summary>
    /// ★★ The label travels with the number. "Recall" would be a claim about defects that exist;
    /// this is a claim about defects somebody found, and the two differ by exactly the blind spot every
    /// participating tool shares.
    /// </summary>
    [Fact]
    public void STAR_the_summary_states_that_pooled_recall_overstates()
    {
        var pooled = PooledRecall.Compute([F("a", "acme/x", "a.cs", 10), F("b", "acme/x", "b.cs", 20)]);

        Assert.Contains("overstate", pooled.Caveat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            typeof(PooledToolResult).GetProperties(),
            p => p.Name.Equals("Recall", StringComparison.Ordinal));
    }
}
