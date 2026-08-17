using System.Net;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The two ways this host presented itself to the outside world and got it wrong.
///
/// <para><b>robots.txt answered 401.</b> It matched no endpoint, and a request that matches no endpoint is evaluated
/// against the ADR-0008 default-deny fallback policy — so the first file every crawler fetches replied
/// <c>{"error":"authentication required — present a registry bearer token"}</c>. A 401 on robots.txt reads as
/// "disallow everything", applied to the host serving the open standard's API.</para>
///
/// <para><b>Every typo answered 401 too</b>, which makes an entirely public, anonymous API look like a walled one.
/// Only the page host gains a plain 404: under <c>/api</c> an anonymous probe must still fail CLOSED (see
/// <c>AuthSurfaceTests</c>), so that half is deliberately unchanged.</para>
/// </summary>
public sealed class PublicSurfaceTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Robots_txt_is_served_and_never_challenges()
    {
        using var client = fx.Client();

        var response = await client.GetAsync("/robots.txt", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("User-agent: *", body, StringComparison.Ordinal);
        // This host is the API; the crawlable copy of the standard is the page site.
        Assert.Contains("Disallow: /", body, StringComparison.Ordinal);
        Assert.Contains("codeassuranceindex.info", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/nope")]
    [InlineData("/some/retired/page")]
    public async Task An_unknown_PAGE_path_is_404_not_401(string path)
    {
        using var client = fx.Client();

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/", "https://codeassuranceindex.info/")]
    [InlineData("/spec", "https://codeassuranceindex.info/spec/")]
    [InlineData("/dimensions", "https://codeassuranceindex.info/dimensions/")]
    [InlineData("/lenses", "https://codeassuranceindex.info/dimensions/")]
    [InlineData("/verify", "https://codeassuranceindex.info/verify/")]
    [InlineData("/calculator", "https://codeassuranceindex.info/verify/")]
    [InlineData("/registry", "https://codeassuranceindex.info/registry/")]
    [InlineData("/cli", "https://codeassuranceindex.info/page-cli/")]
    [InlineData("/badge", "https://codeassuranceindex.info/badge/")]
    public async Task The_retired_marketing_pages_redirect_to_the_standards_site(string path, string target)
    {
        // This host served a complete second website whose content disagreed with the site of the same name. The
        // pages are gone; every URL ever published to them still resolves.
        using var client = fx.Client(followRedirects: false);

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(target, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task The_example_bundle_is_served_and_is_valid_input_to_the_scorer()
    {
        // A printed example can be wrong for months (this one was). A served one is fetched, POSTed back, and folds.
        using var client = fx.Client();

        var example = await client.GetAsync("/api/score/example", Ct);
        Assert.Equal(HttpStatusCode.OK, example.StatusCode);

        var body = await example.Content.ReadAsStringAsync(Ct);
        var scored = await client.PostAsync(
            "/api/score", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.OK, scored.StatusCode);
    }

    [Fact]
    public async Task An_unknown_API_path_still_fails_closed()
    {
        // The half that must NOT change: /api is default-deny, and an anonymous caller gets the bearer challenge.
        using var client = fx.Client();

        var response = await client.GetAsync("/api/nope", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }
}
