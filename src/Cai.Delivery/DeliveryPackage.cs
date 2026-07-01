using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cai.Delivery;

/// <summary>The package format version constants. Two axes never mix: this versions the WIRE SHAPE of a delivery; the
/// rubric version (carried in the payload) versions the SCORING MATH.</summary>
public static class DeliverySchema
{
    /// <summary>The current package format version — <c>MAJOR.MINOR</c>.</summary>
    public const string Current = "1.0";

    /// <summary>The MAJOR this build implements. A verifier rejects a delivery whose MAJOR differs (an incompatible
    /// shape/trust change); a higher MINOR is forward-compatible (additive, unknown fields ignored).</summary>
    public const int SupportedMajor = 1;

    /// <summary>The published JSON Schema id for the current version.</summary>
    public const string SchemaId = "https://cai.canine.dev/schemas/cai-delivery-1.0.schema.json";

    /// <summary>Parse a <c>MAJOR.MINOR</c> version, returning its major, or null when unparseable.</summary>
    public static int? MajorOf(string? schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            return null;
        }

        var dot = schemaVersion.IndexOf('.');
        var head = dot < 0 ? schemaVersion : schemaVersion[..dot];
        return int.TryParse(head, out var major) ? major : null;
    }
}

/// <summary>
/// A signed CAI-delivery package — the shareable evidence artifact. It is exactly two parts: the <see cref="Payload"/>
/// (the point-in-time content cai attests) and the <see cref="Signature"/> over that payload's canonical form. The
/// signature is what makes the artifact trustworthy in someone else's hands: it is signed by cai, not by the sharer, so
/// a buyer can trust a package they did not generate — verify the signature offline, and (independently) reproduce the
/// headline from the embedded evidence.
/// </summary>
public sealed record DeliveryPackage
{
    /// <summary>The signed content.</summary>
    [JsonPropertyName("payload")] public DeliveryPayload Payload { get; init; } = new();

    /// <summary>The Ed25519 signature over <see cref="Payload"/>'s canonical form.</summary>
    [JsonPropertyName("signature")] public DeliverySignature Signature { get; init; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>Parse a package from its JSON wire form. Throws <see cref="JsonException"/> on malformed or null input.</summary>
    public static DeliveryPackage Parse(string json) =>
        JsonSerializer.Deserialize<DeliveryPackage>(json, Options)
        ?? throw new JsonException("CAI-delivery package deserialized to null.");

    /// <summary>Serialize this package to its indented JSON wire form (human-readable — the artifact a seller downloads
    /// and shares). NOTE: this pretty-printed form is NOT what is signed; the signature is over the payload's CANONICAL
    /// form (see <see cref="CanonicalJson"/>), so reformatting the file never affects verification.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);
}

/// <summary>The detached Ed25519 signature over a delivery payload's canonical form.</summary>
public sealed record DeliverySignature
{
    /// <summary>The signature algorithm — always <c>Ed25519</c> (RFC 8032 PureEdDSA).</summary>
    [JsonPropertyName("alg")] public string Alg { get; init; } = DeliverySigning.Algorithm;

    /// <summary>The signing key id — must match <see cref="DeliveryIssuer.KeyId"/>; selects the public key to verify with.</summary>
    [JsonPropertyName("keyId")] public string KeyId { get; init; } = "";

    /// <summary>The canonicalization method the signed bytes were produced by — <c>RFC8785-json</c> (JSON Canonicalization
    /// Scheme). Named on the wire so the verify path is unambiguous and a future scheme can be introduced by version.</summary>
    [JsonPropertyName("canon")] public string Canon { get; init; } = CanonicalJson.Method;

    /// <summary>The signature value — base64url (unpadded) of the 64-byte Ed25519 signature.</summary>
    [JsonPropertyName("value")] public string Value { get; init; } = "";
}
