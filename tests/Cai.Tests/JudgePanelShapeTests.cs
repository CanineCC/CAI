using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The panel's shape: four distinct models, more than one family, temperature zero.
/// </summary>
/// <remarks>
/// <para>★★ 02 §2: "a blind spot lives in the weights; no rephrasing removes it. A single-family ensemble cannot
/// see a single-family blind spot." The cascade recorded whatever it was handed — four votes from one model under
/// four judge names would have been stored as a judgement, and the record would have shown four agreeing judges
/// where there was one opinion counted four times. Ensemble agreement measures consistency, not correctness, and a
/// same-family ensemble does not even measure that much.</para>
///
/// <para>★★ DETERMINISM IS PART OF THE SPEC. A verdict produced at a non-zero temperature cannot be re-run to the
/// same answer, which makes "anyone may re-run the judges and get the same answers" (01 §3) false for it — and
/// nothing recorded the temperature, so a reader could not tell which verdicts that applied to.</para>
///
/// <para>★ Refused with the reason, like the other recording refusals: the cascade still RESOLVES (its own unit
/// tests are pure calculations), and what it will not do is record a judgement from a panel that breaks the
/// method.</para>
/// </remarks>
public sealed class JudgePanelShapeTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"panel-{caller}";

    /// <summary>One vote, complete unless a test breaks it deliberately.</summary>
    private static object Vote(
        string judge, string model, string family, string verdict = "noise",
        double? temperature = 0, string version = "2026-07-01") => new
    {
        judge, verdict, model, modelVersion = version, modelFamily = family, temperature,
        promptId = "p1", prompt = "Should this have fired?", reasoning = "the evidence shown supports it",
    };

    /// <summary>A panel that satisfies the method: four models, two families, temperature zero.</summary>
    private static object[] Round1() =>
    [
        Vote("judge-a", "gpt-judge", "openai"),
        Vote("judge-b", "claude-judge", "anthropic"),
    ];

    private static object[] Round2() =>
    [
        Vote("judge-c", "gemini-judge", "google"),
        Vote("judge-d", "llama-judge", "meta"),
    ];

    private async Task<JsonElement> ResolveAsync(string period, string findingId, object round1, object? round2)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/cascade/resolve", new
        {
            period, findingId, round1, round2,
        }, Ct);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    [Fact]
    public async Task STAR_A_Complete_Panel_Records()
    {
        // ★ The shape the method asks for: four distinct models across the rounds, more than one family,
        // temperature zero everywhere.
        var body = await ResolveAsync(Period(), "f-1", Round1(), Round2());

        Assert.True(body.GetProperty("recorded").GetBoolean(),
            string.Join(" | ", body.GetProperty("unrecordable").EnumerateArray().Select(x => x.GetString())));
    }

    [Fact]
    public async Task STAR_FOUR_Votes_From_ONE_Model_Are_Not_Four_Judges()
    {
        // ★★ THE FAILURE THIS EXISTS FOR. Four judge names, one model: the record would show four agreeing judges
        // where there was one opinion counted four times, and the agreement it reports would be the model agreeing
        // with itself.
        var body = await ResolveAsync(
            Period(), "f-2",
            new object[] { Vote("judge-a", "gpt-judge", "openai"), Vote("judge-b", "gpt-judge", "openai") },
            new object[] { Vote("judge-c", "gpt-judge", "openai"), Vote("judge-d", "gpt-judge", "openai") });

        Assert.False(body.GetProperty("recorded").GetBoolean());
        var why = string.Join(" ", body.GetProperty("unrecordable").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("same model", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_A_SINGLE_FAMILY_Panel_Cannot_See_A_Single_Family_Blind_Spot()
    {
        // ★★ 02 §2 verbatim: "a blind spot lives in the weights; no rephrasing removes it." Four DIFFERENT models
        // from one vendor is still one training tradition, and the check that four models agreed says nothing about
        // whether all four are wrong the same way.
        var body = await ResolveAsync(
            Period(), "f-3",
            new object[] { Vote("judge-a", "gpt-4o", "openai"), Vote("judge-b", "gpt-4.1", "openai") },
            new object[] { Vote("judge-c", "o3", "openai"), Vote("judge-d", "o4-mini", "openai") });

        Assert.False(body.GetProperty("recorded").GetBoolean());
        var why = string.Join(" ", body.GetProperty("unrecordable").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("family", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_An_UNDECLARED_Family_Is_Not_A_Different_Family()
    {
        // ★★ The default that would defeat the check. If a missing family counted as "some other family", every
        // panel would pass by omitting the field — and the requirement would be enforced only against submitters
        // who filled it in honestly.
        var body = await ResolveAsync(
            Period(), "f-4",
            new object[]
            {
                Vote("judge-a", "gpt-judge", "openai"),
                Vote("judge-b", "mystery-judge", family: ""),
            },
            new object[] { Vote("judge-c", "gemini-judge", "google"), Vote("judge-d", "llama-judge", "meta") });

        Assert.False(body.GetProperty("recorded").GetBoolean());
        var why = string.Join(" ", body.GetProperty("unrecordable").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("family", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_A_NON_ZERO_Temperature_Is_Refused()
    {
        // ★★ 01 §3 promises "anyone may re-run the judges and get the same answers". A verdict produced at
        // temperature 0.7 cannot be re-run to the same answer, so that promise is false for it — and the record
        // would not say which verdicts it was false for.
        var body = await ResolveAsync(
            Period(), "f-5",
            new object[]
            {
                Vote("judge-a", "gpt-judge", "openai", temperature: 0.7),
                Vote("judge-b", "claude-judge", "anthropic"),
            },
            Round2());

        Assert.False(body.GetProperty("recorded").GetBoolean());
        var why = string.Join(" ", body.GetProperty("unrecordable").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("temperature", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_An_UNDECLARED_Temperature_Is_Refused_Too()
    {
        // ★ Same reasoning as the family: a missing temperature that counted as zero would let every panel pass by
        // omitting the field, and determinism would be enforced only against the honest.
        var body = await ResolveAsync(
            Period(), "f-6",
            new object[]
            {
                Vote("judge-a", "gpt-judge", "openai", temperature: null),
                Vote("judge-b", "claude-judge", "anthropic"),
            },
            Round2());

        Assert.False(body.GetProperty("recorded").GetBoolean());
        var why = string.Join(" ", body.GetProperty("unrecordable").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("temperature", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_A_Round_One_SETTLE_Records_When_Its_Two_Models_Cross_Families()
    {
        // ★★ THE READING #10 NEEDED, and the reason is the cascade's own design: round two convenes ONLY when
        // round one has split, so requiring four models to RECORD would make the efficient path — most findings —
        // unrecordable. "Four distinct models across the two rounds" describes the FULL panel. The enforceable
        // rule with the same effect is no model twice plus at least two families: a round-one settle is then two
        // distinct models from two traditions. Recorded in 06-decisions.
        var body = await ResolveAsync(Period(), "f-7", Round1(), null);

        Assert.True(body.GetProperty("recorded").GetBoolean(),
            string.Join(" | ", body.GetProperty("unrecordable").EnumerateArray().Select(x => x.GetString())));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("state").GetString()));
    }

    [Fact]
    public async Task STAR_A_Round_One_Settle_From_ONE_Family_Is_Still_Refused()
    {
        // ★★ The half of the rule that survives the reading above. Two judges from one training tradition
        // agreeing is the single-family blind spot exactly — and it is the cheapest panel to convene, so it is the
        // one that would happen by default.
        var body = await ResolveAsync(
            Period(), "f-9",
            new object[] { Vote("judge-a", "gpt-4o", "openai"), Vote("judge-b", "gpt-4.1", "openai") },
            null);

        Assert.False(body.GetProperty("recorded").GetBoolean());
        var why = string.Join(" ", body.GetProperty("unrecordable").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("family", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Same_Model_Voting_Twice_Is_Refused_Even_In_A_Round_One_Pair()
    {
        // ★ One opinion counted twice, which is what the distinctness rule is actually about.
        var body = await ResolveAsync(
            Period(), "f-10",
            new object[] { Vote("judge-a", "gpt-judge", "openai"), Vote("judge-b", "gpt-judge", "anthropic") },
            null);

        Assert.False(body.GetProperty("recorded").GetBoolean());
        var why = string.Join(" ", body.GetProperty("unrecordable").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("same model", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_Resolver_Still_Works_With_No_Period_At_All()
    {
        // ★ The cascade's own unit tests call it as a pure calculation, with no period and no finding. The panel
        // requirement applies to RECORDING; it must not turn the resolver into something that needs a database.
        var body = await ResolveAsync("", "", Round1(), null);

        Assert.False(body.GetProperty("recorded").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("state").GetString()));
    }

    [Fact]
    public async Task STAR_The_Method_Publishes_The_Panel_Requirement()
    {
        using var client = fx.Client();
        var method = JsonDocument.Parse(await client.GetStringAsync("/api/noise/method", Ct)).RootElement;

        var panel = method.GetProperty("judgePanel");
        Assert.Equal(4, panel.GetProperty("distinctModels").GetInt32());
        Assert.True(panel.GetProperty("familiesRequired").GetInt32() >= 2);
        Assert.Equal(0d, panel.GetProperty("temperature").GetDouble());
        Assert.Contains("blind spot", panel.GetProperty("why").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_RECORD_Publishes_Each_Verdict_Family_And_Temperature()
    {
        // ★ A requirement whose inputs are not published cannot be checked by the reader it exists for. The family
        // and the temperature travel with the raw verdict, like the model version already does.
        var period = Period();
        await ResolveAsync(period, "f-8", Round1(), Round2());

        using var client = fx.Client();
        var record = JsonDocument.Parse(await client.GetStringAsync($"/api/noise/record/{period}", Ct)).RootElement;
        var verdict = record.GetProperty("verdicts").EnumerateArray().First();

        Assert.False(string.IsNullOrWhiteSpace(verdict.GetProperty("modelFamily").GetString()));
        Assert.Equal(0d, verdict.GetProperty("temperature").GetDouble());
    }
}
