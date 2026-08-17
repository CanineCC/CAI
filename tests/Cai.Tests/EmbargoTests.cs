using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The embargo: no participant sees another's result before the period publishes.
/// </summary>
/// <remarks>
/// <para>★★ 03 COMMITS TO IT AS ONE OF THE FOUR THINGS THAT MAKE OUR CONFLICT OF INTEREST SURVIVABLE, and
/// <c>/api/noise/record/{period}</c> served everything to everyone immediately. Watchdog owns the standard and
/// participates in it: early sight of a rival's result is the single most valuable thing that position could be
/// worth, and "we would not look" is exactly the assurance nobody should have to accept.</para>
///
/// <para>★★ THE DATE IS IN THE SIGNED MANIFEST. An embargo whose lift date can be edited is not an embargo — it is
/// a promise to lift it when convenient. It sits beside the draw, covered by the same signature.</para>
///
/// <para>★ WATCHDOG IS BOUND BY IT. There is no exemption in the filter, and the test that matters is the one
/// where a principal reads a period holding somebody else's submission.</para>
/// </remarks>
public sealed class EmbargoTests
{
    private static readonly DateTimeOffset Publishes = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void STAR_Before_The_Date_A_Participant_Sees_Only_Its_OWN()
    {
        var before = Publishes.AddDays(-1);

        Assert.True(Embargo.IsInForce(Publishes, before));
        Assert.True(Embargo.MayRead("watchdog", "watchdog", Publishes, before));
        Assert.False(Embargo.MayRead("watchdog", "a-rival-scanner", Publishes, before));
    }

    [Fact]
    public void STAR_WATCHDOG_Is_Bound_By_It_Like_Everybody_Else()
    {
        // ★★ THE TEST THAT MATTERS. Watchdog owns the standard and competes in it, so an exemption here — however
        // it was worded — would make the embargo a courtesy rather than a rule. There is no caller name with a
        // different answer.
        var before = Publishes.AddDays(-1);

        foreach (var caller in new[] { "watchdog", "watchdog.canine.dev", "cai", "admin", "" })
        {
            Assert.False(Embargo.MayRead(caller, "a-rival-scanner", Publishes, before),
                $"'{caller}' could read another participant's result before the date");
        }
    }

    [Fact]
    public void STAR_After_The_Date_Everything_Publishes_To_Everyone()
    {
        var after = Publishes.AddSeconds(1);

        Assert.False(Embargo.IsInForce(Publishes, after));
        Assert.True(Embargo.MayRead("anyone-at-all", "a-rival-scanner", Publishes, after));

        // ★ Including an anonymous reader: after the lift the record is public, which is the whole point of
        // publishing it.
        Assert.True(Embargo.MayRead(null, "a-rival-scanner", Publishes, after));
    }

    [Fact]
    public void STAR_AT_The_Date_It_Has_LIFTED()
    {
        // ★ The boundary in the permissive direction. An embargo that outlasted its published date by a tick would
        // be a different date from the one published, and the discrepancy would only ever be noticed by somebody
        // it inconvenienced.
        Assert.False(Embargo.IsInForce(Publishes, Publishes));
        Assert.True(Embargo.MayRead("anyone", "other", Publishes, Publishes));
    }

    [Fact]
    public void STAR_A_Period_With_NO_Publication_Date_Is_Embargoed_Rather_Than_Open()
    {
        // ★★ FAIL CLOSED. A missing date means nobody has said when this period publishes — and reading "no date"
        // as "publish immediately" would make every period whose manifest entry was incomplete a leak, silently.
        var anyTime = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(Embargo.IsInForce(null, anyTime));
        Assert.False(Embargo.MayRead("watchdog", "a-rival-scanner", null, anyTime));
        Assert.True(Embargo.MayRead("watchdog", "watchdog", null, anyTime));
    }

    [Fact]
    public void STAR_An_ANONYMOUS_Reader_Sees_Nothing_Before_The_Date()
    {
        // ★★ "Only their own" needs an identity to be anybody's own. An unauthenticated caller has no submissions,
        // so it sees none — and the alternative, treating anonymous as a participant, would let anyone read
        // everything by presenting nothing.
        var before = Publishes.AddDays(-1);

        Assert.False(Embargo.MayRead(null, "watchdog", Publishes, before));
        Assert.False(Embargo.MayRead("", "watchdog", Publishes, before));
    }

    [Fact]
    public void The_Caller_Match_Is_Case_Insensitive_But_Not_Fuzzy()
    {
        var before = Publishes.AddDays(-1);

        Assert.True(Embargo.MayRead("WatchDog", "watchdog", Publishes, before));

        // ★ A prefix is not a match: "watch" must not read "watchdog"'s material.
        Assert.False(Embargo.MayRead("watch", "watchdog", Publishes, before));
    }
}

/// <summary>
/// The embargo over the wire: the register is withheld until the period publishes.
/// </summary>
/// <remarks>
/// ★★ THE REGISTER IS THE ONE PART OF THE RECORD ATTRIBUTABLE TO A PARTICIPANT — it names each tool, when it
/// submitted, and whether the run was accepted. The judging beside it carries no tool at all, so it cannot be read
/// as anybody's result. That is why the embargo lands here and only here.
/// </remarks>
public sealed class EmbargoApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonElement> RecordAsync(string period)
    {
        using var client = fx.Client();
        return JsonDocument.Parse(await client.GetStringAsync($"/api/noise/record/{period}", Ct))
            .RootElement.Clone();
    }

    /// <summary>Submit a real run, so there is something in the register to withhold.</summary>
    /// <remarks>
    /// ★★ THE TEST WAS VACUOUS WITHOUT THIS. It asserted the register was empty in a fixture where nothing had
    /// ever submitted — so removing the filter entirely still passed. Found by mutating the filter away and
    /// watching nothing fail, which is the only way that class of test ever gets caught.
    /// </remarks>
    private async Task<string> SubmitAsync(string tool)
    {
        using var client = fx.Client();
        var holdout = JsonDocument.Parse(await client.GetStringAsync("/api/noise/holdout/2026-09", Ct))
            .RootElement.GetProperty("repositories").EnumerateArray()
            .Select(r => (Repo: r.GetProperty("repoId").GetString()!, Sha: r.GetProperty("pinnedSha").GetString()!))
            .ToList();

        var response = await client.PostAsJsonAsync("/api/noise/submissions", new
        {
            period = "2026-09",
            tool,
            toolVersion = "engine-1.0",
            runStartedAt = "2026-08-20T09:00:00Z",
            configuration = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
            recency = holdout.Select(h => new { repoId = h.Repo, stratum = "never-trained" }),
            findings = Array.Empty<object>(),
            reportedFindingCount = 0,
        }, Ct);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct))
            .RootElement.GetProperty("submissionId").GetString()!;
    }

    [Fact]
    public async Task STAR_A_Drawn_Period_Before_Its_Date_Withholds_The_REGISTER()
    {
        // ★★ 2026-09 is drawn and publishes 2026-10-01. A rival's run IS on the register — and an anonymous
        // reader, which is every reader since the noise endpoints take no credentials, sees none of it.
        var receiptId = await SubmitAsync("a-rival-scanner");

        var record = await RecordAsync("2026-09");
        var embargo = record.GetProperty("embargo");

        Assert.True(embargo.GetProperty("inForce").GetBoolean());
        Assert.Equal(
            NoiseCorpus.Draws["2026-09"].PublishesAt,
            embargo.GetProperty("publishesAt").GetDateTimeOffset());
        Assert.Contains("only its own", embargo.GetProperty("note").GetString()!,
            StringComparison.OrdinalIgnoreCase);

        // ★★ The register held something and served nothing.
        Assert.Empty(record.GetProperty("submissions").EnumerateArray());

        // ★ And the submitter can still read their OWN receipt, by an id only they hold.
        using var client = fx.Client();
        var receipt = JsonDocument.Parse(
            await client.GetStringAsync($"/api/noise/submissions/{receiptId}", Ct)).RootElement;
        Assert.Equal("a-rival-scanner", receipt.GetProperty("tool").GetString());
    }

    [Fact]
    public async Task STAR_The_JUDGING_Is_Not_Withheld_Because_It_Names_Nobody()
    {
        // ★★ The scope, asserted so a later change cannot quietly widen or narrow it. Verdicts and resolutions
        // carry a finding id and no tool, so they cannot be read as any participant's result — withholding them
        // would delay the open-judging promise for nothing.
        var record = await RecordAsync("2026-09");

        Assert.True(record.TryGetProperty("verdicts", out _));
        Assert.True(record.TryGetProperty("resolutions", out _));
        Assert.True(record.TryGetProperty("disputes", out _));
    }

    [Fact]
    public async Task STAR_A_Period_With_No_Draw_Is_Not_Embargoed()
    {
        // ★ A period the standard never drew is not one it measures — a submission against it is refused
        // outright, so no participant's material can exist there to protect. Embargoing it would withhold an
        // empty record and say nothing true.
        var record = await RecordAsync("emb-undrawn");

        Assert.False(record.GetProperty("embargo").GetProperty("inForce").GetBoolean());
    }
}
