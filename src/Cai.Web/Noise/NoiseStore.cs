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
public sealed record VerdictRecord(
    string Period, string FindingId, int Round,
    string Judge, string Model, string ModelVersion, string PromptId,
    string Verdict, string Reasoning, DateTimeOffset RecordedAt);

/// <summary>How one finding's cascade settled.</summary>
public sealed record ResolutionRecord(
    string Period, string FindingId, string State, string? Verdict, int? SettledAtRound,
    bool ActionabilityContested, bool? Actionable, string Reason, DateTimeOffset RecordedAt);

/// <summary>A judge prompt, stored once and published in full.</summary>
public sealed record PromptRecord(string PromptId, string Text, DateTimeOffset FirstSeenAt);

/// <summary>
/// Durable storage for the two things the standard promises to keep: who submitted, and how it was judged.
/// </summary>
public interface INoiseStore
{
    /// <summary>Record a receipt. Returns false when an accepted submission already claims (tool, period).</summary>
    bool TryRecordSubmission(SubmissionReceipt receipt, DateTimeOffset? runStartedAt);

    /// <summary>A receipt by id, or null.</summary>
    SubmissionReceipt? FindSubmission(string submissionId);

    /// <summary>Whether an ACCEPTED submission already exists for this tool and period.</summary>
    bool AlreadySubmitted(string tool, string period);

    /// <summary>Every submission for a period, newest first — the register.</summary>
    IReadOnlyList<SubmissionReceipt> ListSubmissions(string period);

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
}

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
                uncovered_json       TEXT NOT NULL
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
    }

    /// <inheritdoc />
    public bool TryRecordSubmission(SubmissionReceipt receipt, DateTimeOffset? runStartedAt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO noise_submissions
                (submission_id, period, tool, tool_version, received_at, run_started_at, accepted,
                 problems_json, holdout_repositories, covered_repositories, uncovered_json)
            VALUES ($id, $period, $tool, $toolVersion, $receivedAt, $runStartedAt, $accepted,
                    $problems, $holdout, $covered, $uncovered)
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
                 verdict, reasoning, recorded_at)
            VALUES ($id, $period, $finding, $round, $judge, $model, $modelVersion, $promptId,
                    $verdict, $reasoning, $recordedAt)
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
                    System.Globalization.CultureInfo.InvariantCulture)));
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
