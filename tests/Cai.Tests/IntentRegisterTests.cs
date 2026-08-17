using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Registering intent BEFORE the draw — the "before" the no-withdrawal rule works from.
/// </summary>
/// <remarks>
/// <para>★★ THE NO-WITHDRAWAL RULE HAD NOWHERE TO SEND ANYBODY. Its refusal already said "register intent before
/// the next draw instead", and there was no endpoint to do it at — so a vendor who submitted and disliked the
/// result was told to use a mechanism that did not exist. Worse, the rule without a before is only half a rule:
/// a vendor can simply never submit the periods that went badly, and the published set quietly becomes "the
/// results people were happy with".</para>
///
/// <para>★★ AND IT MUST CLOSE WHEN THE HOLDOUT IS DRAWN. Intent registered after seeing the draw is not intent —
/// it is a decision made with the sample in hand, which is the one thing the ordering exists to prevent. Refused
/// with the drawn date, so the refusal can be checked against the published draw.</para>
///
/// <para>★ It publishes. A register only CAI can see cannot embarrass anybody into submitting, which is its only
/// enforcement mechanism.</para>
/// </remarks>
public sealed class IntentRegisterTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Tool([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"intent-{caller}";

    private async Task<(HttpStatusCode Status, JsonElement Body)> RegisterAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/intent", payload, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task STAR_Intent_For_A_Future_Period_Is_Registered()
    {
        var (status, body) = await RegisterAsync(new { period = "2099-01", tool = Tool() });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("2099-01", body.GetProperty("period").GetString());
        Assert.Equal(Tool(), body.GetProperty("tool").GetString());
        Assert.True(body.GetProperty("registeredAt").GetDateTimeOffset() > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task STAR_Intent_Is_REFUSED_Once_That_Period_Has_Been_Drawn()
    {
        // ★★ THE WHOLE POINT. 2026-09's holdout is published, so intent for it would be a decision made with the
        // sample in hand — the one thing the ordering exists to prevent. The refusal carries the draw date, so a
        // reader can check it against the published draw rather than taking the refusal's word for it.
        var (status, body) = await RegisterAsync(new { period = "2026-09", tool = Tool() });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("already been drawn", body.GetProperty("error").GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            NoiseCorpus.Draws["2026-09"].DrawnAt,
            body.GetProperty("drawnAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_Registration_Needs_A_Period_And_A_Tool()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await RegisterAsync(new { tool = Tool() })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await RegisterAsync(new { period = "2099-02" })).Status);
    }

    [Fact]
    public async Task STAR_Registering_TWICE_Is_Not_An_Error_And_Keeps_The_FIRST_Time()
    {
        // ★★ The registration's value is its TIMESTAMP — "before the draw" is the claim it makes. A re-post that
        // moved the time forward would let a vendor register early, watch, and quietly refresh the record to a
        // moment that suited them. Idempotent, and the first time stands.
        var first = await RegisterAsync(new { period = "2099-03", tool = Tool() });
        await Task.Delay(15, Ct);
        var second = await RegisterAsync(new { period = "2099-03", tool = Tool() });

        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(
            first.Body.GetProperty("registeredAt").GetDateTimeOffset(),
            second.Body.GetProperty("registeredAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task STAR_The_Register_PUBLISHES_Who_Registered_And_When()
    {
        // ★ A register only CAI can see cannot embarrass anybody into submitting, which is its only enforcement.
        const string period = "2099-04";
        await RegisterAsync(new { period, tool = "alpha-scanner" });
        await RegisterAsync(new { period, tool = "beta-scanner" });

        using var client = fx.Client();
        var body = JsonDocument.Parse(await client.GetStringAsync($"/api/noise/intent/{period}", Ct)).RootElement;

        var tools = body.GetProperty("registered").EnumerateArray()
            .Select(r => r.GetProperty("tool").GetString())
            .ToList();

        Assert.Contains("alpha-scanner", tools);
        Assert.Contains("beta-scanner", tools);
        Assert.All(
            body.GetProperty("registered").EnumerateArray(),
            r => Assert.True(r.GetProperty("registeredAt").GetDateTimeOffset() > DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task STAR_A_Period_Nobody_Registered_For_Says_So_Rather_Than_Looking_Empty()
    {
        using var client = fx.Client();
        var body = JsonDocument.Parse(
            await client.GetStringAsync("/api/noise/intent/2099-11", Ct)).RootElement;

        Assert.Empty(body.GetProperty("registered").EnumerateArray());
        Assert.Contains("nobody has registered", body.GetProperty("note").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Register_Shows_Who_Registered_And_Then_Did_NOT_Submit()
    {
        // ★★ THE FIGURE THE REGISTER EXISTS FOR. A vendor who registered intent and then published nothing is the
        // exact case the no-withdrawal rule cannot catch on its own — they never submitted, so there is nothing to
        // withdraw. Naming them is the entire enforcement mechanism, and it costs nothing to compute.
        const string period = "2099-05";
        await RegisterAsync(new { period, tool = "silent-scanner" });

        using var client = fx.Client();
        var body = JsonDocument.Parse(await client.GetStringAsync($"/api/noise/intent/{period}", Ct)).RootElement;

        var silent = body.GetProperty("registered").EnumerateArray()
            .Single(r => r.GetProperty("tool").GetString() == "silent-scanner");

        Assert.False(silent.GetProperty("submitted").GetBoolean());
        Assert.Contains("registered and did not submit", body.GetProperty("note").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_No_Withdrawal_Refusal_Now_Points_At_A_Real_Endpoint()
    {
        // ★★ The refusal told vendors to "register intent before the next draw instead" and there was nowhere to
        // do it. A rule whose remedy does not exist is a rule that reads as an excuse.
        using var client = fx.Client();
        var method = JsonDocument.Parse(await client.GetStringAsync("/api/noise/method", Ct)).RootElement;

        var intent = method.GetProperty("intentRegister");
        Assert.Equal("/api/noise/intent", intent.GetProperty("endpoint").GetString());
        Assert.Contains("before", intent.GetProperty("closesWhen").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }
}
