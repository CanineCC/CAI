using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The finding count: what the run reported, against what was submitted.
/// </summary>
/// <remarks>
/// <para>★★ THE SIMPLEST ROUTE TO A FLATTERING RATE, AND THE ONE NOTHING COULD SEE. Every existing check
/// constrains the findings that ARRIVE — right repositories, right shas, known claim classes, a run that
/// started after the draw. None of them constrains what was left out. A vendor whose run produced 400 findings
/// can submit the 120 it likes, and coverage still reads "every repository covered", because a repository with
/// one surviving finding is covered.</para>
///
/// <para>★★ SO THE RUN'S OWN COUNT IS DECLARED, AND COMPARED. It converts a silent omission into a specific
/// number a third party can ask about — the same move the recency and configuration declarations make. It does
/// not require trusting the number: it requires stating it.</para>
///
/// <para>★ An ABSENT count is a problem, not a pass. Optional, the check would be present for the honest and
/// absent for everyone else, which is worse than not having it — the receipt would say "verified" over a gate
/// that never ran. Same discipline <c>runStartedAt</c> already gets.</para>
/// </remarks>
public sealed class FindingCountVerificationTests
{
    private static readonly DateTimeOffset Drawn = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ran = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<HoldoutCandidate> Holdout() =>
        HoldoutSampler.Draw(NoiseCorpus.Draws["2026-09"].Seed, NoiseCorpus.Candidates, NoiseCorpus.Rules);

    private static RunConfiguration Config() =>
        new("watchdog-default-2026.08", IsProductDefault: true);

    /// <summary>A submission that passes every OTHER check, so a failure here is about the count alone.</summary>
    private static NoiseSubmission Submission(int? reportedCount, int findingsToSend = int.MaxValue)
    {
        var holdout = Holdout();
        var findings = holdout
            .Take(Math.Min(findingsToSend, holdout.Count))
            .Select(h => new SubmittedFinding(
                h.RepoId, h.PinnedSha, "src/Thing.cs", 42, "D4", "a finding", "pointwise"))
            .ToList();

        return new NoiseSubmission(
            Period: "2026-09",
            Tool: "probe",
            ToolVersion: "engine-8b08d6c6",
            Recency: [.. holdout.Select(h => new RecencyDeclaration(h.RepoId, "never-trained"))],
            Findings: findings,
            RunStartedAt: Ran,
            Configuration: Config(),
            ReportedFindingCount: reportedCount);
    }

    private static SubmissionReceipt Accept(NoiseSubmission submission) =>
        NoiseSubmissions.Accept(submission, Holdout(), Now, Drawn);

    [Fact]
    public void A_count_that_matches_what_was_sent_is_no_problem()
    {
        var holdout = Holdout();
        var receipt = Accept(Submission(reportedCount: holdout.Count));

        Assert.DoesNotContain(receipt.Problems, p =>
            p.Contains("finding count", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(holdout.Count, receipt.ReportedFindingCount);
        Assert.Equal(holdout.Count, receipt.SubmittedFindingCount);
    }

    [Fact]
    public void STAR_A_Run_That_Reported_More_Than_It_Submitted_Is_Refused()
    {
        // ★★ THE FAILURE THE CHECK EXISTS FOR. 400 findings produced, 12 submitted: every other check passes,
        // coverage reads complete, and the rate is taken over the twelve the vendor liked.
        var receipt = Accept(Submission(reportedCount: 400, findingsToSend: 12));

        Assert.False(receipt.Accepted);
        var problem = Assert.Single(receipt.Problems, p =>
            p.Contains("finding count", StringComparison.OrdinalIgnoreCase));

        // ★ BOTH NUMBERS in the message. "The counts disagree" is a message nobody can act on.
        Assert.Contains("400", problem, StringComparison.Ordinal);
        Assert.Contains("12", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void STAR_Submitting_MORE_Than_The_Run_Reported_Is_Also_Refused()
    {
        // ★★ The other direction is not harmless. Findings the run never produced mean the payload was
        // assembled somewhere other than the run, and a rate over an assembled set measures the assembler.
        var holdout = Holdout();
        var receipt = Accept(Submission(reportedCount: 1));

        Assert.False(receipt.Accepted);
        Assert.Contains(receipt.Problems, p =>
            p.Contains("finding count", StringComparison.OrdinalIgnoreCase)
            && p.Contains(holdout.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
    }

    [Fact]
    public void STAR_An_Absent_Count_Is_A_Problem_Not_A_Pass()
    {
        // ★★ Optional, the check would be present for the honest and absent for everybody else — and the
        // receipt would say accepted over a gate that never ran, which is worse than not having the gate.
        var receipt = Accept(Submission(reportedCount: null));

        Assert.False(receipt.Accepted);
        Assert.Contains(receipt.Problems, p =>
            p.Contains("reportedFindingCount", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Negative_Count_Is_Refused_As_Nonsense()
    {
        var receipt = Accept(Submission(reportedCount: -1));

        Assert.False(receipt.Accepted);
        Assert.Contains(receipt.Problems, p =>
            p.Contains("finding count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void STAR_A_Run_That_Legitimately_Found_NOTHING_Is_Accepted()
    {
        // ★★ Zero of zero is a real result and must not be forced into a problem. A tool that reported nothing
        // on the holdout has an honest 0 % — the counts agree, and the coverage report is what says the
        // repositories went unexamined. Conflating "found nothing" with "sent nothing" would make the honest
        // zero unreportable.
        var holdout = Holdout();
        var receipt = NoiseSubmissions.Accept(
            new NoiseSubmission(
                Period: "2026-09", Tool: "probe", ToolVersion: "engine-8b08d6c6",
                Recency: [.. holdout.Select(h => new RecencyDeclaration(h.RepoId, "never-trained"))],
                Findings: [],
                RunStartedAt: Ran,
                Configuration: Config(),
                ReportedFindingCount: 0),
            holdout, Now, Drawn);

        Assert.DoesNotContain(receipt.Problems, p =>
            p.Contains("finding count", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, receipt.SubmittedFindingCount);

        // ★ And the coverage report is what carries the fact, rather than the count check swallowing it.
        Assert.Equal(holdout.Count, receipt.Uncovered.Count);
    }
}

/// <summary>
/// What the receipt and the method document say about the count check.
/// </summary>
/// <remarks>
/// ★★ A GATE THAT FIRES AND TELLS NOBODY IS NOT A GATE. The check above is worthless to a reader who cannot
/// see its inputs or learn that it exists — and "verified" is the only thing CAI does, so a submission that
/// passed the easy checks and skipped the hard ones read exactly like one that passed them all.
/// </remarks>
public sealed class FindingCountPublicationTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task STAR_The_Method_Enumerates_What_Verification_Checks()
    {
        using var client = fx.Client();
        var method = JsonDocument.Parse(await client.GetStringAsync("/api/noise/method", Ct)).RootElement;

        var checks = method.GetProperty("verificationChecks").EnumerateArray()
            .Select(c => c.GetProperty("check").GetString())
            .ToList();

        // ★★ The count check by name, beside the ones that already existed — otherwise a reader has no way
        // to tell which verification a receipt's "accepted" is the result of.
        Assert.Contains("finding-count", checks);
        Assert.Contains("run-ordering", checks);
        Assert.Contains("pinned-sha", checks);

        // ★ Each one says what it ASKS, in a sentence. A list of slugs is not a published method.
        foreach (var c in method.GetProperty("verificationChecks").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("asks").GetString()));
        }
    }

    [Fact]
    public async Task STAR_The_Receipt_Publishes_BOTH_Counts_Even_When_They_Agree()
    {
        using var client = fx.Client();
        var holdout = JsonDocument.Parse(await client.GetStringAsync("/api/noise/holdout/2026-09", Ct))
            .RootElement.GetProperty("repositories").EnumerateArray()
            .Select(r => (Repo: r.GetProperty("repoId").GetString()!, Sha: r.GetProperty("pinnedSha").GetString()!))
            .ToList();

        var response = await client.PostAsJsonAsync("/api/noise/submissions", new
        {
            period = "2026-09",
            tool = "probe-count-publication",
            toolVersion = "engine-8b08d6c6",
            runStartedAt = "2026-08-20T09:00:00Z",
            configuration = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
            recency = holdout.Select(h => new { repoId = h.Repo, stratum = "never-trained" }),
            findings = holdout.Select(h => new
            {
                repoId = h.Repo, pinnedSha = h.Sha, filePath = "src/Thing.cs", line = 42,
                ruleId = "D4", title = "a finding", claimClass = "pointwise",
            }),
            reportedFindingCount = holdout.Count,
        }, Ct);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        var counts = body.GetProperty("findingCount");

        // ★ Published even in agreement: a check whose inputs are invisible cannot be re-derived by the
        // reader it exists for.
        Assert.Equal(holdout.Count, counts.GetProperty("reportedByRun").GetInt32());
        Assert.Equal(holdout.Count, counts.GetProperty("submitted").GetInt32());
        Assert.True(counts.GetProperty("agrees").GetBoolean());
    }

    [Fact]
    public async Task STAR_A_MISMATCH_Publishes_The_TWO_ACTUAL_Numbers()
    {
        // ★★ THE CASE THE AGREEING TEST CANNOT PROVE. A renderer that echoed the declared count into both
        // fields and hard-coded agrees:true passed the happy-path assertion above unchanged — the two values
        // are identical there — so the mismatch is where the receipt earns its place. Found by mutating the
        // renderer and watching nothing fail.
        using var client = fx.Client();
        var holdout = JsonDocument.Parse(await client.GetStringAsync("/api/noise/holdout/2026-09", Ct))
            .RootElement.GetProperty("repositories").EnumerateArray()
            .Select(r => (Repo: r.GetProperty("repoId").GetString()!, Sha: r.GetProperty("pinnedSha").GetString()!))
            .ToList();

        var response = await client.PostAsJsonAsync("/api/noise/submissions", new
        {
            period = "2026-09",
            tool = "probe-count-mismatch",
            toolVersion = "engine-8b08d6c6",
            runStartedAt = "2026-08-20T09:00:00Z",
            configuration = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
            recency = holdout.Select(h => new { repoId = h.Repo, stratum = "never-trained" }),
            findings = holdout.Take(2).Select(h => new
            {
                repoId = h.Repo, pinnedSha = h.Sha, filePath = "src/Thing.cs", line = 42,
                ruleId = "D4", title = "a finding", claimClass = "pointwise",
            }),
            reportedFindingCount = 400,
        }, Ct);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        var counts = body.GetProperty("findingCount");

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Equal(400, counts.GetProperty("reportedByRun").GetInt32());
        Assert.Equal(2, counts.GetProperty("submitted").GetInt32());
        Assert.False(counts.GetProperty("agrees").GetBoolean());
    }
}
