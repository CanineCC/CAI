using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cai.Delivery;
using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The anonymous signature-verification endpoint. Signature checking previously existed only in the CLI and on
/// registry ingest, which put it exactly where the person who needs it is not: the party HANDED a signed survey is
/// the one the signature is for, and the least likely to install tooling to use it. These tests pin the contract that
/// a recipient can paste a package and learn two independent things — is it authentically ours and unedited, and does
/// the number it claims actually reproduce from the evidence it carries.
/// </summary>
public sealed class VerifyDeliveryApiTests(RegistryApiFixture fx) : IClassFixture<RegistryApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Endpoint = "/api/verify-delivery";

    private static EvidenceBundle Evidence(string commit = "3f9a1c2") => new()
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

    private DeliveryPackage Mint(
        DeliveryKeyPair? key = null,
        string repository = "github.com/acme/widgets",
        string commit = "3f9a1c2",
        Func<DeliveryPayload, DeliveryPayload>? mutateBeforeSigning = null)
    {
        var payload = DeliveryBuilder.Build(Evidence(commit), new DeliveryBuildRequest
        {
            DeliveryId = "dlv_verify_" + Guid.NewGuid().ToString("N")[..8],
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

    private async Task<JsonElement> PostAsync(string body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var client = fx.Client(token: null);   // anonymous — that is the whole point of this endpoint
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(Endpoint, content, Ct);

        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private Task<JsonElement> PostAsync(DeliveryPackage package, HttpStatusCode expected = HttpStatusCode.OK) =>
        PostAsync(package.ToJson(), expected);

    [Fact]
    public async Task Anonymous_caller_can_verify_a_genuine_package()
    {
        var json = await PostAsync(Mint());

        Assert.True(json.GetProperty("trustworthy").GetBoolean());
        Assert.True(json.GetProperty("signatureValid").GetBoolean());
        Assert.True(json.GetProperty("reproduced").GetBoolean());
    }

    [Fact]
    public async Task Verified_response_echoes_what_the_document_claims_about_itself()
    {
        // A valid signature only proves the artifact is ours and unedited — NOT that it describes the repository the
        // recipient thinks it does. The echo is what lets them check they were handed a survey of the right code.
        var json = await PostAsync(Mint(repository: "github.com/acme/billing", commit: "abc1234"));

        Assert.Equal("github.com/acme/billing", json.GetProperty("subject").GetProperty("repository").GetString());
        Assert.Equal("abc1234", json.GetProperty("subject").GetProperty("commit").GetString());
        Assert.Equal("rubric-2026.08.15", json.GetProperty("rubricVersion").GetString());
        Assert.Equal(fx.ActiveKey.KeyId, json.GetProperty("keyId").GetString());
        Assert.Equal("watchdog.canine.dev", json.GetProperty("producer").GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("verdict").GetProperty("band").GetString()));
    }

    [Fact]
    public async Task A_package_signed_by_an_untrusted_key_is_not_trustworthy()
    {
        var json = await PostAsync(Mint(key: fx.UnknownKey));

        Assert.False(json.GetProperty("trustworthy").GetBoolean());
        Assert.False(json.GetProperty("signatureValid").GetBoolean());
        Assert.Contains("no public key", json.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_tampered_payload_fails_verification()
    {
        // The exact attack the signature exists to stop: take a genuine survey, edit the number upwards, pass it on.
        var genuine = Mint();
        var doc = JsonNodeEdit(genuine.ToJson(), "verdict-cai", 99.9);

        var json = await PostAsync(doc);

        Assert.False(json.GetProperty("trustworthy").GetBoolean());
        Assert.False(json.GetProperty("signatureValid").GetBoolean());
    }

    [Fact]
    public async Task A_tampered_subject_fails_verification()
    {
        // Re-pointing a genuine survey at a different repository is the other half of the same attack.
        var doc = JsonNodeEdit(Mint().ToJson(), "subject-repository", "github.com/evil/other");

        var json = await PostAsync(doc);

        Assert.False(json.GetProperty("signatureValid").GetBoolean());
    }

    [Fact]
    public async Task A_signed_package_whose_number_does_not_reproduce_is_reported_as_such()
    {
        // Authenticity and honest math are independent claims. A package can be genuinely ours yet carry a headline
        // that its own evidence does not fold to — signed BEFORE the edit, so the signature still checks out.
        // The endpoint must not let a valid signature vouch for the number.
        var package = Mint(mutateBeforeSigning: p => p with { Verdict = p.Verdict with { Cai = 99.0 } });

        var json = await PostAsync(package);

        Assert.True(json.GetProperty("signatureValid").GetBoolean());
        Assert.False(json.GetProperty("reproduced").GetBoolean());
        Assert.False(json.GetProperty("trustworthy").GetBoolean());
        Assert.Equal(99.0, json.GetProperty("claimedCai").GetDouble(), 1);
        Assert.NotEqual(99.0, json.GetProperty("computedCai").GetDouble(), 1);
    }

    [Fact]
    public async Task Malformed_input_is_a_bad_request_not_a_server_error()
    {
        var json = await PostAsync("{ this is not json", HttpStatusCode.BadRequest);

        Assert.True(json.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task An_empty_body_is_a_bad_request()
    {
        await PostAsync(string.Empty, HttpStatusCode.BadRequest);
    }

    /// <summary>Edits one field in a serialized package WITHOUT re-signing — simulating a recipient-side tamper.</summary>
    private static string JsonNodeEdit(string packageJson, string what, object value)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(packageJson)!;
        var payload = node["payload"]!;
        switch (what)
        {
            case "verdict-cai":
                payload["verdict"]!["cai"] = (double)value;
                break;
            case "subject-repository":
                payload["subject"]!["repository"] = (string)value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(what), what, "unhandled edit");
        }

        return node.ToJsonString();
    }
}
