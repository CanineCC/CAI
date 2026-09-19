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
// The sample now signs under `cai-ed25519-sample`, which production does not and must not trust. Verifying the
// example needs only the public half, which ships in examples/cai-delivery.keys.json; regenerating it means
// running this tool, which mints a fresh keypair every time. So the private seed buys a reader nothing, and a
// committed private key — fixture or not — is indistinguishable from a leak to everyone who scans the tree. It
// is written outside the repository instead, the same way docs/spec tells a signer to keep a real one.

using Cai.Delivery;
using Cai.Scoring;
using NSec.Cryptography;

var examples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples"));
if (!Directory.Exists(examples))
{
    examples = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "examples"));
}

const string KeyId = "cai-ed25519-sample";

// ★ Every file below is written through the library's OWN ToJson(), never a hand-rolled serializer. Those options
// drop null-valued properties; a plain `WriteIndented` serializer emits them, and `"surveyFit": null` is rejected
// by the versioned package schema (which types the field as an object and reads ABSENT as "no clarity figure").
// A tool that writes the published example must write it exactly the way the library writes one.

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

File.WriteAllText(samplePath, resigned.ToJson() + "\n");

// 3. The key set a reader verifies the sample against, offline. Public half only.
File.WriteAllText(Path.Combine(examples, "cai-delivery.keys.json"),
    new DeliveryPublicKeySet
    {
        Keys = [new DeliveryPublicKey { KeyId = KeyId, Alg = pair.Alg, PublicKey = pair.PublicKey, Status = "active" }],
    }.ToJson() + "\n");

// 4. The private seed, kept out of the tree so it cannot be committed.
var keyPath = Path.Combine(Path.GetTempPath(), "cai-delivery.sample-key.json");
File.WriteAllText(keyPath, pair.ToJson() + "\n");

// 5. Prove it verifies against its own key set AND re-folds to the headline it claims, before claiming anything.
var published = DeliveryPackage.Parse(File.ReadAllText(samplePath));
var rubric = ResolvedRubric.FromStore(
    new RubricCatalogStore(Path.Combine(Path.GetDirectoryName(examples)!, "rubrics")),
    published.Payload.RubricVersion)
    ?? throw new InvalidOperationException(
        $"rubric '{published.Payload.RubricVersion}' is not in the published archive — cannot re-fold the sample");

var check = DeliveryVerifier.Verify(
    published,
    new DeliveryPublicKeySet { Keys = [new DeliveryPublicKey { KeyId = KeyId, Alg = pair.Alg, PublicKey = pair.PublicKey, Status = "active" }] },
    rubric);

Console.WriteLine($"keyId                  : {KeyId}");
Console.WriteLine($"publicKey              : {pair.PublicKey}");
Console.WriteLine($"privateKey             : {keyPath} (outside the repository — do not commit it)");
Console.WriteLine($"signatureValid         : {check.SignatureValid}");
Console.WriteLine($"reproduced             : {check.Reproduced?.ToString() ?? "null (no embedded evidence)"}");
Console.WriteLine($"authenticAndReproducing: {check.AuthenticAndReproducing}");
Console.WriteLine($"reason                 : {check.Reason ?? "(none)"}");
