using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The <c>/api/auth/*</c> family (<c>/session</c>, <c>/signin</c>) — probed by the prod smoke and observed to 500 on
/// the deployed build. There is NO interactive sign-in surface on this host: these paths match no endpoint, so they
/// fall to the ADR-0008 default-deny fallback policy, whose challenge runs through the registry bearer scheme. Same
/// family as the registry-unconfigured 500s: with the scheme registered conditionally, the challenge THREW
/// (<c>No authenticationScheme was specified</c>) instead of answering. The contract pinned here: an unconfigured
/// surface fails CLOSED and CLEAN — 401 with a JSON body and a <c>WWW-Authenticate: Bearer</c> challenge, never 500 —
/// on the zero-config boot every fresh deploy passes through, and configured mode stays intact.
/// </summary>
public sealed class AuthSurfaceUnconfiguredTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/auth/session")]
    [InlineData("/api/auth/signin")]
    public async Task Unconfigured_auth_paths_fail_closed_with_401_json_never_500(string path)
    {
        using var client = fx.Client();
        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Unconfigured_signin_post_fails_closed_with_401_never_500()
    {
        // The smoke also POSTs credentials at /signin — a body must not change the answer (and, with no principals
        // configured, NOTHING can sign in: fail closed).
        using var client = fx.Client();
        var response = await client.PostAsync("/api/auth/signin",
            new StringContent("user=a&pass=b", Encoding.UTF8, "application/x-www-form-urlencoded"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unconfigured_auth_path_with_unknown_bearer_is_401()
    {
        // No principals are configured, so EVERY token is unknown — still a clean deny, never a 500.
        using var client = fx.Client("tok-that-matches-no-principal");
        var response = await client.GetAsync("/api/auth/session", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>Configured-mode behavior of the same paths stays intact: anonymous is still denied cleanly, and an
/// authenticated principal gets an honest 404 (there IS no auth endpoint here) — never a 500 on either side.</summary>
public sealed class AuthSurfaceConfiguredTests(RegistryApiFixture fx) : IClassFixture<RegistryApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Configured_anonymous_auth_probe_is_still_401()
    {
        using var client = fx.Client(token: null);
        var response = await client.GetAsync("/api/auth/session", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Configured_authenticated_auth_probe_is_404_not_500()
    {
        using var client = fx.Client(RegistryApiFixture.ProducerToken);
        var response = await client.GetAsync("/api/auth/session", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
