# Cai.Delivery

The reference signer and verifier for the **CAI-delivery package** — the signed, tamper-evident, portable form of a CAI verdict. Part of the **CAI (Code Assurance Index)** standard, [codeassuranceindex.info](https://codeassuranceindex.info).

It sits on top of [`Cai.Scoring`](https://www.nuget.org/packages/Cai.Scoring), which stays a pure, crypto-free, deterministic fold. Signing lives here so a consumer can verify a delivery **offline, with the same code the producer used to mint it**.

## What a delivery is

A package wraps a verdict, the evidence bundle it was folded from, and its provenance, then signs the canonical bytes with Ed25519. Verifying it answers two separate questions:

- **Authentic?** — the signature is valid under a key in the published key set.
- **Reproducing?** — re-folding the embedded evidence under the rubric the package names gives back the headline it claims.

A package can be authentic and still not reproduce. The two are reported separately, and `AuthenticAndReproducing` is the conjunction.

```csharp
using Cai.Delivery;

var package = DeliveryPackage.Parse(File.ReadAllText("cai-delivery.sample.json"));
var keys    = DeliveryPublicKeySet.Parse(File.ReadAllText("cai-delivery.keys.json"));

var check = DeliveryVerifier.Verify(package, keys, rubric);
Console.WriteLine(check.AuthenticAndReproducing ? "ok" : check.Reason);
```

Minting one is the mirror image: `DeliveryBuilder.Build` turns evidence into a payload, and `DeliverySigner.SignPackage` turns that payload into a signed package. Those are one entry point each, on purpose — a detached signature is only ever correct for the payload it was taken over, so `SignPackage` stamps the issuer key id and signs *that*, leaving no way to sign one payload and ship another.

## Serializing

`Parse(string json)` and `ToJson()` live on the types that are **documents on the wire** — `DeliveryPackage`, `DeliveryPublicKeySet` and `DeliveryKeyPair` (plus `EvidenceBundle` and `RubricCatalog` in `Cai.Scoring`). Their parts — `DeliveryPayload`, `DeliverySignature`, `DeliveryIssuer`, `DeliveryBuildRequest` — deliberately carry neither.

That is not an omission. A payload on its own is not a document anyone publishes, and the bytes that matter for a payload are the **canonical** ones `CanonicalJson` produces for signing, not a convenience serialization that might differ by a space and break the signature. Serialize a part by serializing the document that contains it.

## The sample

[`examples/`](https://github.com/CanineCC/CAI/tree/main/examples) carries a signature-valid package and the public key set it verifies against, so you can check the format against a real artifact before minting your own.

Apache-2.0, matching the [repository licence](https://github.com/CanineCC/CAI/blob/main/LICENSE).
