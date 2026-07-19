using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Cross-origin access for the marketing islands.
/// <para>cai.canine.dev is served by the imprint CMS, so the standard's interactive proof tools (score a bundle,
/// verify a signed delivery) run as cross-origin web components calling this API from the reader's browser. Without
/// an explicit allow they cannot call it at all — which would mean shipping proof tools that only work on a
/// hostname nobody links to.</para>
/// <para>The allow is deliberately narrow: named first-party origins, no credentials, GET/POST only.</para>
/// </summary>
public sealed class PublicCorsTests(RegistryApiFixture fx) : IClassFixture<RegistryApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> PreflightAsync(string origin, string path = "/api/verify-delivery")
    {
        using var client = fx.Client(token: null);
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return await client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task A_marketing_origin_may_call_the_api_from_a_browser()
    {
        using var response = await PreflightAsync("https://cai.canine.dev");

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed).ShouldBeTrueBecause(
            "the standard's own marketing site must be able to run the verifier");
        Assert.Contains("https://cai.canine.dev", allowed!);
    }

    [Fact]
    public async Task Every_first_party_marketing_host_is_allowed()
    {
        foreach (var origin in new[]
                 {
                     "https://cai.canine.dev", "https://imprint.canine.dev",
                     "https://watchdog.canine.dev", "https://assay.canine.dev",
                 })
        {
            using var response = await PreflightAsync(origin);
            Assert.True(
                response.Headers.Contains("Access-Control-Allow-Origin"),
                $"{origin} should be allowed to call the API");
        }
    }

    [Fact]
    public async Task An_unrelated_origin_is_not_allowed()
    {
        // The allow is an allowlist, not a wildcard: a third-party page must not be able to drive this API from a
        // visitor's browser.
        using var response = await PreflightAsync("https://evil.example.com");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Credentials_are_never_allowed()
    {
        // Every endpoint reached this way is anonymous and read-only in effect, so there is nothing to send —
        // and allowing credentials cross-origin is how a read API becomes an ambient-authority hole.
        using var response = await PreflightAsync("https://cai.canine.dev");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task A_real_cross_origin_post_carries_the_allow_header()
    {
        // The preflight is only half of it — the actual response must carry the header too, or the browser
        // discards the body it just fetched.
        using var client = fx.Client(token: null);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/verify-delivery")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        request.Headers.Add("Origin", "https://cai.canine.dev");

        using var response = await client.SendAsync(request, Ct);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}

file static class Assertions
{
    public static void ShouldBeTrueBecause(this bool actual, string because) => Assert.True(actual, because);
}
