# 0010 — The signed CAI-delivery package and the registry

- Status: Accepted (see the 2026-07-02 implementation addendum at the end)
- Date: 2026-07-01

## Context

The platform's growth engine (`~/rearch/00-architecture.md`) is that **every scan produces a signed, tamper-evident,
shareable CAI-delivery package**. A seller (Watchdog user) shares it to prove code quality; a buyer (Assay user) gets a
free copy and pays for the decision reports built on top. One evidence artifact feeds many paid reports for different
parties. cai is the binding middle: it defines the CAI standard and is the registry these packages flow through.

For a shared package to be worth anything, a buyer must be able to trust a package they did **not** generate. That
demands a specific trust model: the artifact must be *signed and reproducible by us (cai)* and *not editable by the
sharer*. We already have the reproducibility half — `Cai.Scoring` is a deterministic, open fold
([ADR-0002](0002-deterministic-reproducible-scoring.md)) — but nothing yet packages a verdict with its provenance, signs
it, or defines how producers publish and consumers fetch it with seller→buyer access control. We also want to reuse the
existing NIS2/Delivery Ed25519 signing approach rather than invent a new scheme, and to stay closed-loop first
(Watchdog↔cai↔Assay only), deferring any 3rd-party conformance regime.

## Decision

Define a versioned **CAI-delivery package** and a **registry** contract, with a reference implementation in a new
`Cai.Delivery` library that sits on top of `Cai.Scoring`.

**Package format** ([spec](../spec/cai-delivery-package.md), [schema](../../schemas/cai-delivery-1.0.schema.json)).
A package is a `payload` + a detached `signature`. The payload is self-contained and git-independent: the CAI verdict,
the embedded evidence bundle it folds from, and provenance (issuer, producer/scanner identity, repo + commit, rubric
version, timestamp, `schemaVersion`). Two version axes are kept separate — `schemaVersion` for the wire shape,
`rubricVersion` for the math.

**Trust model — Ed25519, signed by cai, recomputed by cai.**

- On push, cai **recomputes** the verdict from the submitted evidence via `Cai.Scoring` and signs *its own* result — it
  never signs a headline handed to it. The signature therefore means "cai computed this."
- Signing is **Ed25519** (RFC 8032, via NSec/libsodium — the existing NIS2/Delivery approach) over the payload's
  **canonical form** (RFC 8785-targeting JCS: key-sorted, compact, UTF-8, pre-rounded numbers). Signing the canonical
  form (not the pretty-printed file) means reformatting never invalidates a signature, while the artifact stays
  human-readable.
- Verification is **offline and two-fold**: authenticity (Ed25519 over the canonical payload, using a published public
  key resolved by `keyId`) *and*, independently, reproducibility (re-fold the embedded evidence and confirm the
  headline). Keys are published as a pinnable key set; a rotated key is **retired, not deleted**, so old deliveries keep
  verifying — the frozen-forever discipline of [ADR-0004](0004-versioned-frozen-rubrics.md).
- The signature attests cai's computation, **not** that the evidence truthfully measures the real repo — closed-loop
  that is the trusted Watchdog producer; 3rd-party producer conformance is deferred.

**Registry contract** ([design](../spec/cai-registry.md)). Under `/api/registry`: producer **push** (`POST /deliveries`
— validate, recompute, sign, store), consumer **pull** (`GET /deliveries/{id}`, `GET /keys`), and **access grants**
(`POST /grants`, `/access-requests`, `PATCH /visibility`, `GET /directory`) that back the mockup-5 seller-shares and
buyer-brings-access flows. The registry is the first **identity-gated** surface, extending
[ADR-0008](0008-api-access-control.md): the open standard API stays anonymous+rate-limited, `/api/registry/*` requires an
authenticated principal (and is protected by the existing default-deny fallback). Per kennel **ADR-0018**, the registry
separates **visibility** (opt-in directory: profile + band only) from **authority** (a grant: read the signed package);
discovery never yields evidence. Because a delivery is a signed point-in-time artifact, revoking a grant stops future
registry reads but does not (and cannot) retract a copy a buyer already holds — grants govern distribution, not
authenticity.

**Versioning/compat.** Additive changes are MINOR (unknown fields ignored; old packages keep verifying); breaking
shape/trust changes are MAJOR and a verifier must reject an unimplemented MAJOR. `schemaVersion` lives inside the signed
payload, so it cannot be stripped or downgraded; a new `alg`/`canon` is introduced by version and named on the wire.

**Reference implementation.** `src/Cai.Delivery` (`DeliveryPackage`/`DeliveryPayload`, `CanonicalJson`,
`DeliverySigner`/`DeliveryVerifier`, `DeliveryBuilder`) plus `cai keygen | sign | verify-delivery` CLI verbs, a
signature-valid [sample](../../examples/cai-delivery.sample.json) with its [public keys](../../examples/cai-delivery.keys.json),
and tests covering sign→verify, reproduce, tamper, wrong/unknown/retired key, and version rejection.

## Consequences

- A buyer can trust a shared package end-to-end with no repo access and no network: verify cai's signature, then
  reproduce the number from the embedded evidence. This is what makes the "share → free copy → paid reports" model work.
- A sharer cannot alter a delivered package without detection, and cannot mint one — only cai signs, only on the push
  path — so provenance (`producer`) cannot be forged.
- `Cai.Scoring` stays a pure, crypto-free deterministic fold; all signing lives in `Cai.Delivery`, which depends on it.
  This keeps [ADR-0002](0002-deterministic-reproducible-scoring.md)'s no-ambient-I/O constraint intact.
- cai must now custody Ed25519 signing keys (server-side, in the registry push path) and operate key rotation with
  permanent retention of retired public keys. Private keys are never committed or shipped.
- Canonicalization is normative *by shared implementation* for the closed loop; a spec-only 3rd-party verifier needs full
  RFC 8785 conformance, which — with the producer conformance regime — is deliberately deferred.
- The registry adds an authenticated surface and an access-grant model to a project that was previously all-anonymous;
  the open standard endpoints are unchanged.

## Addendum — the registry as implemented (2026-07-02)

The registry is now built (`src/Cai.Web/Registry/`, contract in [the registry spec](../spec/cai-registry.md), which is
the authoritative wire document). Two decisions refine this ADR:

1. **Producer-minted, registry-verified (v1).** The original text placed the mint (recompute-then-sign) inside the
   registry push handler. As implemented, the trusted producer mints and signs the package (via the shared
   `Cai.Delivery` builder/signer, so the verdict is still recomputed from evidence at build time — never hand-set), and
   the registry's push path is a **verification gate**: versioned JSON-schema validation, trusted-ACTIVE-key check,
   Ed25519 over the canonical payload, and an independent reproduce check (the verdict must fold from the embedded
   evidence, ±0.5) — tampered, unsigned, schema-invalid or non-reproducing packages are rejected and never stored.
   This matches the master plan's M1 wire ("Watchdog mints a signed Delivery → pushes; cai stores + serves") and is
   forced by the format itself: `deliveryId` lives inside the signed payload, so a registry-assigned id cannot exist
   without re-signing. The invariant this ADR cares about survives intact: nothing that does not reproduce from its own
   evidence is ever signed-and-DISTRIBUTED, and a sharer still cannot alter or mint anything.
2. **Key custody, amended.** "Signing keys never leave cai's push path" becomes: the signing key pair is *issued by
   cai* and operated inside the ONE trusted producer's push path; the registry custodies only public keys
   (`Registry:KeysPath`, served at `GET /api/registry/keys`; retired keys keep verifying stored artifacts but cannot
   publish new ones). `payload.issuer` stays `cai.canine.dev`. Third-party producers (deferred) will NOT get cai-issued
   keys — that is where registry-side minting or the conformance regime becomes necessary, and it can be added as a
   new endpoint without breaking the v1 contract.

Storage: SQLite behind an `IRegistryStore` seam (the spec is storage-agnostic; the delivery PK enforces immutability —
identical re-push is idempotent `200`, same-id-different-content is `409`). Auth: opaque bearer principals with a fixed
claim contract (`name`, `cai:org`, role `producer`) as the Keycloak seam. Deferred from the sketch: access requests,
directory/visibility, email-grant claiming.
