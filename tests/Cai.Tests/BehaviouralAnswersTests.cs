using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Asking the crowd what it would DO, not which label applies.
/// </summary>
/// <remarks>
/// <para>★★ 02 §4: the spec is validated against what practitioners would do, rather than against their opinion of
/// the spec's own vocabulary. "Is this noise?" asks a rater to apply a taxonomy they did not write and may read
/// differently from the person who did — and the answer is then evidence about the vocabulary as much as about the
/// finding. "Would you fix this?" and "would you want this in a report?" are questions a working engineer answers
/// from experience, in a second, and they are also the two decisions the tool actually exists to inform.</para>
///
/// <para>★★ AND IT IS THE HONEST ANSWER TO A 9-SECOND MEDIAN REVIEW. A rater spending nine seconds is not
/// performing a taxonomy classification; they are reacting. Asking the question they are actually answering makes
/// the nine seconds evidence rather than a problem to be explained away.</para>
///
/// <para>★★ REPORTED SEPARATELY, AND THE DISAGREEMENT IS THE POINT. Where "not noise" meets "I would not fix it"
/// the spec and the practitioner have parted company — that gap is the most informative thing this layer produces,
/// and merging the two into one figure would destroy exactly it.</para>
/// </remarks>
public sealed class BehaviouralAnswersTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"beh-{caller}";

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    /// <summary>Register a queue and hand each rater their one item.</summary>
    private async Task<List<(string Rater, string Finding)>> QueueAsync(string period, int items)
    {
        using var client = fx.Client();

        await client.PostAsJsonAsync("/api/noise/crowd/queue", new
        {
            period,
            seed = $"seed-{period}",
            spotCheck = items,
            candidates = Enumerable.Range(1, items).Select(i => new
            {
                findingId = $"f-{i:D3}", state = "accepted", ownerId = $"o{i}",
            }),
        }, Ct);

        List<(string, string)> offers = [];
        for (var i = 1; i <= items; i++)
        {
            var rater = $"r{i}";
            var next = await client.GetAsync($"/api/noise/crowd/next?period={period}&raterId={rater}", Ct);
            if (next.StatusCode == HttpStatusCode.NoContent)
            {
                continue;
            }

            offers.Add((rater, (await JsonAsync(next)).GetProperty("findingId").GetString()!));
        }

        return offers;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> AnswerAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/crowd/answers", payload, Ct);
        return (response.StatusCode, await JsonAsync(response));
    }

    // ── Asking ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_The_Item_Handed_To_A_RATER_Carries_The_Questions()
    {
        // ★★ The rater has to be asked. The item deliberately carries the finding and nothing about the judges —
        // and the QUESTIONS are the one thing it must carry, or every client invents its own wording and the
        // answers stop being comparable between them.
        var period = Period();
        await QueueAsync(period, 1);

        using var client = fx.Client();
        var body = await JsonAsync(
            await client.GetAsync($"/api/noise/crowd/next?period={period}&raterId=asker", Ct));

        var questions = body.GetProperty("questions");
        Assert.Contains("fix", questions.GetProperty("wouldFix").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report", questions.GetProperty("wantInReport").GetString()!,
            StringComparison.OrdinalIgnoreCase);

        // ★ And still nothing about what the judges said — the disguise the spot-check depends on.
        Assert.False(body.TryGetProperty("machineVerdict", out _));
        Assert.False(body.TryGetProperty("reason", out _));
    }

    // ── Answering ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_Both_Behavioural_Answers_Are_Recorded()
    {
        var period = Period();
        var offers = await QueueAsync(period, 1);
        var (rater, finding) = offers[0];

        var (status, _) = await AnswerAsync(new
        {
            period, raterId = rater, findingId = finding,
            verdict = "valid-actionable", machineVerdict = "valid-actionable",
            wouldFix = true, wantInReport = false,
        });

        Assert.Equal(HttpStatusCode.OK, status);

        using var client = fx.Client();
        var results = await JsonAsync(await client.GetAsync($"/api/noise/crowd/results/{period}", Ct));
        var behaviour = results.GetProperty("behaviour");

        Assert.Equal(1, behaviour.GetProperty("answered").GetInt32());
        Assert.Equal(1, behaviour.GetProperty("wouldFix").GetInt32());
        Assert.Equal(0, behaviour.GetProperty("wantInReport").GetInt32());
    }

    [Fact]
    public async Task STAR_An_Answer_That_SKIPS_Them_Is_Counted_As_Unanswered_Not_As_No()
    {
        // ★★ THE DEFAULT THAT WOULD LIE. A missing "would you fix this?" folded into "no" would manufacture
        // evidence that practitioners would not act on findings they were never asked about — and the more raters
        // skipped the question, the stronger that false signal would get.
        var period = Period();
        var offers = await QueueAsync(period, 1);
        var (rater, finding) = offers[0];

        await AnswerAsync(new
        {
            period, raterId = rater, findingId = finding,
            verdict = "noise", machineVerdict = "noise",
        });

        using var client = fx.Client();
        var results = await JsonAsync(await client.GetAsync($"/api/noise/crowd/results/{period}", Ct));
        var behaviour = results.GetProperty("behaviour");

        Assert.Equal(0, behaviour.GetProperty("answered").GetInt32());
        Assert.Equal(1, behaviour.GetProperty("notAsked").GetInt32());
        Assert.Equal(JsonValueKind.Null, behaviour.GetProperty("wouldFixRate").ValueKind);
    }

    // ── Where the two disagree ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_The_Gap_Between_The_LABEL_And_THE_BEHAVIOUR_Is_Published()
    {
        // ★★ THE MOST INFORMATIVE THING THIS LAYER PRODUCES. A finding a rater calls VALID and would NOT fix is a
        // finding the spec counts as a success and the practitioner would ignore — and the reverse, noise somebody
        // would fix anyway, says the taxonomy is cutting in the wrong place. Merging the two into one figure
        // destroys exactly this.
        var period = Period();
        var offers = await QueueAsync(period, 4);

        // valid-but-would-not-fix ×2, noise-but-would-fix ×1, and one that agrees
        var answers = new (string Verdict, bool WouldFix)[]
        {
            ("valid-actionable", false),
            ("valid-actionable", false),
            ("noise", true),
            ("valid-actionable", true),
        };

        for (var i = 0; i < offers.Count && i < answers.Length; i++)
        {
            await AnswerAsync(new
            {
                period, raterId = offers[i].Rater, findingId = offers[i].Finding,
                verdict = answers[i].Verdict, machineVerdict = answers[i].Verdict,
                wouldFix = answers[i].WouldFix, wantInReport = answers[i].WouldFix,
            });
        }

        using var client = fx.Client();
        var results = await JsonAsync(await client.GetAsync($"/api/noise/crowd/results/{period}", Ct));
        var behaviour = results.GetProperty("behaviour");

        Assert.Equal(2, behaviour.GetProperty("validButWouldNotFix").GetInt32());
        Assert.Equal(1, behaviour.GetProperty("noiseButWouldFix").GetInt32());
        Assert.Contains("parted company", behaviour.GetProperty("note").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Behavioural_Answers_Are_NOT_Folded_Into_The_Noise_Rate()
    {
        // ★★ They are evidence ABOUT the taxonomy, not a second taxonomy. Counting "would not fix" as noise would
        // silently redefine the published rate — and the rate would then measure a mixture of two questions that
        // the whole point of asking both was to keep apart.
        var period = Period();
        var offers = await QueueAsync(period, 1);
        var (rater, finding) = offers[0];

        await AnswerAsync(new
        {
            period, raterId = rater, findingId = finding,
            verdict = "valid-actionable", machineVerdict = "valid-actionable",
            wouldFix = false, wantInReport = false,
        });

        using var client = fx.Client();
        var results = await JsonAsync(await client.GetAsync($"/api/noise/crowd/results/{period}", Ct));

        // The verdict slice still sees agreement: the rater and the machine both said valid.
        Assert.Equal(0, results.GetProperty("spotCheck").GetProperty("contradicted").GetInt32());
    }

    [Fact]
    public async Task STAR_The_Method_Publishes_The_Two_QUESTIONS_Verbatim()
    {
        // ★ Every client asks the same words, or the answers are not comparable between them — and a reader
        // weighing the figures needs to know exactly what was asked.
        using var client = fx.Client();
        var method = JsonDocument.Parse(await client.GetStringAsync("/api/noise/method", Ct)).RootElement;

        var behaviour = method.GetProperty("behaviouralQuestions");
        Assert.Contains("fix", behaviour.GetProperty("wouldFix").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report", behaviour.GetProperty("wantInReport").GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("what practitioners would do", behaviour.GetProperty("why").GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not folded", behaviour.GetProperty("relationToTheRate").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }
}
