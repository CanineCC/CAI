using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The rolling twelve-month figure: the number a reader can actually trust.
/// </summary>
/// <remarks>
/// <para>★★ A SINGLE PERIOD'S INTERVAL IS WIDE ENOUGH TO HIDE MOST MOVEMENTS. On 1,800 judged findings a
/// two-point change sits comfortably inside the Wilson interval, and the minimum detectable difference computed
/// over repositories is wider still — so period-to-period comparison is mostly noise about noise. Pooling twelve
/// periods is what makes a trend legible, and 02 §5 lists it as required with every rate.</para>
///
/// <para>★★ AND IT MUST SAY HOW MANY PERIODS IT SPANS. A "twelve-month figure" computed over three periods is not
/// a twelve-month figure; presenting one as the other is the most natural way this number goes wrong, because it
/// looks identical and is available from month one. The span publishes with it, and a short window says so.</para>
/// </remarks>
public sealed class RollingFigureTests
{
    private static PeriodTally P(string period, int judged, int noise) => new(period, judged, noise);

    [Fact]
    public void STAR_Twelve_Periods_Pool_Into_One_Rate_With_Its_Interval()
    {
        var tallies = Enumerable.Range(1, 12)
            .Select(i => P($"2026-{i:D2}", 1000, 200))
            .ToList();

        var rolling = RollingFigure.Compute(tallies, throughPeriod: "2026-12");

        Assert.Equal(12, rolling.Periods);
        Assert.Equal(12_000, rolling.Judged);
        Assert.Equal(0.2, rolling.Rate!.Value, 6);

        // ★ Over 12,000 observations the interval is tight — which is the entire reason for pooling.
        Assert.NotNull(rolling.IntervalLow);
        Assert.True(rolling.IntervalHigh! - rolling.IntervalLow! < 0.02);
        Assert.True(rolling.SpansTheFullWindow);
    }

    [Fact]
    public void STAR_A_Short_Window_Says_So_Rather_Than_Passing_As_Twelve_Months()
    {
        // ★★ THE FAILURE THAT LOOKS IDENTICAL TO SUCCESS. Three periods pooled is a real, useful figure and it
        // is NOT a twelve-month figure. Available from month one, quoted as the annual number, and nothing in
        // the value itself gives it away.
        var rolling = RollingFigure.Compute(
            [P("2026-10", 1000, 200), P("2026-11", 1000, 200), P("2026-12", 1000, 200)],
            throughPeriod: "2026-12");

        Assert.Equal(3, rolling.Periods);
        Assert.False(rolling.SpansTheFullWindow);
        Assert.NotNull(rolling.Rate);
        Assert.Contains("3 of 12", rolling.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public void STAR_Only_The_TWELVE_Periods_Ending_At_THIS_One_Are_Pooled()
    {
        // ★★ A ROLLING window, not "everything we have". Pooling thirty periods would make the figure
        // insensitive to exactly the change a reader is looking for, and a tool that improved a year ago would
        // carry its old rate for ever.
        var tallies = Enumerable.Range(1, 24)
            .Select(i => P($"{2025 + (i - 1) / 12}-{(i - 1) % 12 + 1:D2}", 1000, i <= 12 ? 400 : 100))
            .ToList();

        var rolling = RollingFigure.Compute(tallies, throughPeriod: "2026-12");

        Assert.Equal(12, rolling.Periods);
        Assert.Equal(0.1, rolling.Rate!.Value, 6);   // the recent twelve only
        Assert.Equal("2026-01", rolling.FirstPeriod);
        Assert.Equal("2026-12", rolling.LastPeriod);
    }

    [Fact]
    public void STAR_A_Period_AFTER_The_One_Asked_For_Is_Not_Pooled_Into_It()
    {
        // ★★ Otherwise a correction published later reaches backwards into an older rolling figure, and a
        // reader re-deriving last quarter's number gets a different answer than the one that was published.
        var rolling = RollingFigure.Compute(
            [P("2026-11", 1000, 100), P("2026-12", 1000, 100), P("2027-06", 1000, 900)],
            throughPeriod: "2026-12");

        Assert.Equal(2, rolling.Periods);
        Assert.Equal(0.1, rolling.Rate!.Value, 6);
    }

    [Fact]
    public void No_Periods_Means_No_Figure_Rather_Than_Zero()
    {
        var rolling = RollingFigure.Compute([], throughPeriod: "2026-12");

        Assert.Equal(0, rolling.Periods);
        Assert.Null(rolling.Rate);
        Assert.Null(rolling.IntervalLow);
        Assert.NotNull(rolling.Note);
    }

    [Fact]
    public void STAR_A_Period_Appearing_Twice_Is_Counted_ONCE()
    {
        // ★★ Publications are APPEND-ONLY, so a corrected period has two rows. Pooling both would double that
        // period's weight in the rolling figure and let a correction quietly re-weight the year.
        var rolling = RollingFigure.Compute(
            [P("2026-12", 1000, 500), P("2026-12", 1000, 100)],
            throughPeriod: "2026-12");

        Assert.Equal(1, rolling.Periods);

        // ★ And it is the LATEST row that counts — a correction supersedes, it does not average.
        Assert.Equal(0.1, rolling.Rate!.Value, 6);
    }

    [Fact]
    public void STAR_The_Window_Is_TWELVE_CALENDAR_MONTHS_Not_Twelve_Publications()
    {
        // ★★ THE MISLABELLING THIS FIGURE EXISTS TO PREVENT, from the other direction. A quarterly publisher has
        // twelve publications spanning THREE YEARS; pooling them and calling the result a twelve-month figure
        // would be false in exactly the way the span line is there to stop. Only the four inside the window
        // count, and the span says four.
        var quarterly = new List<PeriodTally>();
        for (var year = 2024; year <= 2026; year++)
        {
            foreach (var month in new[] { "03", "06", "09", "12" })
            {
                quarterly.Add(P($"{year}-{month}", 1000, year == 2026 ? 100 : 500));
            }
        }

        var rolling = RollingFigure.Compute(quarterly, throughPeriod: "2026-12");

        Assert.Equal(4, rolling.Periods);              // 2026-03, -06, -09, -12
        Assert.Equal("2026-03", rolling.FirstPeriod);
        Assert.Equal(0.1, rolling.Rate!.Value, 6);     // the 2026 quarters only
        Assert.False(rolling.SpansTheFullWindow);
    }

    [Fact]
    public void A_Period_That_Is_Not_yyyy_MM_Has_No_Window()
    {
        // ★ A window cannot be placed around a period that is not a month. Said, rather than falling back to
        // "the last twelve rows", which would silently be a different figure under the same name.
        var rolling = RollingFigure.Compute([P("2026-12", 1000, 100)], throughPeriod: "cycle-7");

        Assert.Equal(0, rolling.Periods);
        Assert.Contains("yyyy-MM", rolling.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Window_Is_Published_As_A_Number()
    {
        Assert.Equal(12, RollingFigure.WindowMonths);
    }
}

/// <summary>
/// The spot-check outcome and the rolling figure, on the published result.
/// </summary>
/// <remarks>
/// <para>★★ 02 §5 LISTS BOTH AS REQUIRED WITH EVERY RATE, and both existed only in side endpoints — the
/// spot-check at <c>/crowd/results/{period}</c> and the rolling figure nowhere. A reader quoting the published
/// number got neither, which is exactly the reader the two figures exist for: the spot-check says whether the
/// judges agreeing made them right, and the rolling figure is the only one whose interval is narrow enough to
/// support a claim about a trend.</para>
///
/// <para>★★ BOTH ARE READ BY CAI, never declared. The rolling figure comes from the append-only publication
/// store; the spot-check from the crowd round. A publication able to state its own spot-check agreement would be
/// stating the one number that checks it.</para>
/// </remarks>
public sealed class RollingAndSpotCheckPublicationTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <remarks>
    /// ★★ EXCLUSIONS SCALE WITH `judged`. A flat 60 excluded is 3.2 % of 1,860 and 10.7 % of 560 — above the
    /// ceiling that VOIDS a run — so a fixture with a fixed exclusion count silently stops publishing as soon as
    /// a test uses a smaller sample. Two tests here failed that way, and the refusal arrived as a missing JSON
    /// key rather than as a message about exclusions.
    /// </remarks>
    private static Dictionary<string, object?> Run(string period, int judged = 1800, int noise = 540) => new()
    {
        ["period"] = period,
        ["reported"] = judged + 200,
        ["adjudicated"] = judged,
        ["excluded"] = judged / 50,
        ["unrated"] = 200 - judged / 50,
        ["validAndActionable"] = judged - noise - 100,
        ["validNotActionable"] = 100,
        ["noise"] = noise,
        ["clusters"] = 10,
        ["locCovered"] = 4_200_000L,
        ["recallEstimate"] = 0.62,
        ["recallMethod"] = "pooled-union",
        ["claimClasses"] = new object[] { new { claimClass = "pointwise", judged, noise } },
        ["toolVersion"] = "watchdog-engine 2026.08.3",
        ["holdoutSeed"] = "cai-2026-09-9f2b41c7e0a85d36",
        ["modelSet"] = "judge-a@2026-07",
        ["gitMiningVerified"] = true,
        ["configuration"] = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
        ["fixRateUnavailable"] = "fixture",
        ["rejudgeUnavailable"] = "fixture",
    };

    private async Task<JsonElement> PublishAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/publication", payload, Ct);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    [Fact]
    public async Task STAR_The_First_Publication_Says_Its_Window_Is_One_Period_Long()
    {
        // ★★ The honest first answer. The figure exists from month one and is not the annual number, and only
        // this line separates them.
        var body = await PublishAsync(Run("2031-01"));

        var rolling = body.GetProperty("twelveMonth");
        Assert.Equal(1, rolling.GetProperty("periods").GetInt32());
        Assert.False(rolling.GetProperty("spansTheFullWindow").GetBoolean());
        Assert.Contains("1 of 12", rolling.GetProperty("note").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_A_Second_Period_Pools_With_The_First_And_Narrows_The_Interval()
    {
        // ★★ THE WHOLE VALUE OF THE FIGURE, demonstrated: two periods pooled have a narrower interval than
        // either alone, and the rolling rate sits between the two rates rather than replacing them.
        var first = await PublishAsync(Run("2033-03", judged: 1000, noise: 100));   // 10 %
        var second = await PublishAsync(Run("2033-04", judged: 1000, noise: 300));  // 30 %

        var rolling = second.GetProperty("twelveMonth");
        Assert.Equal(2, rolling.GetProperty("periods").GetInt32());
        Assert.Equal(2000, rolling.GetProperty("judged").GetInt32());
        Assert.Equal(0.2, rolling.GetProperty("rate").GetDouble(), 6);
        Assert.Equal("2033-03", rolling.GetProperty("firstPeriod").GetString());

        var pooledWidth = rolling.GetProperty("intervalHigh").GetDouble()
                        - rolling.GetProperty("intervalLow").GetDouble();
        var singleWidth = first.GetProperty("noiseRateInterval").GetProperty("high").GetDouble()
                        - first.GetProperty("noiseRateInterval").GetProperty("low").GetDouble();
        Assert.True(pooledWidth < singleWidth, $"pooled {pooledWidth} should be tighter than {singleWidth}");
    }

    [Fact]
    public async Task STAR_The_Rolling_Figure_Includes_THIS_Period_Before_It_Is_Stored()
    {
        // ★★ The publication being computed is not in the store yet. Left out, the "rolling figure published
        // with a rate" would exclude that very rate — visibly wrong on the first period and subtly wrong for
        // ever after.
        var body = await PublishAsync(Run("2035-06", judged: 500, noise: 250));

        Assert.Equal(500, body.GetProperty("twelveMonth").GetProperty("judged").GetInt32());
    }

    [Fact]
    public async Task STAR_A_Period_With_No_Crowd_Round_Says_The_Spot_Check_Was_Not_Run()
    {
        // ★ An absence, never a blank. "No spot-check was run" and "the spot-check found no contradictions"
        // are opposite claims and look identical when one of them is a missing field.
        var body = await PublishAsync(Run("2037-07"));

        var spot = body.GetProperty("spotCheck");
        Assert.False(spot.GetProperty("run").GetBoolean());
        Assert.Contains("no crowd round", spot.GetProperty("note").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Spot_Check_Publishes_SEPARATELY_From_The_Contested_Tail()
    {
        // ★★ NEVER MERGED. The contested items are hard by construction; the auto-accepted sample is the
        // pipeline's own claim about itself. A combined figure would hide exactly the disagreement rate on
        // auto-accepted findings that the layer exists to measure — and it is the one that would get quoted.
        const string period = "2039-08";
        using var client = fx.Client();

        // ★ `needs-human`, not "contested": the wire states are accepted / needs-round2 / needs-human, and
        // "contested" is what the QUEUE calls anything that is not accepted. A first draft used the queue's
        // vocabulary on the wire, got a 400, and the failure surfaced three calls later as "no crowd queue is
        // registered" — so the registration is asserted here rather than assumed.
        var registered = await client.PostAsJsonAsync("/api/noise/crowd/queue", new
        {
            period,
            seed = "seed-2039-08",
            spotCheck = 2,
            candidates = new object[]
            {
                new { findingId = "acc-1", state = "accepted", ownerId = "o1" },
                new { findingId = "acc-2", state = "accepted", ownerId = "o2" },
                new { findingId = "con-1", state = "needs-human", ownerId = "o3" },
            },
        }, Ct);
        Assert.True(registered.IsSuccessStatusCode,
            await registered.Content.ReadAsStringAsync(Ct));

        // ★★ THROUGH THE OFFER PATH. An answer to a finding a rater was never handed is refused — without that
        // the queue is only a suggestion — so the test cannot pick which finding it answers. It also cannot know
        // which reason it was handed: the item carries the finding and nothing else, deliberately, because told
        // that four judges agreed a reasonable person rubber-stamps. So the assertions are about the SPLIT, not
        // about a chosen finding.
        var contradictions = 0;
        for (var i = 1; i <= 3; i++)
        {
            var raterId = $"r{i}";
            var next = await client.GetAsync($"/api/noise/crowd/next?period={period}&raterId={raterId}", Ct);
            if (next.StatusCode == HttpStatusCode.NoContent)
            {
                continue;
            }

            var nextBody = await next.Content.ReadAsStringAsync(Ct);
            Assert.True(next.IsSuccessStatusCode, $"/crowd/next answered {(int)next.StatusCode}: {nextBody}");
            var offered = JsonDocument.Parse(nextBody).RootElement.GetProperty("findingId").GetString();

            // ★ One rater disagrees with the machine; the rest confirm.
            var disagree = i == 1;
            if (disagree) { contradictions++; }

            await client.PostAsJsonAsync("/api/noise/crowd/answers", new
            {
                period, raterId, findingId = offered,
                verdict = disagree ? "valid-actionable" : "noise",
                machineVerdict = "noise",
            }, Ct);
        }

        var body = await PublishAsync(Run(period));
        Assert.True(body.TryGetProperty("spotCheck", out _),
            "the publication was refused: " + body.ToString());

        var spot = body.GetProperty("spotCheck");
        var contested = body.GetProperty("contestedTail");

        // ★★ BOTH SLICES PRESENT AND SEPARATE. The queue is 2 spot-checked + 1 contested; the answers land in
        // whichever slice the rater was handed, and neither figure absorbs the other.
        Assert.True(spot.GetProperty("run").GetBoolean());
        Assert.True(contested.GetProperty("run").GetBoolean());
        Assert.Equal(2, spot.GetProperty("queued").GetInt32());
        Assert.Equal(1, contested.GetProperty("queued").GetInt32());
        Assert.Equal(3,
            spot.GetProperty("answered").GetInt32() + contested.GetProperty("answered").GetInt32());
        Assert.Equal(contradictions,
            spot.GetProperty("contradicted").GetInt32() + contested.GetProperty("contradicted").GetInt32());
    }

    [Fact]
    public async Task STAR_The_Method_Says_Both_Are_Published_With_Every_Rate()
    {
        using var client = fx.Client();
        var method = JsonDocument.Parse(await client.GetStringAsync("/api/noise/method", Ct)).RootElement;

        Assert.Equal(RollingFigure.WindowMonths,
            method.GetProperty("twelveMonth").GetProperty("windowMonths").GetInt32());
        Assert.Contains("not yet a twelve-month figure",
            method.GetProperty("twelveMonth").GetProperty("shortWindowRule").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }
}
