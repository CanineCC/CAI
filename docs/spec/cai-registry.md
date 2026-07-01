# CAI registry — design (producer push / consumer pull / access grants)

- Status: Draft (closed-loop: Watchdog ↔ cai ↔ Assay)
- Companion to: [CAI-delivery package format](cai-delivery-package.md), [ADR-0010](../adr/0010-signed-cai-delivery-package-and-registry.md)
- Backs mockup 5: `~/watchdog-mock-5-access-sharing.html` (seller "Del bevis" / buyer "Anmod om adgang")

The registry is cai's role as **the binding middle** (`00-architecture.md`): producers push signed deliveries, consumers
pull them, and sellers grant buyers read access. This document sketches the API and the access model; the full
implementation and UI are out of scope (closed-loop first — no 3rd-party conformance machinery).

## 1. Roles

- **Producer** — the Watchdog surveyor. Measures code, pushes evidence. Closed-loop: exactly one registered producer.
- **Registry** — cai. The *only* party that signs. Recomputes, signs, stores, and mediates access.
- **Consumer** — the Assay buyer (acquirer, procurement, investor). Pulls granted deliveries; verifies offline; builds
  paid reports on top.
- **Owner** — the seller org that owns a repo's deliveries (derived from the producer account). Grants access to buyers.

## 2. Where the registry sits vs. the open API

The existing cai API is an **open, anonymous, rate-limited** standard surface (`/api/rubrics`, `/api/score`,
`/api/verify`) — the standard *is* the product ([ADR-0008](../adr/0008-api-access-control.md)). The registry is the
**first identity-gated surface**: pushing, pulling and granting all require an authenticated principal. This is a clean
extension of ADR-0008's default-deny posture — the open endpoints stay explicitly `AllowAnonymous`; `/api/registry/*`
does *not* opt out, so the default-deny fallback policy already protects it. Signing keys never leave cai's push path, so
a signed delivery can only originate from cai.

## 3. API sketch

All under `/api/registry`. JSON in/out. Authentication: producer = client credential (API key / OIDC client-credentials,
mTLS-capable); consumer/seller = user session (Keycloak OIDC per `00-architecture.md`). Errors reuse the existing
`{ "error": "..." }` / RFC 7807 shapes.

### 3.1 Producer push

```
POST /api/registry/deliveries            (producer credential)
  body: { evidence, subject, producer?, qualityBar?, idempotencyKey? }
  → 201 { deliveryId, package }           # cai recomputed + signed; returns the full signed package
```

cai **validates** the evidence (same validator as `/api/score`), **recomputes** the verdict via `Cai.Scoring`, stamps
`producer` from the authenticated caller, assigns `deliveryId` + `issuedAt`, **signs** ([format §5](cai-delivery-package.md#5-signing--canonicalization)),
and stores it. This recompute-then-sign step is the trust gate (`DeliveryBuilder` + `DeliverySigner` in the reference
impl). `idempotencyKey` (or an evidence+subject hash) makes a re-push return the same delivery rather than a duplicate.
**First scan on every repo is free**; the package is minted regardless (the free/paid line is the *reports*, not the
evidence — [ADR-0003](../adr/0003-free-paid-firewall.md)).

### 3.2 Consumer pull

```
GET /api/registry/deliveries/{id}        (owner OR active grantee)
  → 200 <signed package JSON>            # then verify OFFLINE (signature + reproduce)

GET /api/registry/deliveries/{id}/metadata   (owner OR active grantee)
  → 200 { deliveryId, subject, verdict.cai, verdict.band, issuedAt }   # light header, no evidence

GET /api/registry/keys                    (public)
  → 200 { keys: [ { keyId, alg, publicKey, status } ] }   # for offline verification; pinnable
```

Pull returns the artifact; **verification is client-side and offline** — the registry is a distributor, not a trusted
oracle. `/keys` is the one public registry endpoint (public keys are not secret).

### 3.3 Access grants (seller → buyer)

```
POST   /api/registry/grants               (seller = owner)
  body: { grantee: { orgId | email }, scope: "delivery"|"repo"|"selected",
          scopeRefs: [ deliveryId | repo ... ], expiresAt?, purpose? }
  → 201 { grantId, status: "active"|"pending" }   # "pending" when grantee is an email invite

GET    /api/registry/grants?direction=outgoing|incoming        (authenticated)
  → 200 [ { grantId, counterparty, scope, status, createdAt, expiresAt } ]

DELETE /api/registry/grants/{grantId}     (seller = owner)
  → 204                                    # revokes FUTURE registry access; see §5 on already-delivered copies
```

### 3.4 Access requests (buyer → seller, when no grant yet)

```
POST /api/registry/access-requests        (buyer)
  body: { target: { orgId | deliveryId }, scope, purpose }
  → 201 { requestId, status: "pending" }

POST /api/registry/access-requests/{id}/approve   (seller)  → 200 { grantId }   # becomes a grant
POST /api/registry/access-requests/{id}/decline   (seller)  → 200
GET  /api/registry/access-requests?direction=incoming|outgoing   → 200 [ ... ]
```

### 3.5 Directory & visibility (opt-in discoverability)

```
PATCH /api/registry/visibility            (seller)
  body: { visible: bool, profile: { displayName, sector, ... } }
  → 200

GET   /api/registry/directory?q=&sector=  (buyer)
  → 200 [ { orgId, displayName, sector, latestBand, shareableDeliveries } ]   # profile + BAND only
```

The directory exposes **only** a seller's opt-in profile and latest CAI *band* — never a package, never the number's
evidence. Discovery is the entry point to *request* access; it grants none.

## 4. How it backs the mockup-5 share flow

| Mockup 5 element | Role | Endpoint |
|---|---|---|
| Seller "**Del bevis**" on a bevis/Delivery | seller | `POST /grants` (scope + expiry + grantee) |
| Seller "**Delinger**" hub | seller | `GET /grants?direction=outgoing` |
| Seller "**Synlighed**" toggle + profile | seller | `PATCH /visibility` |
| Buyer "**Katalog**" (find visible targets) | buyer | `GET /directory?q=` |
| Buyer "**Anmod**" on a target | buyer | `POST /access-requests` |
| Seller approves a pending request | seller | `POST /access-requests/{id}/approve` → grant |
| Buyer "**Vurderinger**" (received evidence) | buyer | `GET /grants?direction=incoming` → `GET /deliveries/{id}` |
| "**✓ signatur ok**" badge | buyer | client-side offline verify (`DeliveryVerifier`, `/keys`) |
| "invite via e-mail" fallback (both ways) | either | `POST /grants` or `/access-requests` with `grantee.email` |
| "**Generér rapport (betalt)**" | buyer | Assay — out of scope here (the paid line, [ADR-0003](../adr/0003-free-paid-firewall.md)) |

Both entry flows from `00-architecture.md` are covered: **seller-shares** (`POST /grants`, consent built in) and
**buyer-brings-access** (`POST /access-requests` → seller approves).

## 5. Access model (ADR-0018: visibility vs. authority)

The registry keeps two axes strictly separate (per the kennel **ADR-0018**, *visibility vs. authority*):

- **Visibility** — can you *discover that something exists*? The opt-in directory: a seller's profile + latest band.
  Visibility yields **no** access to any package.
- **Authority** — can you *read the actual signed delivery*? Conferred **only** by an active grant (directly, or via an
  approved access request).

Seeing a seller in the catalog never yields their evidence; only an approved grant does. The seller approves every
request, so consent is explicit on the seller-shares and buyer-brings-access paths alike.

**Authority governs distribution, not authenticity.** A delivery is a signed, point-in-time artifact: once a buyer holds
a copy (they got it free), revoking the grant stops *future* registry reads and removes directory discoverability, but
the copy stays cryptographically valid. This is intentional — the value model is "one evidence → many reports," and the
artifact's trust comes from cai's signature, not from continued registry permission. Sellers are told this at share
time (the artifact is a snapshot they chose to hand over).

## 6. Out of scope (deferred)

- 3rd-party scanner conformance / certification (multiple producers, proving evidence honestly reflects code).
- Full registry storage/UI, notifications, billing hooks (the Assay paid-report side).
- Federation / multi-tenant key delegation. Closed-loop v1 has one producer and one cai signer.
