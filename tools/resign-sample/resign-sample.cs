#:project ../../src/Cai.Delivery/Cai.Delivery.csproj
#:property JsonSerializerIsReflectionEnabled=true
#:property PublishTrimmed=false

// Re-signs examples/cai-delivery.sample.json under a key id of its own.
//
// Why this exists. The published sample used to be signed under `cai-ed25519-2026-07` — the PRODUCTION key id —
// while examples/cai-delivery.keys.json bound that same id to a different public key. Anyone who took the sample
// and posted it to the live /api/verify-delivery got "signature does not verify (tampered payload or wrong key)",
// which is a poor advertisement for a standard whose whole pitch is that strangers can check our work. Worse, the
// bundled key file was a trap for offline verification: it bound a production identifier to a non-production key.
//
// The sample now signs under `cai-ed25519-sample`, which production does not and must not trust. Its private seed
// is published beside it deliberately: the key is worthless for minting anything the registry accepts, and
// publishing it means anyone can regenerate and re-verify the example rather than taking our word for it.

using System.Text.Json;
using Cai.Delivery;
using NSec.Cryptography;

var examples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples"));
if (!Directory.Exists(examples))
{
    examples = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "examples"));
}

const string KeyId = "cai-ed25519-sample";
var indented = new JsonSerializerOptions { WriteIndented = true };

// 1. A fresh keypair for the sample, generated here rather than reused from anywhere.
using var key = Key.Create(SignatureAlgorithm.Ed25519,
    new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

var pair = new DeliveryKeyPair
{
    KeyId = KeyId,
    PublicKey = Base64Url.Encode(key.Export(KeyBlobFormat.RawPublicKey)),
    PrivateKey = Base64Url.Encode(key.Export(KeyBlobFormat.RawPrivateKey)),
};

// 2. Re-sign the existing sample payload verbatim — only the signature and its key id change.
var samplePath = Path.Combine(examples, "cai-delivery.sample.json");
var existing = DeliveryPackage.Parse(File.ReadAllText(samplePath));

using var signer = new DeliverySigner(pair);
var resigned = signer.SignPackage(existing.Payload);

File.WriteAllText(samplePath, JsonSerializer.Serialize(resigned, indented) + "\n");

// 3. The key set a reader verifies the sample against, offline. Public half only.
File.WriteAllText(Path.Combine(examples, "cai-delivery.keys.json"),
    JsonSerializer.Serialize(new DeliveryPublicKeySet
    {
        Keys = [new DeliveryPublicKey { KeyId = KeyId, Alg = pair.Alg, PublicKey = pair.PublicKey, Status = "active" }],
    }, indented) + "\n");

// 4. The private seed, published on purpose so the example is reproducible.
File.WriteAllText(Path.Combine(examples, "cai-delivery.sample-key.json"),
    JsonSerializer.Serialize(pair, indented) + "\n");

// 5. Prove it verifies against its own key set before claiming anything.
var check = DeliveryVerifier.Verify(
    DeliveryPackage.Parse(File.ReadAllText(samplePath)),
    new DeliveryPublicKeySet { Keys = [new DeliveryPublicKey { KeyId = KeyId, Alg = pair.Alg, PublicKey = pair.PublicKey, Status = "active" }] });

Console.WriteLine($"keyId                  : {KeyId}");
Console.WriteLine($"publicKey              : {pair.PublicKey}");
Console.WriteLine($"signatureValid         : {check.SignatureValid}");
Console.WriteLine($"reproduced             : {check.Reproduced?.ToString() ?? "null (no embedded evidence)"}");
Console.WriteLine($"authenticAndReproducing: {check.AuthenticAndReproducing}");
Console.WriteLine($"reason                 : {check.Reason ?? "(none)"}");
