using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Boots the real Cai.Web app in the UNCONFIGURED registry state — exactly what production runs between "the code is
/// deployed" and "the founder provisions credentials": <c>Registry:KeysPath</c> points at a file that does not exist
/// (the systemd drop-in sets the path before the key set is provisioned) and there are NO
/// <c>Registry:Principals</c>. Only the store path is redirected to a scratch dir (storage hygiene for the test run —
/// production redirects it too, via the drop-in; it is not part of the surface under test).
/// </summary>
public sealed class RegistryUnconfiguredFixture : IDisposable
{
    public const string PartnerKey = "test-partner-unconfigured";

    private readonly string _root;

    public RegistryUnconfiguredFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "cai-registry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Registry:DbPath"] = Path.Combine(_root, "registry.db"),
                ["Registry:KeysPath"] = Path.Combine(_root, "trusted-keys.json"), // deliberately ABSENT (prod pre-provisioning state)
                // The anonymous open-API rate budget (1/s · 3/min · 15/day) must never make this suite flaky —
                // anonymous test requests ride the partner-key exemption. Registry-unrelated configuration.
                ["RateLimit:PartnerKey"] = PartnerKey,
            }));
        });
    }

    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>An anonymous or bearer-token client. No principals are configured, so EVERY token is unknown.</summary>
    public HttpClient Client(string? token = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CAI-Partner", PartnerKey);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    public void Dispose()
    {
        Factory.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // scratch dir cleanup is best-effort
        }
    }
}

/// <summary>
/// The registry's safe-by-default contract when it runs UNCONFIGURED (no principals, no trusted-key file). This is the
/// state every fresh deploy is in until credentials are provisioned, and it is exactly the state production was
/// observed in when every <c>/api/registry/*</c> request 500ed with "No authenticationScheme was specified" — the
/// bearer scheme must be registered unconditionally so the deny path CHALLENGES (401) instead of throwing, and the two
/// public endpoints (<c>/health</c>, <c>/keys</c>) must answer without any credential. Never 500.
/// </summary>
public sealed class RegistryUnconfiguredApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unconfigured_registry_health_is_public_and_reports_degraded_not_500()
    {
        using var client = fx.Client();
        var response = await client.GetAsync("/api/registry/health", Ct);

        // The health contract: the endpoint always ANSWERS (200 healthy/degraded, 503 unhealthy — never 500, never a
        // challenge). Unconfigured = store reachable but no ACTIVE trusted key, i.e. degraded: alive, rejects publishes.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Unconfigured_unauthenticated_publish_is_401_with_challenge()
    {
        using var client = fx.Client();
        var response = await client.PostAsync("/api/registry/deliveries",
            new StringContent("{}", Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Unconfigured_unknown_token_publish_is_401()
    {
        using var client = fx.Client("tok-that-matches-no-principal");
        var response = await client.PostAsync("/api/registry/deliveries",
            new StringContent("{}", Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unconfigured_reads_are_401_not_500()
    {
        using var client = fx.Client();
        foreach (var path in new[] { "/api/registry/deliveries", "/api/registry/deliveries/cd_x", "/api/registry/grants" })
        {
            var response = await client.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Unconfigured_keys_endpoint_is_public_and_serves_an_empty_set()
    {
        using var client = fx.Client();
        var response = await client.GetAsync("/api/registry/keys", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(0, doc.RootElement.GetProperty("keys").GetArrayLength());
    }

    [Fact]
    public async Task Unconfigured_global_health_still_answers()
    {
        // The deploy gate polls /health for a 200 before swapping slots — an unconfigured registry must never turn
        // that into a 5xx (degraded maps to 200), or a fresh box could never take its FIRST deploy.
        using var client = fx.Client();
        var response = await client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Zero_config_boot_never_500s_on_the_registry_surface()
    {
        // The literal reproduction of the production incident: boot with NO Registry section at all (the compose-root
        // defaults) and hit the surface anonymously. Health answers, protected endpoints challenge with 401 — the
        // exact requests that returned 500 ("No authenticationScheme was specified") on the deployed build.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:PartnerKey"] = RegistryUnconfiguredFixture.PartnerKey,
            })));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CAI-Partner", RegistryUnconfiguredFixture.PartnerKey);

        var health = await client.GetAsync("/api/registry/health", Ct);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var keys = await client.GetAsync("/api/registry/keys", Ct);
        Assert.Equal(HttpStatusCode.OK, keys.StatusCode);

        var publish = await client.PostAsync("/api/registry/deliveries",
            new StringContent("{}", Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, publish.StatusCode);
    }
}
