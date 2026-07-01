using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace Cai.Delivery;

/// <summary>
/// A cai signing key pair in cai's native key format — the key id plus the raw Ed25519 private (32-byte seed) and public
/// (32-byte) keys, base64url-encoded. This is a SECRET (it can mint signatures cai's identity vouches for) and must
/// never be committed or shared; only the derived <see cref="ToPublicKey"/> is published.
/// </summary>
public sealed record DeliveryKeyPair
{
    /// <summary>The key id this pair signs under.</summary>
    [JsonPropertyName("keyId")] public string KeyId { get; init; } = "";

    /// <summary>The signature algorithm — Ed25519.</summary>
    [JsonPropertyName("alg")] public string Alg { get; init; } = DeliverySigning.Algorithm;

    /// <summary>base64url of the raw 32-byte Ed25519 public key.</summary>
    [JsonPropertyName("publicKey")] public string PublicKey { get; init; } = "";

    /// <summary>base64url of the raw 32-byte Ed25519 private seed. SECRET.</summary>
    [JsonPropertyName("privateKey")] public string PrivateKey { get; init; } = "";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Generate a fresh Ed25519 key pair under <paramref name="keyId"/>.</summary>
    public static DeliveryKeyPair Generate(string keyId)
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        return new DeliveryKeyPair
        {
            KeyId = keyId,
            PublicKey = Base64Url.Encode(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            PrivateKey = Base64Url.Encode(key.Export(KeyBlobFormat.RawPrivateKey)),
        };
    }

    /// <summary>The published (public-only) form of this key pair, marked active.</summary>
    public DeliveryPublicKey ToPublicKey() => new()
    {
        KeyId = KeyId,
        Alg = Alg,
        PublicKey = PublicKey,
        Status = "active",
    };

    /// <summary>Serialize this key pair to indented JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Parse a key pair from JSON.</summary>
    public static DeliveryKeyPair Parse(string json) =>
        JsonSerializer.Deserialize<DeliveryKeyPair>(json) ?? throw new JsonException("Key pair deserialized to null.");
}

/// <summary>One published cai public key — enough to verify a delivery offline. Keys are retained forever once used
/// (status flips to <c>retired</c> on rotation but the key stays published), so a delivery signed under an old key still
/// verifies — the same frozen-forever discipline as rubric versions.</summary>
public sealed record DeliveryPublicKey
{
    /// <summary>The key id — matches a delivery's signature keyId.</summary>
    [JsonPropertyName("keyId")] public string KeyId { get; init; } = "";

    /// <summary>The signature algorithm — Ed25519.</summary>
    [JsonPropertyName("alg")] public string Alg { get; init; } = DeliverySigning.Algorithm;

    /// <summary>base64url of the raw 32-byte Ed25519 public key.</summary>
    [JsonPropertyName("publicKey")] public string PublicKey { get; init; } = "";

    /// <summary><c>active</c> (currently signing) or <c>retired</c> (rotated out, still valid for old deliveries).</summary>
    [JsonPropertyName("status")] public string Status { get; init; } = "active";
}

/// <summary>The published set of cai public keys (cai serves this at its key endpoint; a consumer can also pin it for
/// fully offline verification). Resolve a delivery's <see cref="DeliverySignature.KeyId"/> against this set.</summary>
public sealed record DeliveryPublicKeySet
{
    /// <summary>The published public keys, active and retired.</summary>
    [JsonPropertyName("keys")] public IReadOnlyList<DeliveryPublicKey> Keys { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>The key for <paramref name="keyId"/>, or null when the set does not carry it.</summary>
    public DeliveryPublicKey? Resolve(string keyId) =>
        Keys.FirstOrDefault(k => string.Equals(k.KeyId, keyId, StringComparison.Ordinal));

    /// <summary>Serialize this key set to indented JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Parse a key set from JSON.</summary>
    public static DeliveryPublicKeySet Parse(string json) =>
        JsonSerializer.Deserialize<DeliveryPublicKeySet>(json, Options)
        ?? throw new JsonException("Public key set deserialized to null.");
}
