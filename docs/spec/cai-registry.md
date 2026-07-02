# CAI registry — API contract (producer push / consumer pull / access grants)

- Status: **Implemented (v1, closed-loop)** — this document IS the wire contract; the kennel client is built against it 1:1.
- Base URL: `https://api.cai.canine.dev` (the registry endpoints live in the cai app under `/api/registry`).
- Companion to: [CAI-delivery package format](cai-delivery-package.md), [ADR-0010](../adr/0010-signed-cai-delivery-package-and-registry.md)
  (incl. its v1 addendum), reference implementation `src/Cai.Web/Registry/`, contract tests `tests/Cai.Tests/RegistryApiTests.cs`.
- Backs mockup 5: `~/watchdog-mock-5-access-sharing.html` (seller "Del bevis" / buyer "Anmod om adgang") — the grant flows.

The registry is cai's role as **the binding middle** (`00-architecture.md`): the producer (Watchdog) pushes signed
CAI-delivery packages, consumers (Assay) pull the ones they own or were granted, and sellers grant buyers read access.

## 1. Roles

- **Producer** — the Watchdog surveyor. Measures code, mints the signed delivery (see §3.1), pushes it. Closed-loop:
  exactly one trusted producer, holding the cai-issued signing key.
- **Registry** — cai. **Verifies, stores, serves, and mediates access.** The trust gate on ingest is *verification*
  (schema + Ed25519 + reproduce), never mutation: the stored artifact is byte-for-byte what the producer published.
- **Consumer** — the Assay buyer (acquirer, procurement, investor). Pulls granted deliveries; verifies offline; builds
  paid reports on top.
- **Owner** — the seller org a delivery belongs to. Named by the producer at publish (`ownerOrgId`); grants access to buyers.

## 2. Where the registry sits vs. the open API

The existing cai API is an **open, anonymous, rate-limited** standard surface (`/api/rubrics`, `/api/score`,
`/api/verify`) — the standard *is* the product ([ADR-0008](../adr/0008-api-access-control.md)). The registry is the
**first identity-gated surface**: everything under `/api/registry` except `/health` and `/keys` requires an
authenticated principal — the endpoints simply do not opt out of ADR-0008's default-deny fallback policy.
Authenticated registry calls are **exempt from the open-API rate budget** (their abuse control is the credential);
anonymous calls, including `/health` and `/keys`, stay inside it.

**Safe-by-default (unconfigured):** the bearer scheme is registered unconditionally at the composition root, even when
`Registry:Principals` is empty and no key file exists — a fresh deploy therefore answers exactly like a locked one:
`/health` responds (`Degraded`, §3.4), `/keys` serves an empty set, and every other registry request is challenged
with `401`. Never a `500`; an unconfigured registry accepts nothing but tells you it is alive.

### 2.1 Authentication (v1) and the Keycloak seam

v1 authentication is an opaque **bearer token per principal**, configured server-side
(`Registry:Principals:N:{Token,OrgId,Name,Roles}`), compared in fixed time:

```
Authorization: Bearer <token>
```

Every scheme must produce the same claim contract — that contract is the whole auth seam:

| Claim | Meaning |
|---|---|
| `name` | Human-readable principal (logs/provenance), e.g. `watchdog.canine.dev`. |
| `cai:org` | The org the caller acts as — ownership and grant checks run against this. |
| role `producer` | May publish deliveries. Publishing is the ONLY role-gated operation. |

Swapping in Keycloak later = register an OIDC JWT-bearer scheme that maps token claims to these three; no endpoint or
access-model change. Errors: missing/unknown token → `401 {"error": …}` (+ `WWW-Authenticate: Bearer`); authenticated
but missing role → `403 {"error": …}`.

## 3. Endpoints

All JSON. Every error body is `{ "error": "…" }` (schema rejections add `schema` + `details`). Timestamps are RFC 3339 UTC.

### 3.0 `GET /api/registry/keys` — public

The registry's trusted signing key set (active AND retired), for offline verification; pinnable. Anonymous (like
`/health`, §3.4). Unconfigured ⇒ an empty set (and every publish is rejected).

```
200 → { "keys": [ { "keyId", "alg": "Ed25519", "publicKey", "status": "active"|"retired" } ] }
```

### 3.1 `POST /api/registry/deliveries` — producer push (role `producer`)

The producer mints the signed package (the [package format](cai-delivery-package.md), via `Cai.Delivery`:
`DeliveryBuilder` recomputes the verdict from evidence, `DeliverySigner` signs the canonical payload with the
cai-issued key) and pushes the **complete signed package**, attributing the seller org that owns it:

```jsonc
{
  "ownerOrgId": "org_acme",              // required — the seller org (the producer is trusted to attribute; closed loop)
  "package": { "payload": …, "signature": … }   // required — a complete signed cai-delivery-1.0 package
}
```

The registry's **ingest gates**, in order:

1. **Shape** — body is an object with non-empty `ownerOrgId` and object `package`, else `400`.
2. **Schema** — `package` must validate against the versioned JSON Schema
   (`cai-delivery-1.0.schema.json`, embedded in the binary; format assertions on). Unsigned or malformed packages die
   here: `400 { error, schema, details: ["<instanceLocation>: <error>", …] }`.
3. **Trusted key** — `signature.keyId` must resolve in the registry's trusted key set **with status `active`** — a
   retired key keeps stored deliveries verifiable but cannot publish new ones. Else `422`.
4. **Verification** — the same two-fold offline check a consumer runs (`DeliveryVerifier`): supported `schemaVersion`
   MAJOR, `alg`/`canon` recognized, `signature.keyId == payload.issuer.keyId`, **Ed25519 verifies over the canonical
   payload** (tampered ⇒ reject), and the **verdict reproduces from the embedded evidence** (±0.5 — a signed-but-
   dishonest number is refused distribution; `Cai.Scoring` is used to CHECK the number, never to change it). Else `422`.
5. **Immutability** — `deliveryId` is globally unique, enforced by the store's primary key. Same id + identical
   artifact (canonical-payload hash, signature and owner all equal) ⇒ idempotent `200` returning the stored record.
   Same id + anything different ⇒ `409` (a new scan mints a NEW delivery id). No idempotency key is needed — the
   delivery id + content hash *is* the dedupe.

```
201 → delivery metadata (§3.2 shape) + Location: /api/registry/deliveries/{deliveryId}   # stored
200 → delivery metadata                                                                  # identical re-push
400 → malformed JSON / missing ownerOrgId / missing package / schema-invalid
401/403 → no credential / not a producer
409 → { "error": "delivery '…' already exists with different content — deliveries are immutable; …" }
422 → { "error": "…" }   # unknown/retired key, unsupported MAJOR, bad alg/canon/keyId binding,
                         # signature does not verify, verdict does not reproduce
```

### 3.2 Consumer pull (owner OR active grantee)

```
GET /api/registry/deliveries/{id}
  200 → the stored signed package, VERBATIM (byte-for-byte as published; verify offline against /keys)
  404 → { "error": "delivery not found or not accessible" }

GET /api/registry/deliveries/{id}/metadata
  200 → { "deliveryId", "ownerOrgId",
          "subject": { "repository", "commit", "host" },       // commit/host null when absent
          "producer",                                          // producer NAME (payload.producer.name)
          "rubricVersion",
          "verdict": { "cai", "band" },
          "issuedAt", "publishedAt" }                          // sign time · registry store time
  404 → as above
```

**Unknown id and no-authority read the SAME `404`** — an ungranted caller cannot probe which delivery ids exist
(the visibility axis, §5). There is no producer read-back: the `producer` role confers publish, not read; a producer
org reads only what it owns or is granted, like anyone else.

```
GET /api/registry/deliveries?repository=&producer=&ownerOrgId=&limit=&offset=
  200 → { "deliveries": [ <metadata, §3.2 shape>, … ] }
  400 → limit outside 1..200 or offset < 0
```

Lists **everything the caller can read** — deliveries its org owns plus deliveries actively granted to it — newest
first (`issuedAt` desc, `deliveryId` asc). Filters are optional exact matches (`repository`, `producer` name,
`ownerOrgId`); `limit` defaults 50 (max 200), `offset` defaults 0.

### 3.3 Access grants (seller → buyer)

```
POST /api/registry/grants
  body: { "grantee": { "orgId": "org_buyer" } | { "email": "buyer@example.com" },   // exactly one
          "scope": "delivery" | "repo",
          "scopeRefs": [ "<deliveryId>" | "<repository>", … ],                       // non-empty
          "expiresAt": "2026-12-31T00:00:00Z",                                       // optional, RFC 3339
          "purpose": "due diligence" }                                               // optional
  201 → the grant (shape below) + Location: /api/registry/grants/{grantId}
  400 → { "error": "…" }   # both/neither grantee forms; self-grant; bad scope; empty refs; unparseable
                           # expiresAt; delivery-scope ref that does not exist or is not owned by the caller

GET /api/registry/grants?direction=outgoing|incoming        # REQUIRED query — no silent default
  200 → { "grants": [ { "grantId", "ownerOrgId", "grantee": { "orgId", "email" },  // one of them null
                        "scope", "scopeRefs", "status": "active"|"pending"|"revoked",
                        "purpose", "createdAt", "expiresAt", "revokedAt" }, … ] }
  400 → direction missing/invalid

DELETE /api/registry/grants/{grantId}                        # grantor only
  204 → revoked (idempotent — re-revoking is another 204)
  404 → unknown grant OR not the caller's grant (indistinguishable)
```

Grant semantics:

- **`scope: "delivery"`** — `scopeRefs` are delivery ids. Every ref must exist and be owned by the caller (else `400`;
  "does not exist or is not owned" is deliberately one message). The sketch's separate `"selected"` scope collapsed
  into this — one id or many, same thing.
- **`scope: "repo"`** — `scopeRefs` are repository names; covers the grantor's **current and future** deliveries of
  those repositories while the grant is active. Repo names are not existence-checked (a grant may precede the first
  delivery); a grant only ever covers deliveries **owned by its grantor**, so granting a repo name someone else scans
  confers nothing.
- **org grants** are `active` immediately; **email grants** stay `pending` and confer NO access (the invite/claim flow
  is deferred; pending grants appear in the grantor's `outgoing` list only).
- **Expiry** is evaluated at read time; `expiresAt` in the past is accepted and simply inert. **Revocation** keeps the
  row (`status: "revoked"` + `revokedAt`) — grants are an audit trail, not editable state.
- Grants to your own org are rejected (`400`) — you already own those deliveries.

### 3.4 `GET /api/registry/health` and `GET /health` — public

`GET /api/registry/health` is the registry's own liveness/readiness probe — anonymous by design (a liveness answer can
never require the credential whose absence it must be able to report), and it always ANSWERS with its health status,
never a challenge and never a `500`:

```
200 "Healthy"   → store reachable, at least one ACTIVE trusted signing key
200 "Degraded"  → store reachable but UNCONFIGURED (no active trusted key) — alive; every publish is rejected
503 "Unhealthy" → registry store unreachable
```

`Degraded` maps to `200` on purpose: a fresh box must pass the deploy's health gate BEFORE credentials/keys are
provisioned (safe-by-default, not dead-by-default).

The app-wide `GET /health` (the deploy's verify-before-swap gate) folds in the same registry check next to the rubric
catalog's: `200` only when the rubric catalog is served and the registry store is reachable.

## 4. Storage

The registry contract is storage-agnostic; deliveries are write-once, grants are append+revoke. v1 stores in
**SQLite** (WAL) behind the `IRegistryStore` seam — the simplest store with real durability and a real uniqueness
constraint (the delivery PK is what enforces immutability under concurrency), and zero new ops dependency on the
existing cai deploy. Postgres later = implement `IRegistryStore` against Npgsql and swap one registration; no endpoint
changes. **Production note:** `Registry:DbPath` must point OUTSIDE the blue-green app dirs (e.g.
`/var/lib/cai/registry.db`) so both slots share one store; `Registry:KeysPath` names the trusted key set file
(rotation = update file + restart). With no configured keys the registry rejects every publish — the safe default.

## 5. Access model (ADR-0018: visibility vs. authority)

The registry keeps two axes strictly separate (per the kennel **ADR-0018**, *visibility vs. authority*):

- **Visibility** — can you *discover that something exists*? v1 exposes none: unknown-id and no-authority reads are the
  same `404`, so the fetch path leaks no existence signal. (The opt-in directory is deferred, §6.)
- **Authority** — can you *read the actual signed delivery*? Conferred **only** by ownership or an active grant.

**Authority governs distribution, not authenticity.** A delivery is a signed, point-in-time artifact: once a buyer
holds a copy (they got it free), revoking the grant stops *future* registry reads, but the copy stays
cryptographically valid. This is intentional — the value model is "one evidence → many reports," and the artifact's
trust comes from the signature, not from continued registry permission. Sellers are told this at share time.

## 6. Out of scope (deferred)

- **Access requests** (buyer → seller `POST /access-requests` + approve/decline) and the **directory/visibility**
  surface (`PATCH /visibility`, `GET /directory`) from the original sketch — the seller-shares flow (§3.3) is enough
  for the M1 closed loop; these return with the Assay buyer UX.
- **Email-grant claim flow** (pending → active when the invitee authenticates).
- **Registry-side minting** (push raw evidence, registry recomputes + signs — the original §3.1 sketch). Can be added
  later as an additional endpoint without breaking this contract; the stored artifact is identical either way.
- 3rd-party scanner conformance / certification; federation / multi-tenant key delegation.

## 7. Addendum — v1 implementation decisions (2026-07-02)

The original draft of this document sketched producer push as *"POST raw evidence; cai recomputes, signs and stores"*.
The implemented v1 contract is **producer-minted, registry-verified**: the producer builds and signs the package (with
the cai-issued key, via the shared `Cai.Delivery` reference implementation) and the registry **verifies on ingest** —
schema, active trusted key, Ed25519 over the canonical payload, and verdict-reproduces — rejecting tampered, unsigned,
schema-invalid or dishonest packages. Reasons:

1. The master plan's M1 wire (`~/rearch/PLAN.md` §8) is *"Watchdog mints a signed Delivery → pushes to api.cai; cai
   stores + serves it"*, and the WS2 registry brief's Definition of Done is *"POST a signed Delivery succeeds and a
   TAMPERED one is rejected"* with the invariant *"scores never recomputed here."* Verify-on-ingest is that contract.
2. `deliveryId` sits INSIDE the signed payload, so a registry-assigned id would break the signature by construction;
   the producer mints the id (convention: `cd_…`, globally unique — uniqueness enforced at publish, `409` on collision).
3. The trust invariants hold unchanged: signed under the standard's key set, **verified by the registry**, not editable
   by the sharer — and the reproduce gate preserves the spirit of "signed only what was recomputed" (nothing that does
   not fold to its own headline is ever stored or served).

Key custody amendment (ADR-0010): in closed-loop v1 the signing key pair is **issued by cai and operated inside the
trusted producer's push path**; the registry holds only public keys. `payload.issuer` remains `cai.canine.dev` — the
signature is still the standard's attestation, exercised at its one trusted producer. Third-party producers (deferred)
will NOT receive cai-issued keys; that is where registry-side minting or a conformance regime becomes necessary.
