using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// How the tool was CONFIGURED — the hole every other check leaves open.
/// </summary>
/// <remarks>
/// <para>★★ VERIFICATION CONSTRAINS THE RUN, NOT THE CONFIGURATION. A submission is checked against the
/// holdout's repositories and shas, its provenance names the build, the model set and the seed, and the
/// recency declaration closes "did you develop against these repos". None of it says anything about which
/// rules were switched on. So a vendor could run the correct version against the correct shas with its
/// noisiest rules disabled, its thresholds relaxed, or a profile no customer is ever given, and pass every
/// check the method has.</para>
///
/// <para>★★ THE POINT IS NOT TRUST. It is the same as the recency declaration's: it turns a vague impression
/// into a specific, checkable, public claim that a competitor or a buyer can point at. Lying becomes an act
/// rather than an omission.</para>
///
/// <para>★ It binds us first. Watchdog is the only participant, so we are the first to state our
/// configuration and admit any divergence from what customers actually run.</para>
/// </remarks>
public sealed class SubmissionConfigurationApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private const string Period = "2026-09";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<List<string>> HoldoutAsync(HttpClient client) =>
        [.. JsonDocument.Parse(await client.GetStringAsync($"/api/noise/holdout/{Period}", Ct)).RootElement
            .GetProperty("repositories").EnumerateArray()
            .Select(r => r.GetProperty("repoId").GetString()!)];

    /// <summary>A submission that is correct in every respect except the configuration under test.</summary>
    private async Task<JsonElement> SubmitAsync(string tool, object? configuration)
    {
        using var client = fx.Client();
        var holdout = await HoldoutAsync(client);

        var response = await client.PostAsJsonAsync("/api/noise/submissions", new
        {
            period = Period,
            tool,
            toolVersion = "engine-1.0",
            runStartedAt = "2026-08-20T09:00:00Z",
            configuration,
            recency = holdout.Select(r => new { repoId = r, stratum = "never-trained" }),
            findings = Array.Empty<object>(),
            reportedFindingCount = 0,
        }, Ct);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    private static string Problems(JsonElement body) =>
        string.Join(" ", body.GetProperty("problems").EnumerateArray().Select(p => p.GetString()));

    /// <summary>The shipping default, declared as such.</summary>
    private static object Default() => new
    {
        rulesetId = "watchdog-default-2026.08",
        isProductDefault = true,
        rulesDisabled = Array.Empty<string>(),
        thresholdsAltered = Array.Empty<object>(),
    };

    [Fact]
    public async Task STAR_A_Submission_With_No_Configuration_Declaration_Is_Refused()
    {
        // ★★ The hole itself. Without this, every other check passes on a run configured however the vendor
        // liked, and the published number describes a tool no customer is given.
        var body = await SubmitAsync("undeclared", configuration: null);

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("configuration", Problems(body), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Run_On_The_Shipping_Default_Is_Accepted()
    {
        var body = await SubmitAsync("honest-default", Default());

        Assert.True(body.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task STAR_Claiming_The_Product_Default_While_Disabling_Rules_Is_A_CONTRADICTION()
    {
        // ★★ The most likely way this declaration gets filled in dishonestly is not a lie but a
        // contradiction: tick "this is what customers get" and list the rules you turned off. Refusing it
        // costs an honest vendor one word of explanation and costs a dishonest one the whole manoeuvre.
        var body = await SubmitAsync("contradictory", new
        {
            rulesetId = "watchdog-default-2026.08",
            isProductDefault = true,
            rulesDisabled = new[] { "D4-clone-mass", "D15-hotspot" },
            thresholdsAltered = Array.Empty<object>(),
        });

        Assert.False(body.GetProperty("accepted").GetBoolean());
        var problems = Problems(body);
        Assert.Contains("cannot be the product default", problems, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("D4-clone-mass", problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_Diverging_From_The_Default_Without_Explaining_How_Is_Refused()
    {
        // ★★ A divergence is allowed — a vendor may have good reasons — but it publishes with its
        // explanation. "Not the default" and silence is the combination that tells a reader nothing while
        // looking like a disclosure.
        var body = await SubmitAsync("silent-divergence", new
        {
            rulesetId = "custom-profile",
            isProductDefault = false,
            rulesDisabled = new[] { "D15-hotspot" },
            thresholdsAltered = Array.Empty<object>(),
        });

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("how it differs", Problems(body), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Declared_Divergence_With_Its_Explanation_Is_Accepted()
    {
        var body = await SubmitAsync("honest-divergence", new
        {
            rulesetId = "custom-profile",
            isProductDefault = false,
            divergenceExplanation =
                "the two Electron lenses are disabled because the corpus contains no Electron application, "
                + "and leaving them on produced only their own absence",
            rulesDisabled = new[] { "D29-electron-preload" },
            thresholdsAltered = new object[]
            {
                new { ruleId = "D4-clone-mass", shipped = "60 tokens", used = "80 tokens" },
            },
        });

        Assert.True(body.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task STAR_An_Altered_Threshold_Must_State_What_It_Was_And_What_It_BECAME()
    {
        // ★ "We changed a threshold" is not a checkable claim. Shipped-versus-used is, and it is the pair a
        // competitor or a buyer would ask for.
        var body = await SubmitAsync("vague-threshold", new
        {
            rulesetId = "custom-profile",
            isProductDefault = false,
            divergenceExplanation = "tuned for the corpus",
            rulesDisabled = Array.Empty<string>(),
            thresholdsAltered = new object[] { new { ruleId = "D4-clone-mass" } },
        });

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("shipped", Problems(body), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Ruleset_Id_Is_Required()
    {
        // ★ Without it the declaration names nothing a third party could ask to see.
        var body = await SubmitAsync("anonymous-ruleset", new
        {
            isProductDefault = true,
            rulesDisabled = Array.Empty<string>(),
            thresholdsAltered = Array.Empty<object>(),
        });

        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("ruleset", Problems(body), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Declaration_PUBLISHES_In_The_Record()
    {
        // ★★ A declaration nobody can read is not a disclosure. It publishes beside the number, with the
        // divergence spelled out, so the claim is one a competitor can point at.
        await SubmitAsync("published-config", new
        {
            rulesetId = "custom-profile",
            isProductDefault = false,
            divergenceExplanation = "D15 disabled: no repository in the corpus has enough history",
            rulesDisabled = new[] { "D15-hotspot" },
            thresholdsAltered = new object[]
            {
                new { ruleId = "D4-clone-mass", shipped = "60 tokens", used = "80 tokens" },
            },
        });

        using var client = fx.Client();
        var record = JsonDocument.Parse(
            await client.GetStringAsync($"/api/noise/record/{Period}", Ct)).RootElement;

        var mine = record.GetProperty("submissions").EnumerateArray()
            .First(s => s.GetProperty("tool").GetString() == "published-config");
        var config = mine.GetProperty("configuration");

        Assert.Equal("custom-profile", config.GetProperty("rulesetId").GetString());
        Assert.False(config.GetProperty("isProductDefault").GetBoolean());
        Assert.Contains("no repository in the corpus",
            config.GetProperty("divergenceExplanation").GetString()!, StringComparison.Ordinal);
        Assert.Contains("D15-hotspot",
            config.GetProperty("rulesDisabled").EnumerateArray().Select(r => r.GetString()));
        var threshold = config.GetProperty("thresholdsAltered").EnumerateArray().First();
        Assert.Equal("60 tokens", threshold.GetProperty("shipped").GetString());
        Assert.Equal("80 tokens", threshold.GetProperty("used").GetString());
    }

    [Fact]
    public async Task The_Method_Says_The_Declaration_Is_Required()
    {
        // ★ /method is what a participant reads before building a submitter. A requirement it does not
        // mention is one nobody sends until their submission is refused.
        using var client = fx.Client();
        var method = JsonDocument.Parse(
            await client.GetStringAsync("/api/noise/method", Ct)).RootElement;

        Assert.True(method.GetProperty("requiresConfigurationDeclaration").GetBoolean());
    }
}
