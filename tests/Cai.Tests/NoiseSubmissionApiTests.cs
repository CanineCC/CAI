using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Submitting a run against a published holdout, and the checks CAI applies to it.
/// </summary>
/// <remarks>
/// <para>★ CAI never runs anyone's scanner. A vendor runs their own tool against the published holdout,
/// on their own infrastructure, and submits findings — so CAI needs no credentials, no access and no
/// licence to anybody's product. What it does instead is VERIFY: that the run covered the holdout that
/// was published, at the shas that were published.</para>
/// <para>Every check here exists because its absence is a route to a flattering number that nobody could
/// see from the outside.</para>
/// </remarks>
public sealed class NoiseSubmissionApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A tool name unique to each test.
    /// </summary>
    /// <remarks>
    /// ★ The no-withdrawal rule claims a (tool, period) slot for the life of the process, so tests
    /// sharing a tool name collide on it — which is the RULE WORKING, not a defect in it. Each test
    /// therefore acts as a distinct vendor, which is also how two real vendors relate to one another.
    /// </remarks>
    private static string Tool([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"probe-{caller}";

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/submissions", payload, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    /// <summary>The holdout as published, so a fixture cannot drift from the thing under test.</summary>
    private async Task<List<(string RepoId, string Sha, string Language)>> HoldoutAsync()
    {
        using var client = fx.Client();
        var text = await client.GetStringAsync("/api/noise/holdout/2026-09", Ct);
        return [.. JsonDocument.Parse(text).RootElement.GetProperty("repositories").EnumerateArray()
            .Select(r => (
                r.GetProperty("repoId").GetString()!,
                r.GetProperty("pinnedSha").GetString()!,
                r.GetProperty("language").GetString()!))];
    }

    private static object Finding(string repoId, string sha, string rule = "D4", string claimClass = "pointwise") => new
    {
        repoId,
        pinnedSha = sha,
        filePath = "src/Thing.cs",
        line = 42,
        ruleId = rule,
        title = "a finding",
        claimClass,
    };

    private async Task<object> ValidSubmissionAsync(string tool)
    {
        var holdout = await HoldoutAsync();
        return new
        {
            period = "2026-09",
            tool,
            toolVersion = "engine-8b08d6c6",
            // ★ The run must have STARTED AFTER the draw was published (2026-08-15) — the ordering
            // the whole holdout rests on, and now checked rather than asserted in prose.
            runStartedAt = "2026-08-20T09:00:00Z",
            // ★ The configuration declaration, required since #23-1: every other check constrains the
            // RUN, none of them constrains which rules were switched on.
            configuration = new
            {
                rulesetId = "watchdog-default-2026.08",
                isProductDefault = true,
                rulesDisabled = Array.Empty<string>(),
                thresholdsAltered = Array.Empty<object>(),
            },
            recency = holdout.Select(h => new { repoId = h.RepoId, stratum = "never-trained" }),
            findings = holdout.Select(h => Finding(h.RepoId, h.Sha)),
        };
    }

    // ── The happy path ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_run_covering_the_published_holdout_is_accepted_with_a_receipt()
    {
        var (status, body) = await PostAsync(await ValidSubmissionAsync(Tool()));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("accepted").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("submissionId").GetString()));
    }

    [Fact]
    public async Task The_receipt_is_retrievable_afterwards()
    {
        var (_, posted) = await PostAsync(await ValidSubmissionAsync(Tool()));
        var id = posted.GetProperty("submissionId").GetString();

        using var client = fx.Client();
        var response = await client.GetAsync($"/api/noise/submissions/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal(Tool(), body.GetProperty("tool").GetString());
        Assert.Equal("2026-09", body.GetProperty("period").GetString());
    }

    // ── The checks, each closing a route to a flattering number ───────────────────────────────────

    /// <summary>
    /// ★★ A FINDING ON A REPOSITORY OUTSIDE THE HOLDOUT IS REFUSED. Without this a vendor submits
    /// findings from code of their own choosing and reports a rate over it — which is not a measurement
    /// of anything the standard drew.
    /// </summary>
    [Fact]
    public async Task STAR_a_finding_outside_the_holdout_is_refused()
    {
        var holdout = await HoldoutAsync();
        var submission = new
        {
            period = "2026-09",
            tool = Tool(),
            toolVersion = "v1",
            // ★ The run must have STARTED AFTER the draw was published (2026-08-15) — the ordering
            // the whole holdout rests on, and now checked rather than asserted in prose.
            runStartedAt = "2026-08-20T09:00:00Z",
            // ★ The configuration declaration, required since #23-1: every other check constrains the
            // RUN, none of them constrains which rules were switched on.
            configuration = new
            {
                rulesetId = "watchdog-default-2026.08",
                isProductDefault = true,
                rulesDisabled = Array.Empty<string>(),
                thresholdsAltered = Array.Empty<object>(),
            },
            recency = holdout.Select(h => new { repoId = h.RepoId, stratum = "never-trained" }),
            findings = new[] { Finding("some/other-repo", holdout[0].Sha) },
        };

        var (_, body) = await PostAsync(submission);

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("some/other-repo", string.Join(" ",
            body.GetProperty("problems").EnumerateArray().Select(p => p.GetString())));
    }

    /// <summary>
    /// ★★ A FINDING AT THE WRONG SHA IS REFUSED. "The same code" means nothing across two runs unless
    /// the revision matches — a vendor scanning a later HEAD is measuring different code and would be
    /// compared against everyone else as though they were not.
    /// </summary>
    [Fact]
    public async Task STAR_a_finding_at_the_wrong_sha_is_refused()
    {
        var holdout = await HoldoutAsync();
        var submission = new
        {
            period = "2026-09",
            tool = Tool(),
            toolVersion = "v1",
            // ★ The run must have STARTED AFTER the draw was published (2026-08-15) — the ordering
            // the whole holdout rests on, and now checked rather than asserted in prose.
            runStartedAt = "2026-08-20T09:00:00Z",
            // ★ The configuration declaration, required since #23-1: every other check constrains the
            // RUN, none of them constrains which rules were switched on.
            configuration = new
            {
                rulesetId = "watchdog-default-2026.08",
                isProductDefault = true,
                rulesDisabled = Array.Empty<string>(),
                thresholdsAltered = Array.Empty<object>(),
            },
            recency = holdout.Select(h => new { repoId = h.RepoId, stratum = "never-trained" }),
            findings = new[] { Finding(holdout[0].RepoId, new string('f', 40)) },
        };

        var (_, body) = await PostAsync(submission);

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("sha", string.Join(" ",
            body.GetProperty("problems").EnumerateArray().Select(p => p.GetString())),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ★★ REPOSITORIES THE RUN NEVER COVERED ARE REPORTED, NOT SILENTLY ACCEPTED. Scanning three of
    /// twelve and reporting a rate over them is the most obvious route to a flattering number, and it is
    /// invisible unless coverage publishes.
    /// </summary>
    [Fact]
    public async Task STAR_uncovered_repositories_are_reported()
    {
        var holdout = await HoldoutAsync();
        var submission = new
        {
            period = "2026-09",
            tool = Tool(),
            toolVersion = "v1",
            // ★ The run must have STARTED AFTER the draw was published (2026-08-15) — the ordering
            // the whole holdout rests on, and now checked rather than asserted in prose.
            runStartedAt = "2026-08-20T09:00:00Z",
            // ★ The configuration declaration, required since #23-1: every other check constrains the
            // RUN, none of them constrains which rules were switched on.
            configuration = new
            {
                rulesetId = "watchdog-default-2026.08",
                isProductDefault = true,
                rulesDisabled = Array.Empty<string>(),
                thresholdsAltered = Array.Empty<object>(),
            },
            recency = holdout.Select(h => new { repoId = h.RepoId, stratum = "never-trained" }),
            findings = new[] { Finding(holdout[0].RepoId, holdout[0].Sha) },
        };

        var (_, body) = await PostAsync(submission);

        var coverage = body.GetProperty("coverage");
        Assert.Equal(holdout.Count, coverage.GetProperty("holdoutRepositories").GetInt32());
        Assert.True(coverage.GetProperty("uncovered").GetArrayLength() > 0);
    }

    /// <summary>
    /// ★ A run that covered everything says so explicitly. "Zero uncovered" has to be a published fact,
    /// not the absence of a complaint.
    /// </summary>
    [Fact]
    public async Task Full_coverage_is_stated_rather_than_implied_by_silence()
    {
        var (_, body) = await PostAsync(await ValidSubmissionAsync(Tool()));

        var coverage = body.GetProperty("coverage");
        Assert.Equal(0, coverage.GetProperty("uncovered").GetArrayLength());
        Assert.Equal(coverage.GetProperty("holdoutRepositories").GetInt32(),
                     coverage.GetProperty("coveredRepositories").GetInt32());
    }

    /// <summary>
    /// ★ The RECENCY DECLARATION is required. "Has this tool been developed against this repository?" is
    /// a property of the tool that only the vendor knows, and the pristine-vs-recent gap is the
    /// overfitting number — the most interesting figure the standard produces, and one no vendor would
    /// publish about itself unprompted.
    /// </summary>
    [Fact]
    public async Task STAR_a_submission_without_a_recency_declaration_is_refused()
    {
        var holdout = await HoldoutAsync();
        var submission = new
        {
            period = "2026-09",
            tool = Tool(),
            toolVersion = "v1",
            findings = holdout.Select(h => Finding(h.RepoId, h.Sha)),
        };

        var (_, body) = await PostAsync(submission);

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("recency", string.Join(" ",
            body.GetProperty("problems").EnumerateArray().Select(p => p.GetString())),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ★ Every finding declares its CLAIM CLASS, because a noise rate compares only tools making
    /// comparably falsifiable claims. Without it a pooled rate silently compares a pointwise tool with a
    /// statistical one, which is a category error the standard refuses to make.
    /// </summary>
    [Fact]
    public async Task STAR_a_finding_without_a_claim_class_is_refused()
    {
        var holdout = await HoldoutAsync();
        var submission = new
        {
            period = "2026-09",
            tool = Tool(),
            toolVersion = "v1",
            // ★ The run must have STARTED AFTER the draw was published (2026-08-15) — the ordering
            // the whole holdout rests on, and now checked rather than asserted in prose.
            runStartedAt = "2026-08-20T09:00:00Z",
            // ★ The configuration declaration, required since #23-1: every other check constrains the
            // RUN, none of them constrains which rules were switched on.
            configuration = new
            {
                rulesetId = "watchdog-default-2026.08",
                isProductDefault = true,
                rulesDisabled = Array.Empty<string>(),
                thresholdsAltered = Array.Empty<object>(),
            },
            recency = holdout.Select(h => new { repoId = h.RepoId, stratum = "never-trained" }),
            findings = new[]
            {
                new { repoId = holdout[0].RepoId, pinnedSha = holdout[0].Sha, filePath = "a.cs", line = 1, ruleId = "D4", title = "t", claimClass = "" },
            },
        };

        var (_, body) = await PostAsync(submission);

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("claim class", string.Join(" ",
            body.GetProperty("problems").EnumerateArray().Select(p => p.GetString())),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_claim_class_is_refused_rather_than_guessed()
    {
        var holdout = await HoldoutAsync();
        var submission = new
        {
            period = "2026-09",
            tool = Tool(),
            toolVersion = "v1",
            // ★ The run must have STARTED AFTER the draw was published (2026-08-15) — the ordering
            // the whole holdout rests on, and now checked rather than asserted in prose.
            runStartedAt = "2026-08-20T09:00:00Z",
            // ★ The configuration declaration, required since #23-1: every other check constrains the
            // RUN, none of them constrains which rules were switched on.
            configuration = new
            {
                rulesetId = "watchdog-default-2026.08",
                isProductDefault = true,
                rulesDisabled = Array.Empty<string>(),
                thresholdsAltered = Array.Empty<object>(),
            },
            recency = holdout.Select(h => new { repoId = h.RepoId, stratum = "never-trained" }),
            findings = new[] { Finding(holdout[0].RepoId, holdout[0].Sha, claimClass: "vibes") },
        };

        var (_, body) = await PostAsync(submission);

        Assert.False(body.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task A_submission_for_an_unpublished_period_is_not_found()
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/submissions",
            new { period = "1999-01", tool = "t", toolVersion = "v", recency = Array.Empty<object>(), findings = Array.Empty<object>() }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// ★★ NO WITHDRAWAL. Resubmitting for the same tool and period is refused — otherwise a vendor runs,
    /// dislikes the result, and quietly runs again, and the published set silently becomes "the results
    /// people were happy with".
    /// </summary>
    [Fact]
    public async Task STAR_a_second_submission_for_the_same_tool_and_period_is_refused()
    {
        var holdout = await HoldoutAsync();
        object Payload(string tool) => new
        {
            period = "2026-09",
            tool,
            toolVersion = "v1",
            // ★ The run must have STARTED AFTER the draw was published (2026-08-15) — the ordering
            // the whole holdout rests on, and now checked rather than asserted in prose.
            runStartedAt = "2026-08-20T09:00:00Z",
            // ★ The configuration declaration, required since #23-1: every other check constrains the
            // RUN, none of them constrains which rules were switched on.
            configuration = new
            {
                rulesetId = "watchdog-default-2026.08",
                isProductDefault = true,
                rulesDisabled = Array.Empty<string>(),
                thresholdsAltered = Array.Empty<object>(),
            },
            recency = holdout.Select(h => new { repoId = h.RepoId, stratum = "never-trained" }),
            findings = holdout.Select(h => Finding(h.RepoId, h.Sha)),
        };

        var first = await PostAsync(Payload(Tool()));
        Assert.True(first.Body.GetProperty("accepted").GetBoolean());

        var (status, body) = await PostAsync(Payload(Tool()));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("already", body.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ★★ "ACCEPTED" MUST NOT BE READABLE AS "COMPLETE". A run covering two of twelve repositories is
    /// well-formed — every finding cites a drawn repo at the published sha — so it is accepted, and
    /// refusing it outright would push a vendor whose tool genuinely lacks a language into not
    /// participating at all.
    /// </summary>
    /// <remarks>
    /// But a receipt saying only <c>accepted: true</c> can be quoted as a clean bill of health by a
    /// vendor who scanned a sixth of the holdout. Found by reading the API's own output rather than by a
    /// test: the coverage numbers were right there and the headline flag still said yes.
    /// <para>So completeness is its own published flag, and partial coverage says so in words.</para>
    /// </remarks>
    [Fact]
    public async Task STAR_a_partial_run_is_accepted_but_NOT_marked_complete()
    {
        var holdout = await HoldoutAsync();
        var submission = new
        {
            period = "2026-09",
            tool = Tool(),
            toolVersion = "v1",
            // ★ The run must have STARTED AFTER the draw was published (2026-08-15) — the ordering
            // the whole holdout rests on, and now checked rather than asserted in prose.
            runStartedAt = "2026-08-20T09:00:00Z",
            // ★ The configuration declaration, required since #23-1: every other check constrains the
            // RUN, none of them constrains which rules were switched on.
            configuration = new
            {
                rulesetId = "watchdog-default-2026.08",
                isProductDefault = true,
                rulesDisabled = Array.Empty<string>(),
                thresholdsAltered = Array.Empty<object>(),
            },
            recency = holdout.Select(h => new { repoId = h.RepoId, stratum = "never-trained" }),
            findings = holdout.Take(2).Select(h => Finding(h.RepoId, h.Sha)),
        };

        var (_, body) = await PostAsync(submission);

        Assert.True(body.GetProperty("accepted").GetBoolean(), "it is well-formed");
        Assert.False(body.GetProperty("complete").GetBoolean(), "but it covered a fraction of the holdout");
        Assert.Contains("coverage", body.GetProperty("completenessNote").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_full_run_is_marked_complete()
    {
        var (_, body) = await PostAsync(await ValidSubmissionAsync(Tool()));

        Assert.True(body.GetProperty("accepted").GetBoolean());
        Assert.True(body.GetProperty("complete").GetBoolean());
    }
}
