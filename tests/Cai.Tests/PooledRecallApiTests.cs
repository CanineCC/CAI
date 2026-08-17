using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Pooled recall over HTTP, and the warning that travels with every noise rate.
/// </summary>
public sealed class PooledRecallApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Finding(string tool, string file, int line, bool valid = true) =>
        new { tool, repoId = "acme/x", filePath = file, line, valid };

    private async Task<(HttpStatusCode Status, JsonElement Body)> PooledAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/pooled", payload, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    /// <summary>
    /// ★★ The endpoint publishes precision AND pooled recall for each tool, because either alone is a
    /// number somebody can game by choosing how much to say.
    /// </summary>
    [Fact]
    public async Task STAR_precision_and_pooled_recall_are_published_together()
    {
        var (status, body) = await PooledAsync(new
        {
            // ★ A THIRD tool since #2: the figure is refused below three, because at two the leave-one-out
            // reference is one other tool's findings and "recall" is pairwise agreement.
            findings = new[]
            {
                Finding("quiet", "a.cs", 10),
                Finding("loud", "a.cs", 10),
                Finding("loud", "b.cs", 20),
                Finding("loud", "junk.cs", 30, valid: false),
                Finding("third", "a.cs", 10),
                Finding("third", "b.cs", 20),
            },
        });

        Assert.Equal(HttpStatusCode.OK, status);
        var quiet = body.GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("tool").GetString() == "quiet");

        Assert.Equal(1.0, quiet.GetProperty("precision").GetDouble(), 3);

        // ★★ Two defects in quiet's leave-one-out reference (loud and third found both between them); quiet
        // found one of them.
        Assert.Equal(0.5, quiet.GetProperty("pooledRecall").GetDouble(), 3);
        Assert.Equal(2, quiet.GetProperty("leaveOneOutReferenceSize").GetInt32());
    }

    /// <summary>
    /// ★★ One tool gets NO recall figure. Its recall against a union it alone defines is 100% by
    /// construction, and that number reads as everything while saying nothing.
    /// </summary>
    [Fact]
    public async Task STAR_a_single_tool_gets_no_pooled_recall_over_http()
    {
        var (_, body) = await PooledAsync(new
        {
            findings = new[] { Finding("solo", "a.cs", 10), Finding("solo", "b.cs", 20) },
        });

        var solo = body.GetProperty("tools").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, solo.GetProperty("pooledRecall").ValueKind);
        Assert.Equal(1, body.GetProperty("participatingTools").GetInt32());
    }

    /// <summary>★ The caveat is part of the response, not part of a document nobody reads.</summary>
    [Fact]
    public async Task STAR_the_response_carries_the_overstatement_caveat()
    {
        var (_, body) = await PooledAsync(new
        {
            findings = new[] { Finding("a", "a.cs", 10), Finding("b", "b.cs", 20) },
        });

        Assert.Contains("overstate", body.GetProperty("caveat").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>★ The matching window travels with the figures it produced.</summary>
    [Fact]
    public async Task The_line_window_is_published_with_the_result()
    {
        var (_, body) = await PooledAsync(new
        {
            lineWindow = 5,
            findings = new[] { Finding("a", "a.cs", 10), Finding("b", "a.cs", 14) },
        });

        Assert.Equal(5, body.GetProperty("lineWindow").GetInt32());
        Assert.Equal(1, body.GetProperty("unionSize").GetInt32());
    }

    /// <summary>
    /// ★★ A tool that submitted a run and reported NOTHING appears in the table with null precision and
    /// zero recall. Left out, it would look identical to a tool that never entered.
    /// </summary>
    [Fact]
    public async Task STAR_a_silent_tool_appears_with_null_precision_and_zero_recall()
    {
        var (_, body) = await PooledAsync(new
        {
            silentTools = new[] { "ghost" },
            findings = new[] { Finding("a", "a.cs", 10), Finding("b", "b.cs", 20) },
        });

        var ghost = body.GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("tool").GetString() == "ghost");
        Assert.Equal(JsonValueKind.Null, ghost.GetProperty("precision").ValueKind);
        Assert.Equal(0.0, ghost.GetProperty("pooledRecall").GetDouble(), 3);
    }

    [Fact]
    public async Task A_request_with_no_findings_is_refused()
    {
        var (status, _) = await PooledAsync(new { findings = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    /// <summary>
    /// ★★ THE WARNING TRAVELS WITH THE NOISE RATE ITSELF. A receipt that reports what a run found without
    /// saying the figure is precision-only invites exactly the reading the whole task exists to prevent:
    /// that a quiet tool is a clean one.
    /// </summary>
    [Fact]
    public async Task STAR_the_method_states_that_a_noise_rate_alone_measures_precision_only()
    {
        using var client = fx.Client();
        var body = await client.GetFromJsonAsync<JsonElement>("/api/noise/method", Ct);

        var raw = body.GetRawText();
        Assert.Contains("precision", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/noise/pooled", raw, StringComparison.Ordinal);
    }
}
