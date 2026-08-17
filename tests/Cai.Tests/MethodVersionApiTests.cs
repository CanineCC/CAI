using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Who can change the method, and when.
/// </summary>
/// <remarks>
/// <para>★★ WITH NO GOVERNANCE BODY, THE HONEST ANSWER WAS "WATCHDOG, UNILATERALLY, AT ANY TIME". §01 already
/// requires versioning — "a standard that changes silently is worse than none" — but a version number records
/// that a change happened and constrains nothing about *when*. The failure case is specific and it is the one
/// every reader will watch for: publish a number, dislike it, change the method for the next period. Versioning
/// documents that perfectly and prevents nothing.</para>
///
/// <para>★★ THE RULE (#23-2): a method version takes effect from the NEXT holdout drawn, never the current one.
/// A change published after a holdout is drawn cannot apply to that holdout. It is §01's own neutrality
/// principle — "draw before results exist, prove the ordering" — applied to the method rather than the corpus,
/// and it removes the discretion without requiring a single meeting.</para>
///
/// <para>★ Every version carries a DATED RATIONALE. A history of version numbers with no reasons is a change
/// log that explains nothing, and the reason is the part a reader needs in order to judge whether the change
/// was self-serving.</para>
/// </remarks>
public sealed class MethodVersionApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonElement> MethodAsync(string? period = null)
    {
        using var client = fx.Client();
        var url = period is null ? "/api/noise/method" : $"/api/noise/method?period={period}";
        return JsonDocument.Parse(await client.GetStringAsync(url, Ct)).RootElement.Clone();
    }

    [Fact]
    public async Task STAR_The_Method_Publishes_Its_Whole_VERSION_HISTORY()
    {
        // ★★ Not just the current version. A reader judging whether a change was self-serving needs to see when
        // it was announced, which period it first applied to, and why — and needs it for every version, because
        // the interesting one is always the one before the number somebody disliked.
        var method = await MethodAsync();

        var versions = method.GetProperty("versions").EnumerateArray().ToList();
        Assert.NotEmpty(versions);
        foreach (var v in versions)
        {
            Assert.False(string.IsNullOrWhiteSpace(v.GetProperty("version").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(v.GetProperty("effectiveFromPeriod").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(v.GetProperty("rationale").GetString()));
            Assert.True(v.GetProperty("announcedAt").GetDateTimeOffset() > DateTimeOffset.MinValue);
        }
    }

    [Fact]
    public async Task STAR_The_Change_Control_Rule_Itself_Publishes()
    {
        // ★ A rule nobody can read is not a constraint on us. It is the answer to "who can change this", so it
        // belongs beside the versions rather than in a document a reader has to be handed separately.
        var method = await MethodAsync();

        Assert.True(method.GetProperty("versionTakesEffectFromNextHoldout").GetBoolean());
        Assert.Contains("cannot apply", method.GetProperty("changeControlRule").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_Asking_For_A_PERIOD_Answers_With_The_Version_That_Governed_It()
    {
        // ★★ The whole point. A period is judged by the method in force when its holdout was drawn, so a reader
        // re-deriving an old number must be able to ask which version that was — otherwise "versioned" means
        // "we tell you what the rules are now".
        var forPeriod = await MethodAsync("2026-09");

        Assert.Equal("2026-09", forPeriod.GetProperty("period").GetString());
        Assert.False(string.IsNullOrWhiteSpace(forPeriod.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(forPeriod.GetProperty("rationale").GetString()));
    }

    [Fact]
    public async Task A_Period_Before_Any_Version_Was_In_Force_Says_So()
    {
        // ★ Null, never the earliest version. Claiming a version governed a period that predates it is exactly
        // the retroactive application the rule forbids — in the other direction.
        var forPeriod = await MethodAsync("2020-01");

        Assert.Equal(JsonValueKind.Null, forPeriod.GetProperty("version").ValueKind);
        Assert.Contains("no method version", forPeriod.GetProperty("note").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── The rule, as arithmetic ───────────────────────────────────────────────────────────────────

    [Fact]
    public void STAR_A_Version_Announced_AFTER_A_Draw_Cannot_Claim_That_Period()
    {
        // ★★ THE FAILURE THE RULE EXISTS FOR: publish a number, dislike it, and back-date a method change onto
        // the period it came from. The check is mechanical — the announcement must predate the draw — so it
        // needs no meeting and no good intentions.
        var offending = new MethodVersionRecord(
            "noise-1.1",
            AnnouncedAt: new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero),   // after the draw
            EffectiveFromPeriod: "2026-09",
            Rationale: "tightened the exclusion ceiling");

        var problems = MethodVersions.Validate([offending], NoiseCorpus.Draws);

        Assert.Contains(problems, p => p.Contains("2026-09", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("already drawn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_Version_Announced_Before_The_Draw_Is_Fine()
    {
        var ok = new MethodVersionRecord(
            "noise-1.1",
            AnnouncedAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveFromPeriod: "2026-09",
            Rationale: "first published method");

        Assert.Empty(MethodVersions.Validate([ok], NoiseCorpus.Draws));
    }

    [Fact]
    public void STAR_The_SHIPPED_History_Obeys_Its_Own_Rule()
    {
        // ★★ The guard that matters. A rule we publish and then break in our own history is worse than no rule,
        // and this is the test that fails the moment somebody adds a version with a convenient date.
        Assert.Empty(MethodVersions.Validate(MethodVersions.History, NoiseCorpus.Draws));
    }

    [Fact]
    public void STAR_In_Force_Is_The_LATEST_Version_Effective_At_Or_Before_The_Period()
    {
        // ★ Periods sort lexically as yyyy-MM, which is why that format is used. A version effective from
        // 2026-11 does not govern 2026-09, however recently it was announced.
        MethodVersionRecord[] history =
        [
            new("noise-1.0", new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), "2026-09", "first"),
            new("noise-1.1", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), "2026-11", "second"),
        ];

        Assert.Equal("noise-1.0", MethodVersions.InForceFor("2026-09", history)?.Version);
        Assert.Equal("noise-1.0", MethodVersions.InForceFor("2026-10", history)?.Version);
        Assert.Equal("noise-1.1", MethodVersions.InForceFor("2026-11", history)?.Version);
        Assert.Equal("noise-1.1", MethodVersions.InForceFor("2027-03", history)?.Version);
        Assert.Null(MethodVersions.InForceFor("2026-08", history));
    }

    // ── And it governs what publishes ─────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> Publication(string? period) => new()
    {
        ["period"] = period,
        ["reported"] = 2000,
        ["adjudicated"] = 1900,
        ["excluded"] = 60,
        ["unrated"] = 40,
        ["validAndActionable"] = 900,
        ["validNotActionable"] = 560,
        ["noise"] = 440,
        ["clusters"] = 14,
        ["locCovered"] = 4_200_000L,
        ["recallEstimate"] = 0.62,
        ["recallMethod"] = "pooled-union",
        ["claimClasses"] = new object[] { new { claimClass = "pointwise", judged = 1900, noise = 440 } },
        ["toolVersion"] = "watchdog-engine 2026.08.3",
        ["holdoutSeed"] = "cai-2026-08-a1b2c3",
        ["modelSet"] = "judge-a@2026-07",
        ["gitMiningVerified"] = true,
        ["configuration"] = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
        ["fixRateUnavailable"] = "fixture",
        // ★ The re-judge gate, required since #7b. Declared ABSENT rather than faked: a fixture that
        // asserted its own reproducibility would be the self-measured number the gate exists to stop.
        ["rejudgeUnavailable"] = "fixture: no second pass in this test — the re-judge has its own tests",
    };

    [Fact]
    public async Task STAR_A_Published_Number_Must_Name_Its_PERIOD()
    {
        // ★★ A rate without its period cannot be checked against the method that governed it, and #23-4 is
        // explicit that the number never appears without its interval and its period. The publication endpoint
        // accepted one with neither.
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/publication", Publication(period: null), Ct);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("period",
            string.Join(" ", body.GetProperty("breaches").EnumerateArray()
                .Select(b => b.GetProperty("field").GetString())),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Publication_Reports_The_Version_In_Force_For_ITS_Period()
    {
        // ★★ Not the newest version. A period judged under 1.0 publishes as judged under 1.0 for ever, which is
        // what makes a later change unable to reach back and reinterpret it.
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/publication", Publication("2026-09"), Ct);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-09", body.GetProperty("period").GetString());
        Assert.Equal(
            MethodVersions.InForceFor("2026-09", MethodVersions.History)!.Version,
            body.GetProperty("methodVersion").GetString());
    }
}
