using System.Collections.Concurrent;
using System.Globalization;

namespace Cai.Web.Noise;

/// <summary>One finding a vendor reports against the published holdout.</summary>
/// <param name="ClaimClass">
/// ★ Required. A noise rate compares only tools making comparably falsifiable claims — "line 42
/// dereferences null" can be a false positive, "this file is a hotspot" cannot be in the same sense — so
/// a pooled rate across tools with different claim mixtures is a category error. Undeclared, the error
/// is made silently.
/// </param>
public sealed record SubmittedFinding(
    string RepoId, string PinnedSha, string? FilePath, int? Line,
    string RuleId, string? Title, string ClaimClass);

/// <summary>
/// A vendor's declaration of whether their tool has been developed against a holdout repository.
/// </summary>
/// <remarks>
/// ★ Only the vendor knows this, and the pristine-vs-recent gap is the OVERFITTING NUMBER — the most
/// interesting figure the standard produces, and one no vendor would publish about itself unprompted.
/// Required, therefore, rather than optional.
/// </remarks>
public sealed record RecencyDeclaration(string RepoId, string Stratum);

/// <summary>One threshold moved off its shipping value.</summary>
/// <param name="RuleId">Which rule.</param>
/// <param name="Shipped">What the product ships with. ★ Required: "we changed a threshold" is not checkable.</param>
/// <param name="Used">What this run used.</param>
public sealed record ThresholdChange(string? RuleId, string? Shipped, string? Used);

/// <summary>
/// How the tool was configured for this run — the hole every other check leaves open.
/// </summary>
/// <remarks>
/// <para>★★ VERIFICATION CONSTRAINS THE RUN, NOT THE CONFIGURATION. The submission is checked against the
/// holdout's repositories and shas; provenance names the build, the model set and the seed; the recency
/// declaration closes "did you develop against these repos". None of it says which rules were switched on. So
/// a vendor could run the correct version against the correct shas with its noisiest rules disabled, its
/// thresholds relaxed, or a profile no customer is ever given, and pass every check the method has.</para>
///
/// <para>★★ IT DOES NOT REQUIRE TRUSTING ANYBODY. Its value is the recency declaration's: it converts a vague
/// impression into a specific, checkable, PUBLIC claim that a competitor or a buyer can point at. Lying
/// becomes an act rather than an omission.</para>
///
/// <para>★ It binds us first — Watchdog is the only participant, so we are the first to state our
/// configuration and admit any divergence from what customers actually run.</para>
/// </remarks>
/// <param name="RulesetId">The ruleset or profile used. Required — a declaration naming nothing is not one.</param>
/// <param name="IsProductDefault">Whether this IS what customers get.</param>
/// <param name="DivergenceExplanation">Required when it is not: how it differs, in words that publish.</param>
/// <param name="RulesDisabled">Rules switched off relative to the shipping default.</param>
/// <param name="ThresholdsAltered">Thresholds moved, each with its shipped and used value.</param>
public sealed record RunConfiguration(
    string? RulesetId,
    bool IsProductDefault,
    string? DivergenceExplanation = null,
    IReadOnlyList<string>? RulesDisabled = null,
    IReadOnlyList<ThresholdChange>? ThresholdsAltered = null);

/// <summary>A run submitted against a published holdout.</summary>
/// <param name="RunStartedAt">
/// ★★ WHEN THE SCANNER RAN, and it must be AFTER the draw was published. That ordering is the neutrality
/// property the whole standard rests on: a holdout published after the results exist is worthless however it
/// was made, and a run that predates its own holdout was either run against something else or run against a
/// draw somebody saw early. Optional on the wire so existing submitters are not broken, but a submission that
/// omits it cannot be checked and says so.
/// </param>
public sealed record NoiseSubmission(
    string Period, string Tool, string ToolVersion,
    IReadOnlyList<RecencyDeclaration> Recency,
    IReadOnlyList<SubmittedFinding> Findings,
    DateTimeOffset? RunStartedAt = null,
    RunConfiguration? Configuration = null);

/// <summary>What CAI checked, and what it found.</summary>
public sealed record SubmissionReceipt(
    string SubmissionId, string Period, string Tool, string ToolVersion,
    DateTimeOffset ReceivedAt, bool Accepted,
    IReadOnlyList<string> Problems,
    int HoldoutRepositories, int CoveredRepositories, IReadOnlyList<string> Uncovered);

/// <summary>
/// The submission surface: validate a run against the published holdout, and keep the receipt.
/// </summary>
/// <remarks>
/// <para>★ CAI never runs anyone's scanner — a vendor runs their own tool on their own infrastructure,
/// so no credentials, access or licence to anybody's product is ever needed. What CAI does is VERIFY the
/// run covered the holdout that was published, at the shas that were published.</para>
/// <para>Every check below closes a route to a flattering number that nobody could see from outside.</para>
/// </remarks>
public static class NoiseSubmissions
{
    /// <summary>The claim classes a finding may declare.</summary>
    /// <remarks>
    /// ★★ DERIVED, not retyped. This was a hand-written string set duplicating the vocabulary the publication
    /// side now enforces, so a class added to one and not the other would let a finding be submitted under a
    /// label no published rate could account for — and nothing would have said so. Same drift as a hostname
    /// written down in a hundred files, with a measurement attached.
    /// </remarks>
    public static IReadOnlySet<string> ClaimClasses { get; } =
        Enum.GetValues<ClaimClass>()
            .Select(ClaimSpecificity.Wire)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ★★ THE REGISTER MOVED TO THE DATABASE — see <see cref="SqliteNoiseStore"/>.
    /// </summary>
    /// <remarks>
    /// It used to be two <c>ConcurrentDictionary</c> instances, and the comment sitting here said exactly what
    /// was wrong with that: "a restart currently forgets that a vendor already submitted, which is precisely
    /// the hole the no-withdrawal rule exists to close". That rule is the standard's answer to the worst
    /// failure available to it — a vendor runs, dislikes the result, and the published set quietly becomes
    /// "the results people were happy with" — and it was defeated by a process restart. It is now a partial
    /// UNIQUE index in SQLite, so the claim survives a restart AND cannot be lost to a race between two
    /// concurrent submissions.
    /// <para>What is left here is the VALIDATION, which is pure and belongs nowhere near a connection: does
    /// the run cover the published holdout, at the published shas, with a recency declaration and known claim
    /// classes, and did it start after the draw.</para>
    /// </remarks>
    public static SubmissionReceipt Accept(
        NoiseSubmission submission, IReadOnlyList<HoldoutCandidate> holdout, DateTimeOffset now,
        DateTimeOffset? drawPublishedAt = null)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(holdout);

        var problems = new List<string>();

        // ── The ordering that makes the draw mean anything ────────────────────────────────────────
        //
        // ★★ THE DRAW MUST PRECEDE THE RUN. This is the neutrality property everything else rests on: a
        // holdout published after the results exist is worthless however carefully it was made. It was
        // asserted in prose — "the draw is published, timestamped, BEFORE any scanner runs" — and checked
        // nowhere, so a submission could carry findings produced before the holdout it claims to answer and
        // nothing would notice.
        if (drawPublishedAt is { } drawnAt)
        {
            if (submission.RunStartedAt is not { } startedAt)
            {
                problems.Add(
                    "the submission does not say when the run started, so the standard cannot check that it "
                    + "began after the holdout was published — the ordering the whole draw rests on. Send "
                    + "runStartedAt.");
            }
            else if (startedAt < drawnAt)
            {
                problems.Add(
                    $"the run started at {startedAt:O}, BEFORE this period's holdout was published at "
                    + $"{drawnAt:O}. A result produced before its own draw was either run against something "
                    + "else or run against a draw seen early; either way it cannot answer this holdout.");
            }
        }
        var byRepo = holdout.ToDictionary(h => h.RepoId, StringComparer.OrdinalIgnoreCase);

        // ★ The recency declaration is required — see RecencyDeclaration.
        if (submission.Recency is null or { Count: 0 })
        {
            problems.Add(
                "a recency declaration is required: for each holdout repository, say whether the tool "
                + "has been developed against it. The pristine-vs-recent gap is the overfitting number.");
        }

        // ★★ And the STRATUM VALUES are checked, not merely their presence. Unvalidated, a vendor could
        // declare "quite-fresh" here, be accepted, and then find the publication endpoint refusing a
        // vocabulary the submission endpoint had waved through — the standard disagreeing with itself
        // across two of its own doors. Same vocabulary on both sides, from RecencyStrata.
        foreach (var declaration in submission.Recency ?? [])
        {
            if (RecencyStrata.ParseOrNull(declaration.Stratum) is null)
            {
                problems.Add(
                    $"'{declaration.Stratum}' is not a recency stratum. One of: "
                    + string.Join(", ", Enum.GetValues<RecencyStratum>().Select(RecencyStrata.Wire)) + ".");
            }
        }

        // ── How the tool was configured ───────────────────────────────────────────────────────────
        //
        // ★★ THE ONE THING NO OTHER CHECK CONSTRAINS. See RunConfiguration: the right version against the
        // right shas with the noisiest rules off passes everything else in the method.
        if (submission.Configuration is not { } config)
        {
            problems.Add(
                "a configuration declaration is required: the ruleset or profile used, any rules disabled or "
                + "thresholds altered from the shipping default, and whether that configuration IS the "
                + "product default. Every other check constrains the RUN; none of them constrains how the "
                + "tool was set up.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(config.RulesetId))
            {
                problems.Add(
                    "the configuration declaration needs a rulesetId — a declaration that names nothing is "
                    + "not something a third party could ask to see.");
            }

            var disabled = config.RulesDisabled ?? [];
            var altered = config.ThresholdsAltered ?? [];

            // ★★ The likeliest dishonest filling-in is not a lie but a CONTRADICTION: tick "this is what
            // customers get" and list what you turned off. Refusing it costs an honest vendor one sentence
            // and costs a dishonest one the whole manoeuvre.
            if (config.IsProductDefault && (disabled.Count > 0 || altered.Count > 0))
            {
                var changed = disabled.Concat(altered.Select(a => a.RuleId ?? "an unnamed rule"));
                problems.Add(
                    "this cannot be the product default: the declaration also lists changes to it — "
                    + string.Join(", ", changed)
                    + ". Either it is what customers get, or it diverges and says how.");
            }

            // ★ A divergence is allowed; a silent one is not. "Not the default" plus silence tells a reader
            // nothing while looking like a disclosure.
            if (!config.IsProductDefault && string.IsNullOrWhiteSpace(config.DivergenceExplanation))
            {
                problems.Add(
                    "this configuration is declared as NOT the product default, so it must say how it "
                    + "differs. The explanation publishes with the number.");
            }

            foreach (var change in altered)
            {
                if (string.IsNullOrWhiteSpace(change.RuleId)
                    || string.IsNullOrWhiteSpace(change.Shipped)
                    || string.IsNullOrWhiteSpace(change.Used))
                {
                    problems.Add(
                        $"the altered threshold on '{change.RuleId ?? "an unnamed rule"}' must state what was "
                        + "shipped and what was used. \"We changed a threshold\" is not a checkable claim.");
                }
            }
        }

        foreach (var f in submission.Findings ?? [])
        {
            // ★ Outside the holdout: otherwise a vendor reports over code of their own choosing.
            if (!byRepo.TryGetValue(f.RepoId, out var candidate))
            {
                problems.Add(
                    $"finding on '{f.RepoId}', which is not in the published holdout for "
                    + $"{submission.Period}. A run is measured over the drawn repositories and no others.");
                continue;
            }

            // ★ Wrong sha: "the same code" means nothing across two runs unless the revision matches.
            if (!string.Equals(f.PinnedSha, candidate.PinnedSha, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"finding on '{f.RepoId}' cites sha '{Short(f.PinnedSha)}' where the holdout pins "
                    + $"'{Short(candidate.PinnedSha)}' — a different revision is different code.");
            }

            if (string.IsNullOrWhiteSpace(f.ClaimClass))
            {
                problems.Add($"finding on '{f.RepoId}' declares no claim class ({Classes()}).");
            }
            else if (!ClaimClasses.Contains(f.ClaimClass))
            {
                problems.Add($"finding on '{f.RepoId}' declares claim class '{f.ClaimClass}', which is not one of {Classes()}.");
            }
        }

        // ★ Coverage is REPORTED whether or not it is complete. Scanning three of twelve and reporting a
        // rate over them is the most obvious route to a flattering number, and it is invisible unless
        // coverage publishes — so "zero uncovered" is a stated fact rather than the absence of a
        // complaint.
        var covered = (submission.Findings ?? [])
            .Select(f => f.RepoId)
            .Where(byRepo.ContainsKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uncovered = holdout
            .Where(h => !covered.Contains(h.RepoId))
            .Select(h => h.RepoId)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var receipt = new SubmissionReceipt(
            SubmissionId: Guid.CreateVersion7().ToString("n"),
            Period: submission.Period,
            Tool: submission.Tool,
            ToolVersion: submission.ToolVersion,
            ReceivedAt: now,
            Accepted: problems.Count == 0,
            Problems: problems,
            HoldoutRepositories: holdout.Count,
            CoveredRepositories: covered.Count,
            Uncovered: uncovered);

        // ★ Persistence is the CALLER's, via INoiseStore. This method decides only whether the run
        // answers the holdout it names; storing it is where the no-withdrawal claim is enforced.
        return receipt;
    }

    private static string Key(string tool, string period) =>
        string.Create(CultureInfo.InvariantCulture, $"{tool} {period}");

    private static string Classes() => string.Join(", ", ClaimClasses.OrderBy(c => c, StringComparer.Ordinal));

    private static string Short(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? "(none)" : sha[..Math.Min(12, sha.Length)];
}
