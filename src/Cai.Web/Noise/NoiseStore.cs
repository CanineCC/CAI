using System.Text.Json;
using Cai.Web.Registry;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Cai.Web.Noise;

/// <summary>One judge's raw verdict on one finding, as it will be published.</summary>
/// <param name="Period">The measurement period.</param>
/// <param name="FindingId">Which finding — the join a reader follows to argue with a verdict.</param>
/// <param name="Round">1 for the first pair, 2 for the blind pair.</param>
/// <param name="Judge">The judge slot, e.g. <c>judge-a</c>.</param>
/// <param name="Model">The model that answered.</param>
/// <param name="ModelVersion">Its pinned version. ★ Without it the run cannot be re-derived at all.</param>
/// <param name="PromptId">The prompt used, whose full text is published beside the record.</param>
/// <param name="Verdict">The verdict, in the published vocabulary.</param>
/// <param name="Reasoning">Why. ★★ A verdict a reader cannot argue with is not open judging.</param>
/// <param name="ModelFamily">
/// ★★ The training tradition, not the product name. 02 §2: "a blind spot lives in the weights; no rephrasing
/// removes it — a single-family ensemble cannot see a single-family blind spot." Four different models from one
/// vendor is still one family, and the check that they agreed says nothing about all four being wrong the same way.
/// </param>
/// <param name="Temperature">
/// ★★ Must be 0. 01 §3 promises "anyone may re-run the judges and get the same answers"; a verdict produced at 0.7
/// cannot be re-run to the same answer, so the promise is false for it — and without this field a reader could not
/// tell which verdicts it was false for.
/// </param>
public sealed record VerdictRecord(
    string Period, string FindingId, int Round,
    string Judge, string Model, string ModelVersion, string PromptId,
    string Verdict, string Reasoning, DateTimeOffset RecordedAt,
    string ModelFamily = "", double Temperature = 0);

/// <summary>How one finding's cascade settled.</summary>
public sealed record ResolutionRecord(
    string Period, string FindingId, string State, string? Verdict, int? SettledAtRound,
    bool ActionabilityContested, bool? Actionable, string Reason, DateTimeOffset RecordedAt);

/// <summary>One verdict from the independent second pass over a period's re-judge sample.</summary>
/// <remarks>
/// ★ Carries the same provenance a first-pass verdict does. A reproducibility claim whose second pass cannot be
/// read is worth what the first pass's would be without its reasoning: nothing a reader can argue with.
/// </remarks>
public sealed record RejudgeRecord(
    string Period, string FindingId, string Verdict,
    string Model, string ModelVersion, string PromptId, string Reasoning, DateTimeOffset RecordedAt);

/// <summary>A contested verdict, and how the contest was answered.</summary>
/// <param name="Reason">
/// ★★ Required. "I disagree" is not contestation — 01 §5 is about arguing against published reasoning, and the
/// reason is the half that makes the dispute answerable rather than a vote.
/// </param>
/// <param name="Outcome">
/// <c>upheld</c> or <c>overturned</c>, or null while it is open. ★ An OPEN dispute is the state a reader most
/// needs: it is the one where the standard has been challenged and has not answered.
/// </param>
/// <param name="ResolutionReasoning">
/// Why it was answered that way. ★ Required in BOTH directions: an outcome without reasoning is "the standard
/// says so", the exact argument 01 §5 says CAI does not get to make.
/// </param>
public sealed record DisputeRecord(
    string DisputeId, string Period, string FindingId, string RaisedBy, string Reason, DateTimeOffset RaisedAt,
    string? Outcome, string? ResolutionReasoning, DateTimeOffset? ResolvedAt);

/// <summary>A vendor's declared intent to submit for a period, registered before that period was drawn.</summary>
/// <remarks>
/// ★★ THE "BEFORE" THE NO-WITHDRAWAL RULE WORKS FROM. Without it the rule is half a rule: a vendor can simply
/// never submit the periods that went badly, and the published set quietly becomes "the results people were happy
/// with". The register's value is its TIMESTAMP, so a second registration keeps the first.
/// </remarks>
public sealed record IntentRecord(string Period, string Tool, DateTimeOffset RegisteredAt);

/// <summary>A judge prompt, stored once and published in full.</summary>
public sealed record PromptRecord(string PromptId, string Text, DateTimeOffset FirstSeenAt);

/// <summary>
/// Durable storage for the two things the standard promises to keep: who submitted, and how it was judged.
/// </summary>
public interface INoiseStore
{
    /// <summary>Record a receipt. Returns false when an accepted submission already claims (tool, period).</summary>
    /// <param name="configurationJson">
    /// The configuration declaration as submitted. ★ Stored VERBATIM rather than re-serialised from a parsed
    /// shape: it is a claim the vendor made, and the record's job is to publish what they said.
    /// </param>
    bool TryRecordSubmission(
        SubmissionReceipt receipt, DateTimeOffset? runStartedAt, string? configurationJson);

    /// <summary>A receipt by id, or null.</summary>
    SubmissionReceipt? FindSubmission(string submissionId);

    /// <summary>Whether an ACCEPTED submission already exists for this tool and period.</summary>
    bool AlreadySubmitted(string tool, string period);

    /// <summary>Every submission for a period, newest first — the register.</summary>
    IReadOnlyList<SubmissionReceipt> ListSubmissions(string period);

    /// <summary>The configuration a submission declared, as raw JSON, or null when it declared none.</summary>
    string? ConfigurationJson(string submissionId);

    /// <summary>Record one judge's raw verdict.</summary>
    void RecordVerdict(VerdictRecord verdict);

    /// <summary>Record how a finding settled, replacing any earlier resolution of the same finding.</summary>
    void RecordResolution(ResolutionRecord resolution);

    /// <summary>Register a prompt's full text under its id, if not already known.</summary>
    void RegisterPrompt(string promptId, string text, DateTimeOffset now);

    /// <summary>Every raw verdict for a period, in the order recorded.</summary>
    IReadOnlyList<VerdictRecord> ListVerdicts(string period);

    /// <summary>Every resolution for a period.</summary>
    IReadOnlyList<ResolutionRecord> ListResolutions(string period);

    /// <summary>The prompts referenced by a period's verdicts, in full.</summary>
    IReadOnlyList<PromptRecord> ListPrompts(string period);

    /// <summary>
    /// Store an accepted publication for a period. APPEND-ONLY: a correction is a second row.
    /// </summary>
    /// <param name="payloadJson">The published body verbatim — what was published, not a re-derivation.</param>
    void RecordPublication(string period, string payloadJson, DateTimeOffset publishedAt);

    /// <summary>The latest published payload for a period plus its history, or null when none exists.</summary>
    (string PayloadJson, DateTimeOffset PublishedAt, IReadOnlyList<DateTimeOffset> History)? LatestPublication(
        string period);

    /// <summary>Periods that have a published result, newest first — what a reader can ask for.</summary>
    IReadOnlyList<string> PublishedPeriods();

    /// <summary>
    /// Every published period's judged and noise counts, oldest first, for the rolling figure.
    /// </summary>
    /// <remarks>
    /// ★★ READ FROM THE STORED PAYLOADS, so the rolling figure is pooled from what was actually PUBLISHED rather
    /// than from a running total kept beside it. A counter would drift from the publications the moment a
    /// correction landed, and this is the one figure whose whole value is that it aggregates the record.
    /// <para>★ A corrected period appears twice, later row last — <see cref="RollingFigure"/> takes the latest.</para>
    /// </remarks>
    IReadOnlyList<PeriodTally> PublishedTallies();

    /// <summary>Record the independent second pass over a period's re-judge sample.</summary>
    /// <remarks>
    /// ★ Replaces any earlier pass for the same (period, findingId): a second pass IS the re-judge, and keeping
    /// several would let whoever ran them choose which counted — the steerable sample again, one level up.
    /// </remarks>
    void RecordRejudge(IReadOnlyList<RejudgeRecord> verdicts);

    /// <summary>Every re-judge verdict recorded for a period.</summary>
    IReadOnlyList<RejudgeRecord> ListRejudge(string period);

    /// <summary>Record a raised dispute.</summary>
    void RaiseDispute(DisputeRecord dispute);

    /// <summary>One dispute by id, or null.</summary>
    DisputeRecord? FindDispute(string disputeId);

    /// <summary>
    /// Answer an open dispute. Returns false when it is already answered.
    /// </summary>
    /// <remarks>
    /// ★★ ONCE. Otherwise the outcome is whatever was written last, and "publishes either way" becomes
    /// "publishes whichever way we ended up preferring". Enforced in SQL by the <c>outcome IS NULL</c> predicate
    /// rather than by a read-then-write above it.
    /// </remarks>
    bool ResolveDispute(string disputeId, string outcome, string reasoning, DateTimeOffset resolvedAt);

    /// <summary>Every dispute for a period, oldest first.</summary>
    IReadOnlyList<DisputeRecord> ListDisputes(string period);

    /// <summary>
    /// Register intent, or keep the existing registration. Returns the record that now stands.
    /// </summary>
    /// <remarks>
    /// ★★ IDEMPOTENT, AND THE FIRST TIME STANDS. The registration's whole value is the moment it was made — "before
    /// the draw" is the claim it makes — so a re-post that moved the time forward would let a vendor register
    /// early, watch, and quietly refresh the record to a moment that suited them.
    /// </remarks>
    IntentRecord RegisterIntent(string period, string tool, DateTimeOffset now);

    /// <summary>Every intent registered for a period, oldest first.</summary>
    IReadOnlyList<IntentRecord> ListIntent(string period);

    /// <summary>
    /// Store what a submission reported, so a rater has something to look at.
    /// </summary>
    /// <remarks>
    /// ★★ THE CROWD CANNOT JUDGE WHAT IT CANNOT SEE. Nothing stored the findings: the receipt kept counts and a
    /// rater was handed an id. Keyed by the DERIVED id, so a second tool reporting the same defect writes the same
    /// row rather than a duplicate — which is what makes cross-vendor matching possible at all.
    /// </remarks>
    void RecordFindings(IReadOnlyList<FindingRecord> findings);

    /// <summary>One finding by its derived id, or null.</summary>
    FindingRecord? FindFinding(string findingId);

    /// <summary>
    /// Record what one judgement or one rated item cost.
    /// </summary>
    /// <remarks>
    /// ★★ #23-3 ASSERTS NO COST FIGURE BECAUSE NONE HAS BEEN MEASURED, and says to measure it during the first
    /// full period — which only happens if the counters are in place before the period opens. Afterwards it is
    /// an estimate reconstructed from memory, which is the kind of number this standard exists not to publish.
    /// </remarks>
    void RecordCost(CostEntry entry);

    /// <summary>Per-participant tallies for a period, with the unattributed row last.</summary>
    IReadOnlyList<CostTally> CostFor(string period);

    /// <summary>Periods that have any judging recorded, newest first — what a record page can be asked for.</summary>
    /// <remarks>
    /// ★ Distinct from <see cref="PublishedPeriods"/>: a period can have judging without a published rate (the
    /// judging happens first), and a reader looking for the record wants the former.
    /// </remarks>
    IReadOnlyList<string> JudgedPeriods();
}

/// <summary>What one judgement or one rated item cost, attributed to the participant it was spent on.</summary>
/// <param name="Tool">
/// The participant whose finding it was spent on, or null when the finding is unknown. ★★ LOOKED UP from the
/// stored finding, never taken from the caller: a cost the caller attributes is one it can attribute elsewhere.
/// </param>
/// <param name="Kind">"judging" or "crowd".</param>
/// <param name="ModelSeconds">Model time, or null when the judge did not report it — which is not zero.</param>
public sealed record CostEntry(
    string Period, string? Tool, string Kind, string FindingId,
    double? ModelSeconds, int? InputTokens, int? OutputTokens, DateTimeOffset At);

/// <summary>
/// One participant's tally for a period. ★ Tool is null on the unattributed row.
/// </summary>
/// <param name="JudgementsSolelyYours">
/// ★★ THE MARGINAL FIGURE, and the one #23-3 actually asks for: judgements on findings NO OTHER participant
/// reported. The rest were spent on findings that would have been judged anyway, so counting them as this
/// participant's marginal cost would answer a different question — and per-participant totals therefore overlap
/// by design and do not sum to the period's spend.
/// </param>
public sealed record CostTally(
    string? Tool, int Judgements, int JudgementsWithNoTimeReported,
    double ModelSeconds, int InputTokens, int OutputTokens, int CrowdItemsRated,
    int JudgementsSolelyYours = 0, double ModelSecondsSolelyYours = 0);


/// <summary>
/// SQLite storage for the submission register and the verdict record.
/// </summary>
/// <remarks>
/// <para>★★ THE SUBMISSION REGISTER WAS A DICTIONARY, and its own comment said so: "a restart currently forgets
/// that a vendor already submitted, which is precisely the hole the no-withdrawal rule exists to close". That
/// rule is the standard's answer to the worst failure available to it — a vendor runs, dislikes the result, and
/// the published set quietly becomes "the results people were happy with". A rule defeated by a process restart
/// is not a rule, and it was the FIRST thing an unfriendly participant would have found.</para>
///
/// <para>★★ AND THE CLAIM IS ENFORCED BY THE DATABASE, not by a check above it. A partial UNIQUE index on
/// (period, tool) where accepted = 1 means two concurrent submissions cannot both win: one insert fails. An
/// in-process check plus a later insert is a race, and this is exactly the operation somebody has a motive to
/// race.</para>
///
/// <para>★★ THE VERDICT RECORD IS THE OPEN-JUDGING CLAIM. 01-scope-and-governance promises "every judge prompt,
/// every model and version, every raw verdict with its reasoning, and every human adjudication. Published in
/// full. A reader who disagrees with a verdict must be able to find it, read the reasoning, and say so." None of
/// that was stored anywhere — the cascade resolved votes in memory and returned an answer. It was the one claim
/// a sceptic tests first, and it was the one with nothing behind it.</para>
///
/// <para>★ One database file, several tables — the same file the registry uses, resolved from the same option, so
/// a deploy has one thing to back up and one thing to point outside the app directory.</para>
/// </remarks>
public sealed class SqliteNoiseStore : INoiseStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;

    public SqliteNoiseStore(
        IOptions<RegistryOptions> options, IHostEnvironment env, ILogger<SqliteNoiseStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(logger);

        var path = options.Value.DbPath;
        var resolved = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(env.ContentRootPath, path));
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = resolved }.ToString();
        Initialize();
        logger.LogInformation("Noise store (SQLite) at {Path}", resolved);
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS noise_submissions (
                submission_id        TEXT PRIMARY KEY,
                period               TEXT NOT NULL,
                tool                 TEXT NOT NULL,
                tool_version         TEXT NOT NULL,
                received_at          TEXT NOT NULL,
                run_started_at       TEXT NULL,
                accepted             INTEGER NOT NULL,
                problems_json        TEXT NOT NULL,
                holdout_repositories INTEGER NOT NULL,
                covered_repositories INTEGER NOT NULL,
                uncovered_json       TEXT NOT NULL,
                configuration_json   TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_noise_submissions_period ON noise_submissions(period);

            -- ★★ THE NO-WITHDRAWAL RULE, ENFORCED HERE rather than by a check above it. Partial: only an
            -- ACCEPTED submission claims the slot, so a rejected run stays on the register (it is evidence)
            -- without blocking a corrected one. Two concurrent submissions cannot both win — one insert fails,
            -- which an in-process check followed by an insert could never guarantee.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_noise_submissions_claim
                ON noise_submissions(period, tool) WHERE accepted = 1;

            CREATE TABLE IF NOT EXISTS noise_prompts (
                prompt_id     TEXT PRIMARY KEY,
                text          TEXT NOT NULL,
                first_seen_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS noise_verdicts (
                verdict_id    TEXT PRIMARY KEY,
                period        TEXT NOT NULL,
                finding_id    TEXT NOT NULL,
                round         INTEGER NOT NULL,
                judge         TEXT NOT NULL,
                model         TEXT NOT NULL,
                model_version TEXT NOT NULL,
                prompt_id     TEXT NOT NULL,
                verdict       TEXT NOT NULL,
                reasoning     TEXT NOT NULL,
                recorded_at   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_noise_verdicts_period  ON noise_verdicts(period);
            CREATE INDEX IF NOT EXISTS ix_noise_verdicts_finding ON noise_verdicts(period, finding_id);

            -- ★★ APPEND-ONLY, keyed by nothing but its own id. A correction to a published number is a
            -- SECOND row, so it is visible as a correction: on the one figure where §01 says being seen to
            -- suppress ends the standard, a store that overwrote would make the second publication
            -- indistinguishable from the first.
            CREATE TABLE IF NOT EXISTS noise_publications (
                publication_id TEXT PRIMARY KEY,
                period         TEXT NOT NULL,
                payload_json   TEXT NOT NULL,
                published_at   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_noise_publications_period ON noise_publications(period, published_at);

            -- ★★ BESIDE the verdicts, never instead of them. A dispute that could delete what it overturned
            -- would be a withdrawal mechanism, and the register would quietly become "the verdicts nobody
            -- objected to". noise_verdicts is untouched by anything in here.
            -- ★★ (period, tool) is the KEY, so a second registration cannot move the timestamp: the moment it
            -- was made is the entire claim.
            -- ★★ Keyed by the DERIVED finding id, so two tools reporting one defect write one row. `tool` is
            -- stored because the register needs it and is NEVER served with the evidence: a rater told which
            -- vendor produced a finding is being asked a different question.
            CREATE TABLE IF NOT EXISTS noise_findings (
                finding_id  TEXT PRIMARY KEY,
                period      TEXT NOT NULL,
                tool        TEXT NOT NULL,
                repo_id     TEXT NOT NULL,
                pinned_sha  TEXT NOT NULL,
                file_path   TEXT NULL,
                line        INTEGER NULL,
                rule_id     TEXT NOT NULL,
                title       TEXT NULL,
                claim_class TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_noise_findings_period ON noise_findings(period);

            -- ★★ WHO REPORTED IT — one row per (finding, tool). The finding row itself keeps the FIRST reporter
            -- because a second tool reporting the same defect is the same finding, which is the property the
            -- pooled union needs. But the cost of judging that finding was spent on BOTH of them, and attributing
            -- it wholly to whoever submitted first overstates one participant and understates the other.
            CREATE TABLE IF NOT EXISTS noise_finding_tools (
                finding_id TEXT NOT NULL,
                tool       TEXT NOT NULL,
                PRIMARY KEY (finding_id, tool)
            );

            -- ★★ APPEND-ONLY, one row per judgement or rated item. The marginal cost of a participant is the
            -- sum over its rows, so a ledger that overwrote could not answer "what did this period cost" twice
            -- with the same number. `tool` is NULL when the finding is unknown — that cost is real and must not
            -- vanish into a smaller total.
            CREATE TABLE IF NOT EXISTS noise_cost (
                cost_id       TEXT PRIMARY KEY,
                period        TEXT NOT NULL,
                tool          TEXT NULL,
                kind          TEXT NOT NULL,
                finding_id    TEXT NOT NULL,
                model_seconds REAL NULL,
                input_tokens  INTEGER NULL,
                output_tokens INTEGER NULL,
                at            TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_noise_cost_period ON noise_cost(period);

            CREATE TABLE IF NOT EXISTS noise_intent (
                period        TEXT NOT NULL,
                tool          TEXT NOT NULL,
                registered_at TEXT NOT NULL,
                PRIMARY KEY (period, tool)
            );

            CREATE TABLE IF NOT EXISTS noise_disputes (
                dispute_id           TEXT PRIMARY KEY,
                period               TEXT NOT NULL,
                finding_id           TEXT NOT NULL,
                raised_by            TEXT NOT NULL,
                reason               TEXT NOT NULL,
                raised_at            TEXT NOT NULL,
                outcome              TEXT NULL,
                resolution_reasoning TEXT NULL,
                resolved_at          TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_noise_disputes_period ON noise_disputes(period, raised_at);

            CREATE TABLE IF NOT EXISTS noise_rejudge (
                period        TEXT NOT NULL,
                finding_id    TEXT NOT NULL,
                verdict       TEXT NOT NULL,
                model         TEXT NOT NULL,
                model_version TEXT NOT NULL,
                prompt_id     TEXT NOT NULL,
                reasoning     TEXT NOT NULL,
                recorded_at   TEXT NOT NULL,
                PRIMARY KEY (period, finding_id)
            );

            CREATE TABLE IF NOT EXISTS noise_resolutions (
                period                  TEXT NOT NULL,
                finding_id              TEXT NOT NULL,
                state                   TEXT NOT NULL,
                verdict                 TEXT NULL,
                settled_at_round        INTEGER NULL,
                actionability_contested INTEGER NOT NULL,
                actionable              INTEGER NULL,
                reason                  TEXT NOT NULL,
                recorded_at             TEXT NOT NULL,
                PRIMARY KEY (period, finding_id)
            );
            """;
        cmd.ExecuteNonQuery();

        // ★ Additive on a table that may already exist in a dev database — SQLite has no ADD COLUMN IF NOT
        // EXISTS, so this is guarded, exactly as the registry store does it.
        AddColumnIfMissing(conn, "noise_submissions", "configuration_json", "TEXT NULL");

        // ★★ The panel's shape, per verdict (#10). Additive for the same reason: a dev database already holds
        // verdicts recorded before these were required, and dropping them to add two columns would destroy the
        // record the standard promises to keep.
        AddColumnIfMissing(conn, "noise_verdicts", "model_family", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(conn, "noise_verdicts", "temperature", "REAL NOT NULL DEFAULT 0");
    }

    /// <summary>Idempotent ALTER TABLE … ADD COLUMN, checked via PRAGMA table_info.</summary>
    private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string def)
    {
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table})";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), column, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {def}";
        alter.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public bool TryRecordSubmission(
        SubmissionReceipt receipt, DateTimeOffset? runStartedAt, string? configurationJson)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO noise_submissions
                (submission_id, period, tool, tool_version, received_at, run_started_at, accepted,
                 problems_json, holdout_repositories, covered_repositories, uncovered_json,
                 configuration_json)
            VALUES ($id, $period, $tool, $toolVersion, $receivedAt, $runStartedAt, $accepted,
                    $problems, $holdout, $covered, $uncovered, $configuration)
            """;
        cmd.Parameters.AddWithValue("$id", receipt.SubmissionId);
        cmd.Parameters.AddWithValue("$period", receipt.Period);
        cmd.Parameters.AddWithValue("$tool", receipt.Tool);
        cmd.Parameters.AddWithValue("$toolVersion", receipt.ToolVersion);
        cmd.Parameters.AddWithValue("$receivedAt", receipt.ReceivedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$runStartedAt", runStartedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$accepted", receipt.Accepted ? 1 : 0);
        cmd.Parameters.AddWithValue("$problems", JsonSerializer.Serialize(receipt.Problems, Json));
        cmd.Parameters.AddWithValue("$holdout", receipt.HoldoutRepositories);
        cmd.Parameters.AddWithValue("$covered", receipt.CoveredRepositories);
        cmd.Parameters.AddWithValue("$uncovered", JsonSerializer.Serialize(receipt.Uncovered, Json));
        cmd.Parameters.AddWithValue("$configuration", configurationJson ?? (object)DBNull.Value);

        try
        {
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            // ★ The unique claim already exists. Losing this race is the rule working, not an error to log
            // and swallow — the caller turns it into the conflict a participant is told about.
            return false;
        }
    }

    /// <inheritdoc />
    public SubmissionReceipt? FindSubmission(string submissionId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM noise_submissions WHERE submission_id = $id";
        cmd.Parameters.AddWithValue("$id", submissionId ?? "");
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadReceipt(reader) : null;
    }

    /// <inheritdoc />
    public bool AlreadySubmitted(string tool, string period)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM noise_submissions WHERE tool = $tool AND period = $period AND accepted = 1";
        cmd.Parameters.AddWithValue("$tool", tool ?? "");
        cmd.Parameters.AddWithValue("$period", period ?? "");
        return Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<SubmissionReceipt> ListSubmissions(string period)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT * FROM noise_submissions WHERE period = $period ORDER BY received_at DESC";
        cmd.Parameters.AddWithValue("$period", period ?? "");
        using var reader = cmd.ExecuteReader();

        var list = new List<SubmissionReceipt>();
        while (reader.Read())
        {
            list.Add(ReadReceipt(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public void RecordVerdict(VerdictRecord verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO noise_verdicts
                (verdict_id, period, finding_id, round, judge, model, model_version, prompt_id,
                 verdict, reasoning, recorded_at, model_family, temperature)
            VALUES ($id, $period, $finding, $round, $judge, $model, $modelVersion, $promptId,
                    $verdict, $reasoning, $recordedAt, $family, $temperature)
            """;
        cmd.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("n"));
        cmd.Parameters.AddWithValue("$period", verdict.Period);
        cmd.Parameters.AddWithValue("$finding", verdict.FindingId);
        cmd.Parameters.AddWithValue("$round", verdict.Round);
        cmd.Parameters.AddWithValue("$judge", verdict.Judge);
        cmd.Parameters.AddWithValue("$model", verdict.Model);
        cmd.Parameters.AddWithValue("$modelVersion", verdict.ModelVersion);
        cmd.Parameters.AddWithValue("$promptId", verdict.PromptId);
        cmd.Parameters.AddWithValue("$verdict", verdict.Verdict);
        cmd.Parameters.AddWithValue("$reasoning", verdict.Reasoning);
        cmd.Parameters.AddWithValue("$recordedAt", verdict.RecordedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$family", verdict.ModelFamily);
        cmd.Parameters.AddWithValue("$temperature", verdict.Temperature);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void RecordResolution(ResolutionRecord resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        // ★ REPLACE on (period, finding): a re-judge corrects how a finding settled rather than leaving two
        // answers on the record. The raw VERDICTS are append-only — those are the evidence — but "how it
        // settled" has one current value.
        cmd.CommandText =
            """
            INSERT INTO noise_resolutions
                (period, finding_id, state, verdict, settled_at_round, actionability_contested,
                 actionable, reason, recorded_at)
            VALUES ($period, $finding, $state, $verdict, $round, $contested, $actionable, $reason, $recordedAt)
            ON CONFLICT(period, finding_id) DO UPDATE SET
                state = excluded.state, verdict = excluded.verdict,
                settled_at_round = excluded.settled_at_round,
                actionability_contested = excluded.actionability_contested,
                actionable = excluded.actionable, reason = excluded.reason,
                recorded_at = excluded.recorded_at
            """;
        cmd.Parameters.AddWithValue("$period", resolution.Period);
        cmd.Parameters.AddWithValue("$finding", resolution.FindingId);
        cmd.Parameters.AddWithValue("$state", resolution.State);
        cmd.Parameters.AddWithValue("$verdict", resolution.Verdict ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$round", resolution.SettledAtRound ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$contested", resolution.ActionabilityContested ? 1 : 0);
        cmd.Parameters.AddWithValue(
            "$actionable", resolution.Actionable is { } a ? (a ? 1 : 0) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$reason", resolution.Reason);
        cmd.Parameters.AddWithValue("$recordedAt", resolution.RecordedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void RegisterPrompt(string promptId, string text, DateTimeOffset now)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        // ★ Stored ONCE per id rather than on every verdict. The same prompt answers thousands of findings, and
        // a record that repeats it is a record nobody downloads.
        cmd.CommandText =
            """
            INSERT INTO noise_prompts (prompt_id, text, first_seen_at)
            VALUES ($id, $text, $at)
            ON CONFLICT(prompt_id) DO NOTHING
            """;
        cmd.Parameters.AddWithValue("$id", promptId ?? "");
        cmd.Parameters.AddWithValue("$text", text ?? "");
        cmd.Parameters.AddWithValue("$at", now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyList<VerdictRecord> ListVerdicts(string period)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT * FROM noise_verdicts WHERE period = $period ORDER BY recorded_at, finding_id, round, judge";
        cmd.Parameters.AddWithValue("$period", period ?? "");
        using var reader = cmd.ExecuteReader();

        var list = new List<VerdictRecord>();
        while (reader.Read())
        {
            list.Add(new VerdictRecord(
                reader.GetString(reader.GetOrdinal("period")),
                reader.GetString(reader.GetOrdinal("finding_id")),
                reader.GetInt32(reader.GetOrdinal("round")),
                reader.GetString(reader.GetOrdinal("judge")),
                reader.GetString(reader.GetOrdinal("model")),
                reader.GetString(reader.GetOrdinal("model_version")),
                reader.GetString(reader.GetOrdinal("prompt_id")),
                reader.GetString(reader.GetOrdinal("verdict")),
                reader.GetString(reader.GetOrdinal("reasoning")),
                DateTimeOffset.Parse(
                    reader.GetString(reader.GetOrdinal("recorded_at")),
                    System.Globalization.CultureInfo.InvariantCulture),
                reader.GetString(reader.GetOrdinal("model_family")),
                reader.GetDouble(reader.GetOrdinal("temperature"))));
        }

        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<ResolutionRecord> ListResolutions(string period)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM noise_resolutions WHERE period = $period ORDER BY finding_id";
        cmd.Parameters.AddWithValue("$period", period ?? "");
        using var reader = cmd.ExecuteReader();

        var list = new List<ResolutionRecord>();
        while (reader.Read())
        {
            var verdictOrdinal = reader.GetOrdinal("verdict");
            var roundOrdinal = reader.GetOrdinal("settled_at_round");
            var actionableOrdinal = reader.GetOrdinal("actionable");
            list.Add(new ResolutionRecord(
                reader.GetString(reader.GetOrdinal("period")),
                reader.GetString(reader.GetOrdinal("finding_id")),
                reader.GetString(reader.GetOrdinal("state")),
                reader.IsDBNull(verdictOrdinal) ? null : reader.GetString(verdictOrdinal),
                reader.IsDBNull(roundOrdinal) ? null : reader.GetInt32(roundOrdinal),
                reader.GetInt32(reader.GetOrdinal("actionability_contested")) == 1,
                reader.IsDBNull(actionableOrdinal) ? null : reader.GetInt32(actionableOrdinal) == 1,
                reader.GetString(reader.GetOrdinal("reason")),
                DateTimeOffset.Parse(
                    reader.GetString(reader.GetOrdinal("recorded_at")),
                    System.Globalization.CultureInfo.InvariantCulture)));
        }

        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<PromptRecord> ListPrompts(string period)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT p.* FROM noise_prompts p
            WHERE p.prompt_id IN (SELECT DISTINCT prompt_id FROM noise_verdicts WHERE period = $period)
            ORDER BY p.prompt_id
            """;
        cmd.Parameters.AddWithValue("$period", period ?? "");
        using var reader = cmd.ExecuteReader();

        var list = new List<PromptRecord>();
        while (reader.Read())
        {
            list.Add(new PromptRecord(
                reader.GetString(reader.GetOrdinal("prompt_id")),
                reader.GetString(reader.GetOrdinal("text")),
                DateTimeOffset.Parse(
                    reader.GetString(reader.GetOrdinal("first_seen_at")),
                    System.Globalization.CultureInfo.InvariantCulture)));
        }

        return list;
    }

    /// <inheritdoc />
    public string? ConfigurationJson(string submissionId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT configuration_json FROM noise_submissions WHERE submission_id = $id";
        cmd.Parameters.AddWithValue("$id", submissionId ?? "");
        var value = cmd.ExecuteScalar();
        return value is null or DBNull ? null : (string)value;
    }

    /// <inheritdoc />
    public void RaiseDispute(DisputeRecord dispute)
    {
        ArgumentNullException.ThrowIfNull(dispute);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO noise_disputes
                (dispute_id, period, finding_id, raised_by, reason, raised_at)
            VALUES ($id, $period, $finding, $by, $reason, $at)
            """;
        cmd.Parameters.AddWithValue("$id", dispute.DisputeId);
        cmd.Parameters.AddWithValue("$period", dispute.Period);
        cmd.Parameters.AddWithValue("$finding", dispute.FindingId);
        cmd.Parameters.AddWithValue("$by", dispute.RaisedBy);
        cmd.Parameters.AddWithValue("$reason", dispute.Reason);
        cmd.Parameters.AddWithValue("$at", dispute.RaisedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public DisputeRecord? FindDispute(string disputeId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = DisputeSelect + " WHERE dispute_id = $id";
        cmd.Parameters.AddWithValue("$id", disputeId ?? "");
        using var reader = cmd.ExecuteReader();

        return reader.Read() ? ReadDispute(reader) : null;
    }

    /// <inheritdoc />
    public bool ResolveDispute(string disputeId, string outcome, string reasoning, DateTimeOffset resolvedAt)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        // ★★ `outcome IS NULL` is the lock. A read-then-write above this could resolve the same dispute twice
        // from two requests, and the second answer would silently replace the first.
        cmd.CommandText =
            """
            UPDATE noise_disputes
               SET outcome = $outcome, resolution_reasoning = $reasoning, resolved_at = $at
             WHERE dispute_id = $id AND outcome IS NULL
            """;
        cmd.Parameters.AddWithValue("$outcome", outcome);
        cmd.Parameters.AddWithValue("$reasoning", reasoning);
        cmd.Parameters.AddWithValue("$at", resolvedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$id", disputeId ?? "");

        return cmd.ExecuteNonQuery() == 1;
    }

    /// <inheritdoc />
    public IReadOnlyList<DisputeRecord> ListDisputes(string period)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = DisputeSelect + " WHERE period = $period ORDER BY raised_at, dispute_id";
        cmd.Parameters.AddWithValue("$period", period ?? "");
        using var reader = cmd.ExecuteReader();

        var list = new List<DisputeRecord>();
        while (reader.Read())
        {
            list.Add(ReadDispute(reader));
        }

        return list;
    }

    private const string DisputeSelect =
        "SELECT dispute_id, period, finding_id, raised_by, reason, raised_at, outcome, resolution_reasoning, "
      + "resolved_at FROM noise_disputes";

    private static DisputeRecord ReadDispute(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8)
            ? null
            : DateTimeOffset.Parse(reader.GetString(8), System.Globalization.CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public IntentRecord RegisterIntent(string period, string tool, DateTimeOffset now)
    {
        using var conn = Open();

        using (var insert = conn.CreateCommand())
        {
            // ★ DO NOTHING on conflict — the first registration's timestamp is the claim, and a later one must
            // not overwrite it. Enforced by the key rather than by a read-then-write above it.
            insert.CommandText =
                """
                INSERT INTO noise_intent (period, tool, registered_at)
                VALUES ($period, $tool, $at)
                ON CONFLICT (period, tool) DO NOTHING
                """;
            insert.Parameters.AddWithValue("$period", period);
            insert.Parameters.AddWithValue("$tool", tool);
            insert.Parameters.AddWithValue("$at", now.ToString("O"));
            insert.ExecuteNonQuery();
        }

        using var read = conn.CreateCommand();
        read.CommandText =
            "SELECT period, tool, registered_at FROM noise_intent WHERE period = $period AND tool = $tool";
        read.Parameters.AddWithValue("$period", period);
        read.Parameters.AddWithValue("$tool", tool);
        using var reader = read.ExecuteReader();
        reader.Read();

        return new IntentRecord(
            reader.GetString(0), reader.GetString(1),
            DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public IReadOnlyList<IntentRecord> ListIntent(string period)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT period, tool, registered_at FROM noise_intent WHERE period = $period "
          + "ORDER BY registered_at, tool";
        cmd.Parameters.AddWithValue("$period", period ?? "");
        using var reader = cmd.ExecuteReader();

        var list = new List<IntentRecord>();
        while (reader.Read())
        {
            list.Add(new IntentRecord(
                reader.GetString(0), reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture)));
        }

        return list;
    }

    /// <inheritdoc />
    public void RecordFindings(IReadOnlyList<FindingRecord> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Count == 0)
        {
            return;
        }

        using var conn = Open();
        using var tx = conn.BeginTransaction();

        foreach (var f in findings)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            // ★ DO NOTHING on conflict: the same defect reported by a second tool is the SAME finding, and the
            // first row already describes it. Overwriting would churn the row for no gain and lose nothing either.
            cmd.CommandText =
                """
                INSERT INTO noise_findings
                    (finding_id, period, tool, repo_id, pinned_sha, file_path, line, rule_id, title, claim_class)
                VALUES ($id, $period, $tool, $repo, $sha, $file, $line, $rule, $title, $class)
                ON CONFLICT (finding_id) DO NOTHING
                """;
            cmd.Parameters.AddWithValue("$id", f.FindingId);
            cmd.Parameters.AddWithValue("$period", f.Period);
            cmd.Parameters.AddWithValue("$tool", f.Tool);
            cmd.Parameters.AddWithValue("$repo", f.RepoId);
            cmd.Parameters.AddWithValue("$sha", f.PinnedSha);
            cmd.Parameters.AddWithValue("$file", (object?)f.FilePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$line", (object?)f.Line ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rule", f.RuleId);
            cmd.Parameters.AddWithValue("$title", (object?)f.Title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$class", f.ClaimClass);
            cmd.ExecuteNonQuery();

            // ★★ AND WHO REPORTED IT, which the finding row cannot say twice. Two tools reporting one defect
            // produce one finding and two rows here — the difference between "this cost was spent on you" and
            // "this cost was spent on a finding you also reported".
            using var who = conn.CreateCommand();
            who.Transaction = tx;
            who.CommandText =
                """
                INSERT INTO noise_finding_tools (finding_id, tool) VALUES ($id, $tool)
                ON CONFLICT (finding_id, tool) DO NOTHING
                """;
            who.Parameters.AddWithValue("$id", f.FindingId);
            who.Parameters.AddWithValue("$tool", f.Tool);
            who.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <inheritdoc />
    public FindingRecord? FindFinding(string findingId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT finding_id, period, tool, repo_id, pinned_sha, file_path, line, rule_id, title, claim_class
            FROM noise_findings WHERE finding_id = $id
            """;
        cmd.Parameters.AddWithValue("$id", findingId ?? "");
        using var reader = cmd.ExecuteReader();

        return reader.Read()
            ? new FindingRecord(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9))
            : null;
    }

    /// <inheritdoc />
    public void RecordCost(CostEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO noise_cost
                (cost_id, period, tool, kind, finding_id, model_seconds, input_tokens, output_tokens, at)
            VALUES ($id, $period, $tool, $kind, $finding, $seconds, $in, $out, $at)
            """;
        cmd.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("n"));
        cmd.Parameters.AddWithValue("$period", entry.Period);
        cmd.Parameters.AddWithValue("$tool", (object?)entry.Tool ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", entry.Kind);
        cmd.Parameters.AddWithValue("$finding", entry.FindingId);
        cmd.Parameters.AddWithValue("$seconds", (object?)entry.ModelSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$in", (object?)entry.InputTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$out", (object?)entry.OutputTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", entry.At.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyList<CostTally> CostFor(string period)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        // ★ Summed in SQL, grouped on the tool with NULL as its own group — the unattributed row. COUNT over a
        // nullable column counts non-nulls, which is exactly what "judgements that reported a time" means.
        // ★★ ATTRIBUTED BY JOIN, not by the column. A finding two tools reported produces two rows here — the
        // judgement was spent on both — so `reporters = 1` is what separates the marginal cost from the cost
        // that would have been paid anyway. The LEFT JOIN keeps a cost whose finding nothing recorded: it is real
        // spend attributable to nobody, and dropping it would make the total smaller than what was spent.
        cmd.CommandText =
            """
            WITH reporters AS (
                SELECT finding_id, COUNT(*) AS n FROM noise_finding_tools GROUP BY finding_id
            )
            SELECT ft.tool,
                   SUM(CASE WHEN c.kind = 'judging' THEN 1 ELSE 0 END)                            AS judgements,
                   SUM(CASE WHEN c.kind = 'judging' AND c.model_seconds IS NULL THEN 1 ELSE 0 END) AS untimed,
                   COALESCE(SUM(c.model_seconds), 0)                                              AS seconds,
                   COALESCE(SUM(c.input_tokens), 0)                                               AS in_tok,
                   COALESCE(SUM(c.output_tokens), 0)                                              AS out_tok,
                   SUM(CASE WHEN c.kind = 'crowd' THEN 1 ELSE 0 END)                              AS crowd,
                   SUM(CASE WHEN c.kind = 'judging' AND COALESCE(r.n, 0) = 1 THEN 1 ELSE 0 END)    AS solely,
                   COALESCE(SUM(CASE WHEN COALESCE(r.n, 0) = 1 THEN c.model_seconds END), 0)       AS solely_seconds
            FROM noise_cost c
            LEFT JOIN noise_finding_tools ft ON ft.finding_id = c.finding_id
            LEFT JOIN reporters r            ON r.finding_id  = c.finding_id
            WHERE c.period = $period
            GROUP BY ft.tool
            ORDER BY ft.tool IS NULL, ft.tool
            """;
        cmd.Parameters.AddWithValue("$period", period ?? "");

        List<CostTally> tallies = [];
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tallies.Add(new CostTally(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetInt32(1), reader.GetInt32(2),
                reader.GetDouble(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
                reader.GetInt32(7), reader.GetDouble(8)));
        }

        return tallies;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> JudgedPeriods()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT period FROM noise_resolutions ORDER BY period DESC";
        using var reader = cmd.ExecuteReader();

        var periods = new List<string>();
        while (reader.Read())
        {
            periods.Add(reader.GetString(0));
        }

        return periods;
    }

    /// <inheritdoc />
    public IReadOnlyList<PeriodTally> PublishedTallies()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT period, payload_json FROM noise_publications ORDER BY published_at, publication_id";
        using var reader = cmd.ExecuteReader();

        var list = new List<PeriodTally>();
        while (reader.Read())
        {
            var period = reader.GetString(0);
            try
            {
                var root = System.Text.Json.JsonDocument.Parse(reader.GetString(1)).RootElement;
                var noise = Read(root, "noise");
                var judged = noise
                           + Read(root, "validAndActionable")
                           + Read(root, "validNotActionable");
                list.Add(new PeriodTally(period, judged, noise));
            }
            catch (System.Text.Json.JsonException)
            {
                // ★ A payload that will not parse is skipped rather than throwing: one unreadable row must not
                // take the rolling figure — and every other endpoint — down with it. It is a stored artefact,
                // not live input, so there is nothing to reject back to a caller.
            }
        }

        return list;

        static int Read(System.Text.Json.JsonElement root, string name) =>
            root.TryGetProperty(name, out var v)
            && v.ValueKind == System.Text.Json.JsonValueKind.Number
            && v.TryGetInt32(out var n) ? n : 0;
    }

    /// <inheritdoc />
    public void RecordRejudge(IReadOnlyList<RejudgeRecord> verdicts)
    {
        ArgumentNullException.ThrowIfNull(verdicts);
        if (verdicts.Count == 0)
        {
            return;
        }

        using var conn = Open();
        using var tx = conn.BeginTransaction();

        foreach (var v in verdicts)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                INSERT INTO noise_rejudge
                    (period, finding_id, verdict, model, model_version, prompt_id, reasoning, recorded_at)
                VALUES ($period, $finding, $verdict, $model, $version, $prompt, $reasoning, $at)
                ON CONFLICT (period, finding_id) DO UPDATE SET
                    verdict = excluded.verdict, model = excluded.model,
                    model_version = excluded.model_version, prompt_id = excluded.prompt_id,
                    reasoning = excluded.reasoning, recorded_at = excluded.recorded_at
                """;
            cmd.Parameters.AddWithValue("$period", v.Period);
            cmd.Parameters.AddWithValue("$finding", v.FindingId);
            cmd.Parameters.AddWithValue("$verdict", v.Verdict);
            cmd.Parameters.AddWithValue("$model", v.Model);
            cmd.Parameters.AddWithValue("$version", v.ModelVersion);
            cmd.Parameters.AddWithValue("$prompt", v.PromptId);
            cmd.Parameters.AddWithValue("$reasoning", v.Reasoning);
            cmd.Parameters.AddWithValue("$at", v.RecordedAt.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <inheritdoc />
    public IReadOnlyList<RejudgeRecord> ListRejudge(string period)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT period, finding_id, verdict, model, model_version, prompt_id, reasoning, recorded_at
            FROM noise_rejudge WHERE period = $period ORDER BY finding_id
            """;
        cmd.Parameters.AddWithValue("$period", period ?? "");
        using var reader = cmd.ExecuteReader();

        var list = new List<RejudgeRecord>();
        while (reader.Read())
        {
            list.Add(new RejudgeRecord(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture)));
        }

        return list;
    }

    /// <inheritdoc />
    public void RecordPublication(string period, string payloadJson, DateTimeOffset publishedAt)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO noise_publications (publication_id, period, payload_json, published_at)
            VALUES ($id, $period, $payload, $at)
            """;
        cmd.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("n"));
        cmd.Parameters.AddWithValue("$period", period);
        cmd.Parameters.AddWithValue("$payload", payloadJson);
        cmd.Parameters.AddWithValue("$at", publishedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public (string PayloadJson, DateTimeOffset PublishedAt, IReadOnlyList<DateTimeOffset> History)?
        LatestPublication(string period)
    {
        using var conn = Open();

        var history = new List<DateTimeOffset>();
        using (var all = conn.CreateCommand())
        {
            all.CommandText =
                "SELECT published_at FROM noise_publications WHERE period = $period ORDER BY published_at";
            all.Parameters.AddWithValue("$period", period ?? "");
            using var reader = all.ExecuteReader();
            while (reader.Read())
            {
                history.Add(DateTimeOffset.Parse(
                    reader.GetString(0), System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        if (history.Count == 0)
        {
            return null;
        }

        using var latest = conn.CreateCommand();
        latest.CommandText =
            """
            SELECT payload_json, published_at FROM noise_publications
            WHERE period = $period ORDER BY published_at DESC, publication_id DESC LIMIT 1
            """;
        latest.Parameters.AddWithValue("$period", period ?? "");
        using var row = latest.ExecuteReader();
        if (!row.Read())
        {
            return null;
        }

        return (
            row.GetString(0),
            DateTimeOffset.Parse(row.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
            history);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PublishedPeriods()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT DISTINCT period FROM noise_publications ORDER BY period DESC";
        using var reader = cmd.ExecuteReader();

        var periods = new List<string>();
        while (reader.Read())
        {
            periods.Add(reader.GetString(0));
        }

        return periods;
    }

    private static SubmissionReceipt ReadReceipt(SqliteDataReader reader) => new(
        SubmissionId: reader.GetString(reader.GetOrdinal("submission_id")),
        Period: reader.GetString(reader.GetOrdinal("period")),
        Tool: reader.GetString(reader.GetOrdinal("tool")),
        ToolVersion: reader.GetString(reader.GetOrdinal("tool_version")),
        ReceivedAt: DateTimeOffset.Parse(
            reader.GetString(reader.GetOrdinal("received_at")),
            System.Globalization.CultureInfo.InvariantCulture),
        Accepted: reader.GetInt32(reader.GetOrdinal("accepted")) == 1,
        Problems: JsonSerializer.Deserialize<List<string>>(
            reader.GetString(reader.GetOrdinal("problems_json")), Json) ?? [],
        HoldoutRepositories: reader.GetInt32(reader.GetOrdinal("holdout_repositories")),
        CoveredRepositories: reader.GetInt32(reader.GetOrdinal("covered_repositories")),
        Uncovered: JsonSerializer.Deserialize<List<string>>(
            reader.GetString(reader.GetOrdinal("uncovered_json")), Json) ?? []);
}
