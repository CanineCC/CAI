using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cai.Delivery;
using Cai.Scoring;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Boots the real Cai.Web app (in-process) with a scratch registry: a temp SQLite store, a temp trusted-key file
/// (one active key, one retired key, plus the shipped sample's public key), and four configured principals —
/// the producer (Watchdog), a seller org, a buyer org and a stranger org.
/// </summary>
public sealed class RegistryApiFixture : IDisposable
{
    public const string ProducerToken = "tok-producer";
    public const string SellerToken = "tok-seller";
    public const string BuyerToken = "tok-buyer";
    public const string StrangerToken = "tok-stranger";
    public const string SellerOrg = "org_seller";
    public const string BuyerOrg = "org_buyer";
    public const string StrangerOrg = "org_stranger";
    public const string PartnerKey = "test-partner";

    private readonly string _root;

    public RegistryApiFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "cai-registry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        ActiveKey = DeliveryKeyPair.Generate("cai-ed25519-test-active");
        RetiredKey = DeliveryKeyPair.Generate("cai-ed25519-test-retired");
        UnknownKey = DeliveryKeyPair.Generate("cai-ed25519-test-unknown"); // NOT in the trusted set

        // The shipped sample's public key rides along so the canonical example package publishes cleanly.
        var sampleKeys = DeliveryPublicKeySet.Parse(File.ReadAllText(Path.Combine(RepoRoot, "examples", "cai-delivery.keys.json")));
        TrustedKeys = new DeliveryPublicKeySet
        {
            Keys =
            [
                ActiveKey.ToPublicKey(),
                RetiredKey.ToPublicKey() with { Status = "retired" },
                .. sampleKeys.Keys,
            ],
        };

        var keysPath = Path.Combine(_root, "trusted-keys.json");
        File.WriteAllText(keysPath, TrustedKeys.ToJson());

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Registry:DbPath"] = Path.Combine(_root, "registry.db"),
                ["Registry:KeysPath"] = keysPath,
                ["Registry:Principals:0:Token"] = ProducerToken,
                ["Registry:Principals:0:OrgId"] = "org_watchdog",
                ["Registry:Principals:0:Name"] = "watchdog.canine.dev",
                ["Registry:Principals:0:Roles:0"] = "producer",
                ["Registry:Principals:1:Token"] = SellerToken,
                ["Registry:Principals:1:OrgId"] = SellerOrg,
                ["Registry:Principals:1:Name"] = "Acme (seller)",
                ["Registry:Principals:2:Token"] = BuyerToken,
                ["Registry:Principals:2:OrgId"] = BuyerOrg,
                ["Registry:Principals:2:Name"] = "BuyerCo",
                ["Registry:Principals:3:Token"] = StrangerToken,
                ["Registry:Principals:3:OrgId"] = StrangerOrg,
                ["Registry:Principals:3:Name"] = "Stranger",
                // Anonymous-request tests ride the partner-key rate-limit exemption so the open-API budget
                // (1/s · 3/min · 15/day) can never make THIS suite flaky.
                ["RateLimit:PartnerKey"] = PartnerKey,
            }));
        });
    }

    public WebApplicationFactory<Program> Factory { get; }

    public DeliveryKeyPair ActiveKey { get; }

    public DeliveryKeyPair RetiredKey { get; }

    public DeliveryKeyPair UnknownKey { get; }

    public DeliveryPublicKeySet TrustedKeys { get; }

    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cai.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new DirectoryNotFoundException("could not locate repo root (Cai.slnx)");
        }
    }

    /// <summary>An HttpClient with a principal's bearer token (or anonymous when null — then the partner header keeps
    /// it out of the open-API rate budget).</summary>
    public HttpClient Client(string? token)
    {
        var client = Factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            client.DefaultRequestHeaders.Add("X-CAI-Partner", PartnerKey);
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
/// The registry contract end-to-end over real HTTP (in-process): publish is schema-gated + signature-verified +
/// reproduce-checked, deliveries are immutable, reads are owner-or-grantee only, and grants grant/revoke/expire the
/// way the spec says. These tests ARE the endpoint contract the kennel client is built against.
/// </summary>
public sealed class RegistryApiTests(RegistryApiFixture fx) : IClassFixture<RegistryApiFixture>
{
    private static int _seq;

    /// <summary>The per-test cancellation token (xUnit1051 — keeps cancellation responsive).</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewId(string prefix) => $"{prefix}_{Interlocked.Increment(ref _seq)}_{Guid.NewGuid():N}"[..30];

    private static EvidenceBundle SampleEvidence(string commit = "3f9a1c2") => new()
    {
        RubricVersion = "rubric-2026.08.15",
        Commit = commit,
        QualityBar = "production",
        AnalyzableProjects = 3,
        ProductionLoc = 1500,
        Dimensions =
        [
            new DimensionScore("D1", "code-quality", 7.5, 0.95),
            new DimensionScore("D3", "code-quality", 8.2, 0.95),
            new DimensionScore("D5", "architecture", 7.1, 0.95),
            new DimensionScore("D9", "testing", 7.0, 0.85),
            new DimensionScore("D30", "security", 7.6, 0.90),
        ],
    };

    private DeliveryPackage Mint(string deliveryId, string repository, string commit = "3f9a1c2", DeliveryKeyPair? key = null,
        Func<DeliveryPayload, DeliveryPayload>? mutateBeforeSigning = null)
    {
        var payload = DeliveryBuilder.Build(SampleEvidence(commit), new DeliveryBuildRequest
        {
            DeliveryId = deliveryId,
            IssuedAt = "2026-07-02T09:00:00Z",
            Subject = new DeliverySubject { Repository = repository, Commit = commit, Host = "github.com" },
            Producer = new DeliveryProducer { Name = "watchdog.canine.dev", Scanner = "watchdog-surveyor", ScannerVersion = "4.2.0" },
        });
        if (mutateBeforeSigning is not null)
        {
            payload = mutateBeforeSigning(payload);
        }

        using var signer = new DeliverySigner(key ?? fx.ActiveKey);
        return signer.SignPackage(payload);
    }

    private static StringContent PublishBody(string ownerOrgId, string packageJson) =>
        new($"{{\"ownerOrgId\":{JsonSerializer.Serialize(ownerOrgId)},\"package\":{packageJson}}}", Encoding.UTF8, "application/json");

    private async Task<HttpResponseMessage> PublishAsync(DeliveryPackage package, string ownerOrgId = RegistryApiFixture.SellerOrg,
        string token = RegistryApiFixture.ProducerToken, string? packageJson = null)
    {
        using var client = fx.Client(token);
        return await client.PostAsync("/api/registry/deliveries", PublishBody(ownerOrgId, packageJson ?? package.ToJson()), Ct);
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

    // ── health + keys ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Health_is_green()
    {
        using var client = fx.Client(null);
        var response = await client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Keys_endpoint_is_public_and_serves_the_trusted_set()
    {
        using var client = fx.Client(null); // no credential — /keys is the one anonymous registry endpoint
        var response = await client.GetAsync("/api/registry/keys", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var served = DeliveryPublicKeySet.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(fx.TrustedKeys.Keys.Count, served.Keys.Count);
        Assert.Contains(served.Keys, k => k.KeyId == fx.ActiveKey.KeyId && k.Status == "active");
        Assert.Contains(served.Keys, k => k.KeyId == fx.RetiredKey.KeyId && k.Status == "retired");
    }

    // ── publish: the trust gate ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_valid_package_returns_201_with_metadata_and_location()
    {
        var id = NewId("cd_valid");
        var package = Mint(id, "acme/checkout-api");
        var response = await PublishAsync(package);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/registry/deliveries/{id}", response.Headers.Location?.ToString());

        using var body = await Json(response);
        Assert.Equal(id, body.RootElement.GetProperty("deliveryId").GetString());
        Assert.Equal(RegistryApiFixture.SellerOrg, body.RootElement.GetProperty("ownerOrgId").GetString());
        Assert.Equal("acme/checkout-api", body.RootElement.GetProperty("subject").GetProperty("repository").GetString());
        Assert.Equal(package.Payload.Verdict.Cai, body.RootElement.GetProperty("verdict").GetProperty("cai").GetDouble(), 2);
        Assert.Equal(package.Payload.Verdict.Band, body.RootElement.GetProperty("verdict").GetProperty("band").GetString());
    }

    [Fact]
    public async Task Publish_tampered_package_is_rejected_422()
    {
        var package = Mint(NewId("cd_tamper"), "acme/tampered");
        var tampered = package with { Payload = package.Payload with { Verdict = package.Payload.Verdict with { Cai = 99.0 } } };
        var response = await PublishAsync(tampered);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = await Json(response);
        Assert.Contains("signature verification failed", body.RootElement.GetProperty("error").GetString());

        // and nothing was stored
        using var owner = fx.Client(RegistryApiFixture.SellerToken);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/registry/deliveries/{tampered.Payload.DeliveryId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Publish_schema_invalid_package_is_rejected_400_with_details()
    {
        var package = Mint(NewId("cd_schema"), "acme/schema-invalid");

        // strip the signature member — an UNSIGNED package is not even schema-shaped
        var node = System.Text.Json.Nodes.JsonNode.Parse(package.ToJson())!.AsObject();
        node.Remove("signature");

        var response = await PublishAsync(package, packageJson: node.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = await Json(response);
        Assert.Contains("does not validate", body.RootElement.GetProperty("error").GetString());
        Assert.True(body.RootElement.GetProperty("details").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Publish_with_unknown_signing_key_is_rejected_422()
    {
        var response = await PublishAsync(Mint(NewId("cd_unknown"), "acme/unknown-key", key: fx.UnknownKey));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = await Json(response);
        Assert.Contains("not a trusted registry key", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Publish_with_retired_key_is_rejected_422()
    {
        var response = await PublishAsync(Mint(NewId("cd_retired"), "acme/retired-key", key: fx.RetiredKey));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = await Json(response);
        Assert.Contains("retired", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Publish_with_unsupported_major_version_is_rejected_422()
    {
        var package = Mint(NewId("cd_major"), "acme/future", mutateBeforeSigning: p => p with { SchemaVersion = "2.0" });
        var response = await PublishAsync(package);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = await Json(response);
        Assert.Contains("MAJOR", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Publish_signed_but_non_reproducing_verdict_is_rejected_422()
    {
        // signed honestly over a DISHONEST verdict — authenticity passes, the reproduce check refuses distribution
        var package = Mint(NewId("cd_dishonest"), "acme/dishonest",
            mutateBeforeSigning: p => p with { Verdict = p.Verdict with { Cai = p.Verdict.Cai + 20.0 } });
        var response = await PublishAsync(package);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = await Json(response);
        Assert.Contains("does not reproduce", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Publish_without_credential_is_401()
    {
        using var client = fx.Client(null);
        var response = await client.PostAsync("/api/registry/deliveries",
            PublishBody(RegistryApiFixture.SellerOrg, Mint(NewId("cd_anon"), "acme/anon").ToJson()), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Publish_with_non_producer_credential_is_403()
    {
        var response = await PublishAsync(Mint(NewId("cd_role"), "acme/role"), token: RegistryApiFixture.SellerToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Publish_without_ownerOrgId_is_400()
    {
        using var client = fx.Client(RegistryApiFixture.ProducerToken);
        var response = await client.PostAsync("/api/registry/deliveries",
            new StringContent($"{{\"package\":{Mint(NewId("cd_noowner"), "acme/no-owner").ToJson()}}}", Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await Json(response);
        Assert.Contains("ownerOrgId", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_shipped_sample_package_publishes_cleanly()
    {
        var sample = File.ReadAllText(Path.Combine(RegistryApiFixture.RepoRoot, "examples", "cai-delivery.sample.json"));
        var response = await PublishAsync(null!, ownerOrgId: RegistryApiFixture.SellerOrg, packageJson: sample);
        // 201 on the first suite run; the sample has a FIXED delivery id, so any parallel/dup publish is the
        // idempotent 200 — both prove the canonical example passes schema + signature + reproduce.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
    }

    // ── immutability ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Republishing_the_identical_package_is_idempotent_200()
    {
        var id = NewId("cd_idem");
        var package = Mint(id, "acme/idempotent");

        Assert.Equal(HttpStatusCode.Created, (await PublishAsync(package)).StatusCode);
        var again = await PublishAsync(package);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        using var body = await Json(again);
        Assert.Equal(id, body.RootElement.GetProperty("deliveryId").GetString());
    }

    [Fact]
    public async Task Republishing_the_same_id_with_different_content_is_409()
    {
        var id = NewId("cd_conflict");
        Assert.Equal(HttpStatusCode.Created, (await PublishAsync(Mint(id, "acme/original"))).StatusCode);

        var different = await PublishAsync(Mint(id, "acme/original", commit: "0000000")); // same id, new commit
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        using var body = await Json(different);
        Assert.Contains("immutable", body.RootElement.GetProperty("error").GetString());

        // the stored artifact is untouched
        using var owner = fx.Client(RegistryApiFixture.SellerToken);
        var stored = DeliveryPackage.Parse(await owner.GetStringAsync($"/api/registry/deliveries/{id}", Ct));
        Assert.Equal("3f9a1c2", stored.Payload.Subject.Commit);
    }

    // ── fetch: owner or grantee only ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Owner_fetches_the_stored_package_verbatim()
    {
        var id = NewId("cd_fetch");
        var package = Mint(id, "acme/fetch-me");
        await PublishAsync(package);

        using var owner = fx.Client(RegistryApiFixture.SellerToken);
        var response = await owner.GetAsync($"/api/registry/deliveries/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        // byte-for-byte what the producer published — and it still verifies offline
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(package.ToJson(), text);
        Assert.True(DeliveryVerifier.Verify(DeliveryPackage.Parse(text), fx.TrustedKeys).Trustworthy);
    }

    [Fact]
    public async Task Ungranted_org_cannot_fetch_and_cannot_probe_existence()
    {
        var id = NewId("cd_private");
        await PublishAsync(Mint(id, "acme/private"));

        using var buyer = fx.Client(RegistryApiFixture.BuyerToken);
        var response = await buyer.GetAsync($"/api/registry/deliveries/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // …and an id that does not exist reads EXACTLY the same (no existence side channel)
        var missing = await buyer.GetAsync($"/api/registry/deliveries/{NewId("cd_none")}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(Ct), await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Producer_role_grants_publish_not_read()
    {
        var id = NewId("cd_prodread");
        await PublishAsync(Mint(id, "acme/producer-read"));

        using var producer = fx.Client(RegistryApiFixture.ProducerToken); // org_watchdog ≠ owner, no grant
        Assert.Equal(HttpStatusCode.NotFound, (await producer.GetAsync($"/api/registry/deliveries/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Fetch_without_credential_is_401()
    {
        using var client = fx.Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/registry/deliveries/whatever", Ct)).StatusCode);
    }

    [Fact]
    public async Task Metadata_returns_the_light_header_without_evidence()
    {
        var id = NewId("cd_meta");
        var package = Mint(id, "acme/metadata");
        await PublishAsync(package);

        using var owner = fx.Client(RegistryApiFixture.SellerToken);
        var response = await owner.GetAsync($"/api/registry/deliveries/{id}/metadata", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await Json(response);
        Assert.Equal(id, body.RootElement.GetProperty("deliveryId").GetString());
        Assert.Equal("rubric-2026.08.15", body.RootElement.GetProperty("rubricVersion").GetString());
        Assert.Equal(package.Payload.Verdict.Cai, body.RootElement.GetProperty("verdict").GetProperty("cai").GetDouble(), 2);
        Assert.False(body.RootElement.TryGetProperty("evidence", out _)); // light header — never the evidence
        Assert.False(body.RootElement.TryGetProperty("payload", out _));
    }

    // ── grants: the authority axis ───────────────────────────────────────────────────────────────────────────────

    private async Task<string> GrantAsync(object request, string token = RegistryApiFixture.SellerToken,
        HttpStatusCode expect = HttpStatusCode.Created)
    {
        using var client = fx.Client(token);
        var response = await client.PostAsJsonAsync("/api/registry/grants", request, Ct);
        Assert.Equal(expect, response.StatusCode);
        if (expect != HttpStatusCode.Created)
        {
            return "";
        }

        using var body = await Json(response);
        return body.RootElement.GetProperty("grantId").GetString()!;
    }

    [Fact]
    public async Task Grant_then_fetch_allowed_then_revoke_then_denied()
    {
        var id = NewId("cd_grant");
        await PublishAsync(Mint(id, "acme/granted"));

        using var buyer = fx.Client(RegistryApiFixture.BuyerToken);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/api/registry/deliveries/{id}", Ct)).StatusCode);

        var grantId = await GrantAsync(new
        {
            grantee = new { orgId = RegistryApiFixture.BuyerOrg },
            scope = "delivery",
            scopeRefs = new[] { id },
            purpose = "due diligence",
        });

        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync($"/api/registry/deliveries/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync($"/api/registry/deliveries/{id}/metadata", Ct)).StatusCode);

        // revoke stops FUTURE registry reads (the copy already fetched stays valid — grants govern distribution)
        using var seller = fx.Client(RegistryApiFixture.SellerToken);
        Assert.Equal(HttpStatusCode.NoContent, (await seller.DeleteAsync($"/api/registry/grants/{grantId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/api/registry/deliveries/{id}", Ct)).StatusCode);

        // revocation is idempotent
        Assert.Equal(HttpStatusCode.NoContent, (await seller.DeleteAsync($"/api/registry/grants/{grantId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Expired_grant_confers_no_access()
    {
        var id = NewId("cd_expired");
        await PublishAsync(Mint(id, "acme/expired-grant"));
        await GrantAsync(new
        {
            grantee = new { orgId = RegistryApiFixture.BuyerOrg },
            scope = "delivery",
            scopeRefs = new[] { id },
            expiresAt = "2020-01-01T00:00:00Z", // expiry is evaluated at read time
        });

        using var buyer = fx.Client(RegistryApiFixture.BuyerToken);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/api/registry/deliveries/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Repo_scope_grant_covers_current_and_future_deliveries()
    {
        var repo = $"acme/{NewId("repo")}";
        var first = NewId("cd_repo1");
        await PublishAsync(Mint(first, repo));

        await GrantAsync(new { grantee = new { orgId = RegistryApiFixture.BuyerOrg }, scope = "repo", scopeRefs = new[] { repo } });

        var second = NewId("cd_repo2"); // published AFTER the grant — still covered while the grant is active
        await PublishAsync(Mint(second, repo, commit: "aaaa111"));

        using var buyer = fx.Client(RegistryApiFixture.BuyerToken);
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync($"/api/registry/deliveries/{first}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync($"/api/registry/deliveries/{second}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Repo_scope_grant_does_not_cover_another_orgs_deliveries_of_the_same_repo_name()
    {
        var repo = $"acme/{NewId("shared")}";
        var strangersDelivery = NewId("cd_other");
        await PublishAsync(Mint(strangersDelivery, repo), ownerOrgId: RegistryApiFixture.StrangerOrg);

        // seller grants buyer that repo name — but the seller owns no such delivery, the stranger does
        await GrantAsync(new { grantee = new { orgId = RegistryApiFixture.BuyerOrg }, scope = "repo", scopeRefs = new[] { repo } });

        using var buyer = fx.Client(RegistryApiFixture.BuyerToken);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/api/registry/deliveries/{strangersDelivery}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Granting_a_delivery_you_do_not_own_is_400()
    {
        var id = NewId("cd_notmine");
        await PublishAsync(Mint(id, "acme/not-mine"), ownerOrgId: RegistryApiFixture.StrangerOrg);

        await GrantAsync(new { grantee = new { orgId = RegistryApiFixture.BuyerOrg }, scope = "delivery", scopeRefs = new[] { id } },
            token: RegistryApiFixture.SellerToken, expect: HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Granting_to_your_own_org_is_400()
    {
        var id = NewId("cd_self");
        await PublishAsync(Mint(id, "acme/self-grant"));
        await GrantAsync(new { grantee = new { orgId = RegistryApiFixture.SellerOrg }, scope = "delivery", scopeRefs = new[] { id } },
            expect: HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Grantee_must_carry_exactly_one_of_orgId_or_email()
    {
        var id = NewId("cd_both");
        await PublishAsync(Mint(id, "acme/grantee-both"));
        await GrantAsync(new
        {
            grantee = new { orgId = RegistryApiFixture.BuyerOrg, email = "buyer@example.com" },
            scope = "delivery",
            scopeRefs = new[] { id },
        }, expect: HttpStatusCode.BadRequest);
        await GrantAsync(new { grantee = new { }, scope = "delivery", scopeRefs = new[] { id } },
            expect: HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Email_grant_is_pending_and_confers_no_access()
    {
        var id = NewId("cd_email");
        await PublishAsync(Mint(id, "acme/email-invite"));

        using var seller = fx.Client(RegistryApiFixture.SellerToken);
        var response = await seller.PostAsJsonAsync("/api/registry/grants",
            new { grantee = new { email = "buyer@example.com" }, scope = "delivery", scopeRefs = new[] { id } }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await Json(response);
        Assert.Equal("pending", body.RootElement.GetProperty("status").GetString());

        using var buyer = fx.Client(RegistryApiFixture.BuyerToken);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/api/registry/deliveries/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Revoking_someone_elses_grant_is_404()
    {
        var id = NewId("cd_revother");
        await PublishAsync(Mint(id, "acme/revoke-other"));
        var grantId = await GrantAsync(new { grantee = new { orgId = RegistryApiFixture.BuyerOrg }, scope = "delivery", scopeRefs = new[] { id } });

        using var stranger = fx.Client(RegistryApiFixture.StrangerToken);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/registry/grants/{grantId}", Ct)).StatusCode);

        using var buyer = fx.Client(RegistryApiFixture.BuyerToken); // grant still in force
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync($"/api/registry/deliveries/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Grants_list_by_direction()
    {
        var id = NewId("cd_dirs");
        await PublishAsync(Mint(id, "acme/directions"));
        var grantId = await GrantAsync(new
        {
            grantee = new { orgId = RegistryApiFixture.BuyerOrg },
            scope = "delivery",
            scopeRefs = new[] { id },
            purpose = "direction test",
        });

        using var seller = fx.Client(RegistryApiFixture.SellerToken);
        using var outgoing = await Json(await seller.GetAsync("/api/registry/grants?direction=outgoing", Ct));
        var mineOut = outgoing.RootElement.GetProperty("grants").EnumerateArray().Single(g => g.GetProperty("grantId").GetString() == grantId);
        Assert.Equal(RegistryApiFixture.BuyerOrg, mineOut.GetProperty("grantee").GetProperty("orgId").GetString());
        Assert.Equal("active", mineOut.GetProperty("status").GetString());

        using var buyer = fx.Client(RegistryApiFixture.BuyerToken);
        using var incoming = await Json(await buyer.GetAsync("/api/registry/grants?direction=incoming", Ct));
        var mineIn = incoming.RootElement.GetProperty("grants").EnumerateArray().Single(g => g.GetProperty("grantId").GetString() == grantId);
        Assert.Equal(RegistryApiFixture.SellerOrg, mineIn.GetProperty("ownerOrgId").GetString());

        var bad = await seller.GetAsync("/api/registry/grants", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    // ── list ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_shows_owned_and_granted_deliveries_only()
    {
        var repo = $"acme/{NewId("list")}";
        var ownedAndGranted = NewId("cd_lg");
        var ownedOnly = NewId("cd_lo");
        await PublishAsync(Mint(ownedAndGranted, repo));
        await PublishAsync(Mint(ownedOnly, repo, commit: "bbbb222"));
        await GrantAsync(new { grantee = new { orgId = RegistryApiFixture.BuyerOrg }, scope = "delivery", scopeRefs = new[] { ownedAndGranted } });

        using var seller = fx.Client(RegistryApiFixture.SellerToken);
        using var sellerList = await Json(await seller.GetAsync($"/api/registry/deliveries?repository={Uri.EscapeDataString(repo)}", Ct));
        var sellerIds = sellerList.RootElement.GetProperty("deliveries").EnumerateArray().Select(d => d.GetProperty("deliveryId").GetString()).ToList();
        Assert.Contains(ownedAndGranted, sellerIds);
        Assert.Contains(ownedOnly, sellerIds);

        using var buyer = fx.Client(RegistryApiFixture.BuyerToken);
        using var buyerList = await Json(await buyer.GetAsync($"/api/registry/deliveries?repository={Uri.EscapeDataString(repo)}", Ct));
        var buyerIds = buyerList.RootElement.GetProperty("deliveries").EnumerateArray().Select(d => d.GetProperty("deliveryId").GetString()).ToList();
        Assert.Contains(ownedAndGranted, buyerIds);
        Assert.DoesNotContain(ownedOnly, buyerIds);

        using var stranger = fx.Client(RegistryApiFixture.StrangerToken);
        using var strangerList = await Json(await stranger.GetAsync($"/api/registry/deliveries?repository={Uri.EscapeDataString(repo)}", Ct));
        Assert.Equal(0, strangerList.RootElement.GetProperty("deliveries").GetArrayLength());
    }

    [Fact]
    public async Task List_validates_paging()
    {
        using var seller = fx.Client(RegistryApiFixture.SellerToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await seller.GetAsync("/api/registry/deliveries?limit=0", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await seller.GetAsync("/api/registry/deliveries?limit=201", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await seller.GetAsync("/api/registry/deliveries?offset=-1", Ct)).StatusCode);
    }
}
