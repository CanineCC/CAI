# CAI-delivery package — format specification

- Version: **1.0** (format MAJOR 1)
- Status: Draft (closed-loop: Watchdog producer ↔ cai registry ↔ Assay consumer)
- JSON Schema: [`schemas/cai-delivery-1.0.schema.json`](../../schemas/cai-delivery-1.0.schema.json)
  (`$id`: `https://cai.canine.dev/schemas/cai-delivery-1.0.schema.json`)
- Reference implementation: [`src/Cai.Delivery`](../../src/Cai.Delivery) · sample: [`examples/cai-delivery.sample.json`](../../examples/cai-delivery.sample.json)
- See also: [ADR-0010](../adr/0010-signed-cai-delivery-package-and-registry.md) (decision + trust model),
  [registry design](cai-registry.md) (push / pull / grants).

## 1. What it is

A **CAI-delivery package** is the shareable evidence artifact the whole platform is built on: *every scan produces one*.
It is a **signed (Ed25519), tamper-evident, point-in-time, self-contained** record of a codebase's CAI — the number a
seller hands a buyer to prove code quality, and the input a buyer's paid decision reports are built on.

The trust invariant (from `00-architecture.md`): a delivery is **signed and reproducible by cai, not editable by the
sharer**. That is what lets a buyer rely on a package they did *not* generate — they verify cai's signature offline, and,
independently, reproduce the headline from the embedded evidence. One evidence artifact → many paid reports for different
parties; the artifact itself is free.

## 2. Design invariants

1. **Signed by cai, not the sharer.** The signature is cai's. A seller can hold and forward a package but cannot alter a
   byte without breaking verification.
2. **cai signs only what cai recomputed.** On push, cai re-folds the submitted evidence through the open scorer
   ([ADR-0002](../adr/0002-deterministic-reproducible-scoring.md)) and signs *its own* verdict — never a headline handed
   to it. The signature means "cai computed this," which is precisely why it is trustworthy.
3. **Self-contained & git-independent.** Repo identity, commit, rubric version and the full evidence bundle travel *in*
   the artifact. No repo, network or registry access is needed to read or verify it.
4. **Independently reproducible.** The embedded evidence lets any holder recompute the headline with the open
   `Cai.Scoring` library and confirm it matches — authenticity (signature) and honesty (reproduce) are two separate
   checks.
5. **Point-in-time & immutable.** A delivery is a frozen, dated snapshot. A new scan mints a new delivery; deliveries are
   never edited in place.
6. **Two independent version axes.** `schemaVersion` versions the *wire shape*; `rubricVersion` versions the *scoring
   math*. Neither implies the other.

## 3. Structure

A package is exactly two members: the `payload` (signed content) and the detached `signature` over it.

```jsonc
{
  "payload": { /* the signed content — §4 */ },
  "signature": { /* Ed25519 over the payload's canonical form — §5 */ }
}
```

### 3.1 `payload`

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | string | Package format `MAJOR.MINOR`, e.g. `"1.0"`. Covered by the signature (no downgrade). |
| `deliveryId` | string | cai's stable id for this delivery (assigned at push), e.g. `cd_acme_checkout-api_3f9a1c2`. |
| `issuedAt` | string | Sign time, RFC 3339 UTC. |
| `issuer` | object | `{ name, keyId }` — who signed (cai) and which key. `keyId` selects the verification key. |
| `producer` | object | `{ name, scanner?, scannerVersion? }` — who **measured** the code. Stamped by cai from the authenticated producer, so it cannot be forged by a sharer. |
| `subject` | object | `{ repository, commit?, host? }` — repo identity (git-independent provenance). |
| `rubricVersion` | string | The frozen rubric the verdict was computed under (e.g. `rubric-2026.08.15`). |
| `qualityBar` | string \| null | The band cutline profile (absent ⇒ production baseline). |
| `measurement` | object | `{ measuredLoc?, productionLoc, analyzableProjects, scannedAt? }` — scale provenance (never re-folded). |
| `verdict` | object | The CAI verdict cai recomputed — §4.1. |
| `evidence` | object | The embedded evidence bundle — the open record the verdict folds from. §4.2. |

#### 4.1 `verdict`

Mirrors the `/api/score` response so a consumer reads the same shape everywhere: `cai` (0–100 headline), `band`
(Exemplary / Strong / Adequate / Weak / Critical), `aggregate`, `categoryMean`, `coherenceNote`, and the per-`lenses`
and per-`categories` roll-ups. Every number is rounded to a fixed precision at build time (score 2 dp, weight 4 dp,
contribution 2 dp) so the canonical form is stable across machines. Per-lens fields include `criticalGated` and
`criticalContributors` so a reader can name *why* a band was capped ("gated by C1"), never an anonymous flag.

#### 4.2 `evidence`

The `Cai.Scoring` evidence bundle verbatim: `rubricVersion`, `commit`, `qualityBar`, `analyzableProjects`,
`productionLoc`, optional `headlineScore`, and the measured `dimensions` / `metaDimensions` (or a thin `lenses`
fallback). Its presence is what makes the artifact self-verifying — see [ADR-0002](../adr/0002-deterministic-reproducible-scoring.md).

The bundle may also carry two **descriptive, non-scored** metrics. They ride *inside* the signed evidence so a consumer
can echo them, but the scorer reads **neither** — they can **never** fold into the CAI or move the headline, and a package
that omits them is unaffected (a consumer that lacks them shows **"Not assessed"**):

| Field | Type | Notes |
|---|---|---|
| `rebuildCost` | string \| object \| null | The producer's rebuild-cost estimate, echoed verbatim. Either a plain string (`"€118k–€204k"`) **or** a `{ low, high, currency }` object. Descriptive only — never scored. |
| `busFactor` | string \| null | The producer's worded key-person-risk summary (`"2 of 11 devs"`), echoed verbatim. Descriptive only — never a scored dimension. |

### 3.2 `signature`

| Field | Type | Notes |
|---|---|---|
| `alg` | string | `"Ed25519"` (RFC 8032 PureEdDSA). |
| `keyId` | string | Must equal `payload.issuer.keyId`. Selects the public key. |
| `canon` | string | `"RFC8785-json"` — the canonicalization the signed bytes were produced by. |
| `value` | string | base64url (unpadded) of the 64-byte Ed25519 signature. |

## 5. Signing & canonicalization

Ed25519 signs a **byte string**, so signer and verifier must derive *identical* bytes from a payload. Those bytes are the
payload's **canonical form**:

1. Serialize the payload to JSON.
2. Re-emit it **key-sorted** (ascending, by key code units), **compact** (no insignificant whitespace), as **UTF-8**,
   preserving number tokens (values are pre-rounded, so no exponent/precision ambiguity remains).
3. `signature.value = base64url( Ed25519_sign( privateKey, canonicalBytes ) )`.

This targets **RFC 8785 (JSON Canonicalization Scheme)**. Because the closed loop (producer, registry, consumer) all run
the one reference canonicalizer (`Cai.Delivery.CanonicalJson`), they agree by construction; full independent RFC 8785
string/number conformance for a spec-only 3rd-party verifier is **deferred** with the 3rd-party conformance regime.

The signature is over the *canonical* form, so the human-readable, pretty-printed file a seller downloads can be
reformatted freely without affecting verification. (Rationale for canonicalization over a base64 "detached-JWS payload"
blob: the artifact stays human-readable — a seller can open it, a buyer can diff it.)

## 6. Verification (consumer, offline)

Given a package and cai's public key set:

1. **Version** — parse `payload.schemaVersion`; reject if its MAJOR is unimplemented (a newer MINOR is fine).
2. **Algorithm/canon** — require `signature.alg = Ed25519` and `signature.canon = RFC8785-json`.
3. **Key binding** — require `signature.keyId == payload.issuer.keyId`; resolve that `keyId` in the key set.
4. **Authenticity** — canonicalize the payload; `Ed25519_verify(publicKey, canonicalBytes, signature.value)`. Fail ⇒
   tampered or wrong key.
5. **Reproducibility** (independent) — fold `payload.evidence` with `Cai.Scoring` and confirm the headline matches
   `payload.verdict.cai` within tolerance (±0.5).

A package is **trustworthy** only when step 4 passes **and** step 5 does not contradict it. The reference CLI does all
five: `cai verify-delivery <package.json> --keys <keys.json>`.

### What the signature does and does not attest

It attests: *"cai.canine.dev folded this evidence under this rubric to this CAI, for this subject, as submitted by this
producer, at this time."* It does **not** attest that the evidence is a truthful measurement of the real repository —
that is the producer's claim. Closed-loop, the producer is the trusted Watchdog surveyor; proving a *3rd-party*
producer's evidence honestly reflects its code is the deferred conformance regime.

## 7. Keys & rotation

cai publishes its public keys as a key set (`{ keys: [ { keyId, alg, publicKey, status } ] }`), served at cai's key
endpoint and pinnable for fully offline verification. `publicKey` is base64url of the raw 32-byte Ed25519 key. A key is
**retired** (not deleted) on rotation: it stays published so deliveries signed under it still verify — the same
frozen-forever discipline as rubric versions ([ADR-0004](../adr/0004-versioned-frozen-rubrics.md)). `keyId` convention:
`cai-ed25519-<year>-<month>`.

## 8. Versioning & compatibility

- **MINOR bump** — additive, backward-compatible (new optional fields). Verifiers ignore unknown fields; old packages
  keep verifying.
- **MAJOR bump** — a breaking shape or trust change (removed/renamed/retyped required field, or a new
  canonicalization/signature scheme). Verifiers **must reject** a MAJOR they do not implement.
- `schemaVersion` is inside the signed payload, so the version cannot be stripped or downgraded on the wire.
- A new `canon`/`alg` value is introduced by version, and the wire field names it, so the verify path is never ambiguous.

## 9. Security considerations

- **Private key custody.** Signing keys are issued by cai and never ship publicly; in closed-loop v1 the key pair is
  operated inside the ONE trusted producer's push path, while the registry custodies only the public key set and
  **verifies every package on ingest** (see [ADR-0010's implementation addendum](../adr/0010-signed-cai-delivery-package-and-registry.md)
  and the [registry contract](cai-registry.md)). The reference `keygen` marks the private key `SECRET — do not
  commit`; the repository publishes only public keys.
- **Revocation vs. artifacts.** A delivery is a signed point-in-time artifact: revoking a *grant* stops future registry
  reads and discovery, but a copy a buyer already holds stays cryptographically valid by design (they got a free copy).
  Grants govern *distribution*, not an artifact's authenticity — see [registry design §5](cai-registry.md#5-access-model-adr-0018-visibility-vs-authority).
- **Key compromise.** Handled by rotation + retirement; a compromised key is marked retired and re-issuance is out of
  scope for closed-loop v1.

## 10. Example

[`examples/cai-delivery.sample.json`](../../examples/cai-delivery.sample.json) is a real, signature-valid package
(verifiable against [`examples/cai-delivery.keys.json`](../../examples/cai-delivery.keys.json)):

```
$ cai verify-delivery examples/cai-delivery.sample.json --keys examples/cai-delivery.keys.json
✓ signature verified — signed by cai.canine.dev (key cai-ed25519-2026-07), Ed25519
  subject   acme/checkout-api @ 3f9a1c2
  verdict   CAI 70.3 (Strong)  ·  rubric rubric-2026.08.15  ·  issued 2026-07-01T10:32:04Z
✓ headline reproduces from embedded evidence (70.3 = claimed 70.3)
```

To regenerate the sample (writes a fresh key pair to a scratch path; commit only the public key set + package):

```
cai keygen cai-ed25519-2026-07 --out /tmp/signing-key.json > examples/cai-delivery.keys.json
cai sign examples/evidence.sample.json --key /tmp/signing-key.json \
    --repo acme/checkout-api --commit 3f9a1c2 --host github.com \
    --producer watchdog.canine.dev --scanner watchdog-surveyor --scanner-version 4.2.0 \
    --id cd_acme_checkout-api_3f9a1c2 --issued-at 2026-07-01T10:32:04Z \
    --out examples/cai-delivery.sample.json
```
