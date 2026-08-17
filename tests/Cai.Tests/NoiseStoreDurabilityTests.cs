using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The submission register and the verdict record survive a restart — because both used to be dictionaries.
/// </summary>
/// <remarks>
/// <para>★★ THE NO-WITHDRAWAL RULE WAS DEFEATED BY A PROCESS RESTART, and the code said so in its own comment:
/// "a restart currently forgets that a vendor already submitted, which is precisely the hole the rule exists to
/// close". That rule is the standard's answer to the worst failure available to it — a vendor runs, dislikes the
/// result, and the published set quietly becomes "the results people were happy with". It was also the first
/// thing an unfriendly participant would have found.</para>
///
/// <para>★★ AND NO JUDGING WAS RECORDED AT ALL. 01 promises every prompt, model version and raw verdict with its
/// reasoning, published in full; the cascade resolved votes in memory and returned an answer. The claim a
/// sceptic tests first was the one with nothing behind it.</para>
///
/// <para>★ "Restart" here is a SECOND application built over the same database file, which is what a deploy or
/// a crash actually looks like. Two factories, one file: nothing in process survives, and everything the
/// standard promised to keep does.</para>
/// </remarks>
public sealed class NoiseStoreDurabilityTests : IDisposable
{
    private const string Period = "2026-09";     // the one period with a published draw

    private readonly string _root;
    private readonly string _dbPath;

    public NoiseStoreDurabilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cai-noise-store", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "cai.db");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A held file handle on a temp directory is not worth failing a test over.
        }
    }

    /// <summary>A fresh application over the SAME database file — a restart, in every way that matters.</summary>
    private WebApplicationFactory<Program> Restart() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Registry:DbPath"] = _dbPath,
                ["RateLimit:PartnerKey"] = RegistryUnconfiguredFixture.PartnerKey,
            })));

    private static HttpClient Client(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-CAI-Partner", RegistryUnconfiguredFixture.PartnerKey);
        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<object> SubmissionAsync(HttpClient client, string tool)
    {
        var holdout = JsonDocument.Parse(await client.GetStringAsync($"/api/noise/holdout/{Period}")).RootElement
            .GetProperty("repositories").EnumerateArray()
            .Select(r => r.GetProperty("repoId").GetString()!)
            .ToList();

        return new
        {
            period = Period,
            tool,
            toolVersion = "v1",
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
            recency = holdout.Select(r => new { repoId = r, stratum = "never-trained" }),
            findings = Array.Empty<object>(),
            reportedFindingCount = 0,
        };
    }

    [Fact]
    public async Task STAR_A_Submission_Still_Claims_Its_Slot_After_A_RESTART()
    {
        // ★★ The whole point. Before this, a restart forgot the claim and the same tool could submit again for
        // the same period — which is the no-withdrawal rule not existing.
        string? submissionId;

        await using (var first = Restart())
        {
            using var client = Client(first);
            var body = await JsonAsync(await client.PostAsJsonAsync(
                "/api/noise/submissions", await SubmissionAsync(client, "persistent-tool")));
            submissionId = body.GetProperty("submissionId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(submissionId));
        }

        await using var second = Restart();
        using var after = Client(second);

        // The receipt is still findable by id…
        var found = await JsonAsync(await after.GetAsync($"/api/noise/submissions/{submissionId}"));
        Assert.Equal("persistent-tool", found.GetProperty("tool").GetString());

        // …and a second attempt for the same tool and period is refused.
        var again = await after.PostAsJsonAsync(
            "/api/noise/submissions", await SubmissionAsync(after, "persistent-tool"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("cannot be", (await JsonAsync(again)).GetProperty("error").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_A_REJECTED_Run_Stays_On_The_Register_Without_Claiming_The_Slot()
    {
        // ★★ Two properties at once. It stays because it is EVIDENCE — a run a vendor would like to forget is
        // exactly the kind the rule keeps. It does not claim the slot because a rejected run is not a
        // submission that answered the holdout, and blocking a corrected one would punish fixing a mistake.
        await using var app = Restart();
        using var client = Client(app);

        // Rejected: an unknown recency stratum.
        var holdout = JsonDocument.Parse(await client.GetStringAsync($"/api/noise/holdout/{Period}")).RootElement
            .GetProperty("repositories").EnumerateArray()
            .Select(r => r.GetProperty("repoId").GetString()!).ToList();

        var bad = await JsonAsync(await client.PostAsJsonAsync("/api/noise/submissions", new
        {
            period = Period,
            tool = "second-chance",
            toolVersion = "v1",
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
            recency = holdout.Select(r => new { repoId = r, stratum = "quite-fresh" }),
            findings = Array.Empty<object>(),
            reportedFindingCount = 0,
        }));
        Assert.False(bad.GetProperty("accepted").GetBoolean());

        // A corrected submission is accepted — the slot was never claimed.
        var good = await client.PostAsJsonAsync(
            "/api/noise/submissions", await SubmissionAsync(client, "second-chance"));
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);

        // ★★ BOTH ARE ON THE REGISTER, including the refusal — but the register is EMBARGOED until the period
        // publishes (#15), so an anonymous reader sees neither. That is the embargo working, not the register
        // being edited: the receipts are still fetchable by their ids, which only the submitter holds.
        var record = await JsonAsync(await client.GetAsync($"/api/noise/record/{Period}"));
        Assert.True(record.GetProperty("embargo").GetProperty("inForce").GetBoolean());
        Assert.Empty(record.GetProperty("submissions").EnumerateArray());

        var refused = await JsonAsync(
            await client.GetAsync($"/api/noise/submissions/{bad.GetProperty("submissionId").GetString()}"));
        var accepted = await JsonAsync(
            await client.GetAsync(
                $"/api/noise/submissions/{(await JsonAsync(good)).GetProperty("submissionId").GetString()}"));

        Assert.False(refused.GetProperty("accepted").GetBoolean());
        Assert.True(accepted.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task STAR_The_Claim_Is_Enforced_By_The_DATABASE_Not_By_A_Check_Above_It()
    {
        // ★★ Two submissions racing for one slot. An in-process check followed by an insert cannot guarantee
        // that only one wins, and this is precisely the operation somebody has a motive to race. The partial
        // UNIQUE index means one insert fails, whichever order they arrive in.
        await using var app = Restart();
        using var client = Client(app);
        var payload = await SubmissionAsync(client, "racer");

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => client.PostAsJsonAsync("/api/noise/submissions", payload)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var r in results) { r.Dispose(); }
    }

    // ── The verdict record ────────────────────────────────────────────────────────────────────────

    private static object Vote(string judge, string verdict, string reasoning = "because the cited line does "
        + "dereference a value that may be null") => new
    {
        judge,
        verdict,

        // ★ Distinct model per judge, two families, temperature 0 — the panel shape #10 requires to record.
        model = judge == "judge-a" ? "gpt-judge" : "claude-judge",
        modelFamily = judge == "judge-a" ? "openai" : "anthropic",
        temperature = 0,
        modelVersion = "2026-07-01",
        promptId = "noise-judge-v3",
        prompt = "You are shown one finding and the code it cites. Decide whether it should have fired.",
        reasoning,
    };

    [Fact]
    public async Task STAR_A_Judged_Finding_Leaves_A_Record_That_SURVIVES_A_Restart()
    {
        // ★★ 01: "every judge prompt, every model and version, every raw verdict with its reasoning …
        // published in full. A reader who disagrees with a verdict must be able to find it, read the
        // reasoning, and say so." None of it was stored.
        await using (var first = Restart())
        {
            using var client = Client(first);
            var body = await JsonAsync(await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
            {
                period = Period,
                findingId = "fp-0001",
                round1 = new[] { Vote("judge-a", "noise"), Vote("judge-b", "noise") },
            }));

            Assert.True(body.GetProperty("recorded").GetBoolean());
            Assert.Empty(body.GetProperty("unrecordable").EnumerateArray());
        }

        await using var second = Restart();
        using var after = Client(second);
        var record = await JsonAsync(await after.GetAsync($"/api/noise/record/{Period}"));

        Assert.Equal(1, record.GetProperty("judged").GetInt32());
        Assert.Equal(2, record.GetProperty("rawVerdicts").GetInt32());

        var verdict = record.GetProperty("verdicts").EnumerateArray().First();
        Assert.Equal("fp-0001", verdict.GetProperty("findingId").GetString());
        Assert.Equal("gpt-judge", verdict.GetProperty("model").GetString());
        Assert.Equal("2026-07-01", verdict.GetProperty("modelVersion").GetString());
        Assert.Contains("dereference", verdict.GetProperty("reasoning").GetString()!, StringComparison.Ordinal);

        // ★ The prompt is published in full, and ONCE — the same prompt answers thousands of findings, and a
        // record that repeats it per verdict is a record nobody downloads.
        var prompts = record.GetProperty("prompts").EnumerateArray().ToList();
        Assert.Single(prompts);
        Assert.Contains("Decide whether it should have fired",
            prompts[0].GetProperty("text").GetString()!, StringComparison.Ordinal);

        var resolution = record.GetProperty("resolutions").EnumerateArray().First();
        Assert.Equal("noise", resolution.GetProperty("verdict").GetString());
        Assert.Equal(1, resolution.GetProperty("settledAtRound").GetInt32());
    }

    [Fact]
    public async Task STAR_A_Verdict_Without_Its_Model_Version_Or_Reasoning_Is_NOT_Recorded()
    {
        // ★★ Refused rather than half-stored. A verdict with no reasoning is one a reader cannot argue with,
        // and a record full of those would satisfy the letter of "published in full" while defeating it — the
        // endpoint says which votes were unrecordable and why.
        await using var app = Restart();
        using var client = Client(app);

        var body = await JsonAsync(await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
        {
            period = Period,
            findingId = "fp-0002",
            round1 = new object[]
            {
                new { judge = "judge-a", verdict = "noise" },       // no model, version, prompt or reasoning
                Vote("judge-b", "noise"),
            },
        }));

        Assert.False(body.GetProperty("recorded").GetBoolean());
        var reasons = string.Join(" ", body.GetProperty("unrecordable").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("judge-a", reasons, StringComparison.Ordinal);
        Assert.Contains("not open judging", reasons, StringComparison.Ordinal);

        // Nothing partial reached the record.
        var record = await JsonAsync(await client.GetAsync($"/api/noise/record/{Period}"));
        Assert.Equal(0, record.GetProperty("rawVerdicts").GetInt32());

        // ★ And the RESOLUTION still came back — the cascade is still a calculator when it cannot record.
        Assert.Equal("noise", body.GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task Without_A_Period_And_Finding_It_Stays_A_Pure_Resolver()
    {
        // ★ The cascade's own unit tests resolve votes without recording anything, and that has to keep
        // working: a calculation is not a judgement, and forcing every caller to name a finding would make the
        // arithmetic untestable.
        await using var app = Restart();
        using var client = Client(app);

        var body = await JsonAsync(await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
        {
            round1 = new[] { Vote("judge-a", "noise"), Vote("judge-b", "noise") },
        }));

        Assert.False(body.GetProperty("recorded").GetBoolean());
        Assert.Equal("noise", body.GetProperty("verdict").GetString());

        var record = await JsonAsync(await client.GetAsync($"/api/noise/record/{Period}"));
        Assert.Equal(0, record.GetProperty("rawVerdicts").GetInt32());
    }

    [Fact]
    public async Task STAR_An_Empty_Record_Says_So_Rather_Than_Looking_Clean()
    {
        // ★★ "Nothing has been judged for this period" and "everything was judged and agreed" are different
        // facts, and an empty list renders identically for both. The absence is named.
        await using var app = Restart();
        using var client = Client(app);

        var record = await JsonAsync(await client.GetAsync($"/api/noise/record/{Period}"));

        Assert.Equal(0, record.GetProperty("judged").GetInt32());
        Assert.Contains("this is an absence, not a clean run",
            record.GetProperty("note").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Re_Judge_Corrects_How_A_Finding_Settled_And_Keeps_Both_Raw_Verdicts()
    {
        // ★★ The raw verdicts are APPEND-ONLY — they are the evidence, and a re-judge does not unsay what a
        // judge said. "How it settled" has one current value, so that is replaced. Both properties matter: a
        // record that overwrote verdicts could hide a changed mind, and one that kept two resolutions would
        // leave a reader unable to say what the standard concluded.
        await using var app = Restart();
        using var client = Client(app);

        await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
        {
            period = Period, findingId = "fp-0003",
            round1 = new[] { Vote("judge-a", "noise"), Vote("judge-b", "noise") },
        });
        await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
        {
            period = Period, findingId = "fp-0003",
            round1 = new[] { Vote("judge-a", "valid-actionable"), Vote("judge-b", "valid-actionable") },
        });

        var record = await JsonAsync(await client.GetAsync($"/api/noise/record/{Period}"));
        Assert.Equal(1, record.GetProperty("judged").GetInt32());          // one resolution
        Assert.Equal(4, record.GetProperty("rawVerdicts").GetInt32());     // four raw verdicts
        Assert.Equal("valid-actionable",
            record.GetProperty("resolutions").EnumerateArray().First().GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task The_Method_Names_Where_The_Record_Is_Published()
    {
        // ★ A promise nobody can find is not kept. /method is what a participant reads before building.
        await using var app = Restart();
        using var client = Client(app);

        var method = JsonDocument.Parse(await client.GetStringAsync("/api/noise/method")).RootElement;

        Assert.Equal("/api/noise/record/{period}",
            method.GetProperty("verdictRecordEndpoint").GetString());
    }
}
