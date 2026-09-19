# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the reference scorer
(`Cai.Scoring`) follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The rubric catalogs are versioned independently and by date under `rubrics/` — any change that can
move a score for unchanged evidence mints a new rubric version (see
[ADR-0004](docs/adr/0004-versioned-frozen-rubrics.md)).

## [Unreleased]

### Security
- **A traversing rubric version could serve a catalog from outside the archive.** `rubricVersion` reaches
  `Path.Combine` from three anonymous sources — the `/api/rubrics/{version}` routes and the `rubricVersion` inside
  an uploaded evidence bundle on `/api/score` and `/api/verify` — and the only check was `IsNullOrWhiteSpace`.
  Attestation did not contain it: `IsAttested` compares the document's own declared version against the requested
  string, so a catalog placed outside the root declaring that traversal satisfied every check. `RubricCatalogStore`
  now gates `Get`, `RawCatalogJson` and `DeclaredVersion` on `RubricVersionOrder.IsWellFormed` before touching the
  filesystem. The same guard closes an unbounded attestation cache that a stranger could grow one row per request.
  Covered by a regression test that fails without it.
- **The publish workflow interpolated untrusted values and a secret into `run:` scripts.** The dispatch input, the
  laundered `steps.ver.outputs.version` and `secrets.GITHUB_TOKEN` are now bound via `env:` and read as shell
  variables, and the resolved version is validated against a semantic-version pattern before it leaves its step.
  That job holds the nuget.org publish key.
- **The SQLite migration helpers quote their identifiers.** `AddColumnIfMissing` in both stores quotes table and
  column names with SQLite's own delimiter (doubling any embedded one) and holds the column definition to the
  narrow shape those files emit. DDL takes no parameters, so this is what "parameterise it" means here.
- The `api` and `app` vhosts redirect to their own literal hostname rather than echoing `$host`; Dependabot waits
  7 days before proposing a newly published version; the a11y workflow runs on Node 22 rather than EOL Node 20.

### Changed
- **Every production project groups its files by role** — `Domain/`, `Infrastructure/`, `Endpoints/`, `Pages/` —
  with the convention and what each project's shape claims written down in `docs/architecture.md`. Namespaces are
  declared explicitly and do not follow the folders, so this moved files and nothing else. `Cai.Web.Noise` was a
  flat 28-file folder mixing the standard's rules, its SQLite store and its HTTP surface; `Cai.Scoring` now shows
  ADR-0002's determinism claim in the tree, with one file in `Infrastructure/` and eleven in `Domain/`.
- **The registry and the Noise Standard are their own projects** — `src/Cai.Web.Registry` and
  `src/Cai.Web.Noise`, mapped into `Cai.Web` rather than living inside it
  ([ADR-0011](docs/adr/0011-one-project-per-standard-under-the-web-host.md)). The namespaces already
  layered this way; now the compiler enforces it, so neither standard can drift into the other or into
  the site by proximity. Files moved, namespaces did not; `NoiseStandardHealthCheck` is public because
  registering it from `Program.cs` now crosses an assembly boundary. `dotnet publish src/Cai.Web` is
  unchanged and pulls both libraries in.
- **`INoiseStore` is now six role interfaces** — `INoiseSubmissionStore`, `INoiseJudgingStore`,
  `INoisePublicationStore`, `INoiseDisputeStore`, `INoiseFindingStore`, `INoiseCostStore` — with `INoiseStore`
  composing them for the few readers that genuinely span the record. Twenty-eight members on one interface meant
  every handler and page declared a dependency on all of them; seventeen call sites now declare the one role they
  use, so the cost endpoint can no longer record a verdict. `AddNoiseStandard()` registers the single store under
  every role, so the narrowing changes what a caller may reach for, not which store it gets.
- **The web projects are internal by default.** `Cai.Web.Noise` exposes two types where it exposed a hundred and
  fourteen, `Cai.Web.Registry` thirteen where it exposed seventeen, and `Cai.Web` one. The Noise Standard's three
  Blazor pages moved into `Cai.Web.Noise` beside the store they read, which is what let its data model stop being
  public; the host tells the router about the second assembly. The two published libraries, `Cai.Scoring` and
  `Cai.Delivery`, are untouched — their surface IS the product.

### Removed
- **`examples/cai-delivery.sample-key.json`.** The sample's private seed was published deliberately, but
  verifying the example only ever needed the public half in `examples/cai-delivery.keys.json`, and
  regenerating it runs `tools/resign-sample`, which mints a fresh keypair anyway. A committed private key
  reads as a leak to every scanner and every reader, whichever it is. The tool now writes the seed outside
  the repository and prints where.

### Removed — BREAKING (0.2.0)

- **`DeliverySigner.Sign(DeliveryPayload)` is no longer public.** Signing a delivery has one entry point,
  `SignPackage`, which stamps the payload with this signer's key id and signs *that*. A detached signature handed
  out on its own is only ever correct for the payload it was taken over, and nothing in the system asked for one —
  the method had no caller outside the class. Keeping it public invited signing one payload and shipping another,
  which is the case `SignPackage` exists to make unrepresentable.

- **There is no longer any way to fold a CAI score, or read a band word, without naming the rubric it was
  computed under.** `CaiScorer.Score(EvidenceBundle)`, `CaiScorer.Verify(EvidenceBundle, double)` and
  `Bands.For(double)` are DELETED, and `catalog` is non-nullable on what remains.

  Why removed rather than deprecated: `1d557c3` let a catalog pin the fold's constants and the cutlines,
  and four of the five call sites in the system kept calling the overload that silently substitutes
  `ScoringParameters.Default` — including `DeliveryBuilder`, the mint-time trust gate, and
  `DeliveryVerifier`, the consumer's reproducibility check. `[Obsolete]` is a warning: the build stays
  green and the wrong number still ships. For a VERIFIER that is the worst failure mode available — the
  caller believes they reproduced a headline when they re-folded it under their own build's constants. A
  compile error is strictly better than a confidently wrong verdict.

  **The fallback survives, because it is not the defect.** A catalog publishing no `scoring` block still
  resolves to `ScoringParameters.Default`, and one predating `category` still folds on the bundle's own —
  which is what keeps all 38 published rubric versions verifying to the same number. "This catalog pins
  nothing" is a fact about a rubric; "I fetched no catalog" was a fact about the caller. Only the second
  is now unrepresentable.

- `DeliveryBuildRequest.RubricContentHash`. The builder DERIVES the digest from the rubric it folded
  under; accepting one from the caller let a payload witness one document while its number came from
  another — the single thing a content digest exists to prevent.

- `DeliveryVerifier.Verify(..., bool reproduce, ...)`. A flag that silently changed what "verified" means.
  Split into `VerifySignature` (authenticity alone, folds nothing) and `Verify` (rubric required, always
  reproduces, and REFUSES a rubric whose version or digest is not the one the package witnesses).

### Fixed
- **`tools/resign-sample` wrote a sample the registry would reject.** It serialized the package with its own
  `WriteIndented` options instead of the library's `ToJson()`, so null-valued properties survived — and
  `"surveyFit": null` fails the versioned package schema, which types that field as an object and reads ABSENT as
  "no clarity figure". The tool now writes every file through the type's own `ToJson()`, which is the point of
  having those methods. It had also stopped compiling against `DeliveryVerifier.Verify`, which now requires the
  rubric; it resolves one from the published archive and proves the sample re-folds before claiming anything.
- **`examples/cai-delivery.sample.json` and `.keys.json` regenerated** by that tool: the same payload, minted
  under a fresh `cai-ed25519-sample` keypair and written in the library's wire form (no null-valued properties).
  The seed that was published alongside earlier samples now corresponds to nothing that ships.
- A `<see cref="ToJson"/>` on `CatalogDimension.DeepScan` pointed at a member of a different type; it now names
  `RubricCatalog.ToJson`.

- **The registry's two rubric endpoints disagreed about what a version contains — for all 38 published
  versions.** `/api/rubrics/{v}/digest` hashes the RAW archived document; `/api/rubrics/{v}/catalog`
  serves `RubricCatalog.ToJson()`. `CatalogDimension.DeepScan` was a non-nullable bool, so `ToJson()`
  added `"deepScan": false` to every dimension the catalog endpoint served while the digest endpoint
  hashed a document without it. Canonicalization does not rescue that — it normalizes formatting and
  ordering, not an added field. Any consumer fetching the catalog and checking it against the digest
  endpoint, or against a delivery's `rubricContentHash`, would have concluded the archive had been
  tampered with. `DeepScan` is now `bool?`: absent means the catalog does not say, which is the truth —
  no publisher has ever emitted the field.

- **`ScoringParameters` compared by reference where it promised value semantics.**
  `QualityBarParameters` holds two dictionaries, and a record's generated equality compares members with
  `EqualityComparer<T>.Default` — reference equality for a dictionary. Parameters parsed off the wire
  never equalled the identical parameters in memory, so every check of the form "does this rubric version
  pin the same rules?" quietly answered "different". Now structural, with an order-independent hash.

- `ResolvedRubric.ContentHash` is nullable, and `FromCatalog` produces null. The digest exists so a holder
  can re-fetch the named version and prove it was not edited; that check needs something to fetch. A
  catalog built in memory has no published counterpart, so digesting its serialized form asserted a check
  nobody can perform — and the result was indistinguishable from a real witness.

- The rubric-mismatch refusal names the in-memory case instead of rendering a blank digest. A guard that
  aborts correct-looking work and cannot say why is a guard people learn to delete.

### Added
- **The rubric catalog now pins the fold's own constants and the band cutlines (`scoring` block), so a
  rubric version selects its scorer semantics.** ADR-0004 requires a catalog to pin every input that can
  move a score, but the OWA decays, the critical gate, the architecture surface floor and the band
  cutlines lived only as `const` in `Cai.Scoring`: `rubricVersion` selected the dimension→category map
  and nothing else, so verifying an old report ran the current build's constants. Two documents also
  disagreed about the cutlines — `Band.cs` said they must not vary by rubric version, while
  `QualityBarBands` already shifted all four by the evidence-carried `qualityBar` and called itself
  their single source of truth (and both restated `90/70/50/25`, so editing one would have diverged
  them silently). `RubricCatalog.Scoring` now carries all of it and the scorer folds under the
  catalog's values, which means old rubrics replay because their own catalog says what they meant —
  no version-dispatch switch, and no historical code path kept alive.
  **Nothing moved.** A catalog without the block resolves to `ScoringParameters.Default`, the values
  the scorer has always used, and the block is omitted from the serialized form, so no archived
  catalog's content digest changes and every published version verifies to the same number. The 631
  existing tests pass unchanged; 11 new ones pin that a published block governs the fold and that two
  rubric versions replay their own parameters from the same evidence.

- **A signed delivery now carries how complete the survey behind it was (`evidence.surveyFit`).** A CAI is a fold of
  the evidence that was produced, and the artifact said nothing about how much of the survey actually resolved — so a
  headline over ten resolved lenses and the same headline over four were indistinguishable to the party holding the
  report. They are different claims. The bundle now carries the producer's per-scan survey clarity: `depthApplicable`
  (domain/architecture lenses that applied to this codebase at all), `depthFired` (those that resolved), and the
  optional `languageApplicability`, `band` and `explanation` the producer publishes. The gap between the first two is
  the survey's blind spots. Note what the denominator does: a lens that does not APPLY to a repository is not counted
  in it, so "not applicable" stops being the same silence as "applicable but blind" — the first was never held against
  the repo and now visibly isn't.
  **It is descriptive and non-scored, permanently.** `CaiScorer` reads none of it and a test pins that the worst
  possible clarity — every applicable lens blind — cannot cost a repository a single point. That is deliberate: a thin
  survey is not a bad codebase, and scoring it as one would be the exact confusion the figure exists to prevent. It
  rides inside the signed payload like `rebuildCost`/`busFactor`/`topology` (verbatim, tamper-evident, omitted from the
  canonical form when absent), so every signature already in someone's hands is unaffected, and no rubric version is
  minted — it cannot move a score for unchanged evidence.

- **Thirteen historical rubric catalogs published, closing the archive's back-gap.** Production runs
  referenced `rubric-2026.06.0`–`.5`, `.7`, `.10`, `.11`, `.17`, `rubric-2026.08.10`, `.11` and `.13`,
  but the archive had never held them — so a report naming one could not be verified by the party it
  was handed to, which is the one promise the frozen-rubric contract exists to make. Each was
  regenerated from the engine commit that set `RubricVersion.Current` to that version, and each emitted
  catalog declares the version it is published under.
- **`rubric-2026.08.13` is out of quarantine.** Its published catalog declared `rubric-2026.08.14` and
  its provenance was unrecoverable from the machine where the quarantine was created; the pre-relocation
  engine repository turned out to still exist as `CanineCC/RETIRED.watchdog.canine.dev`. The recovered
  catalog differs from the quarantined one (121 dimensions against 122), so the quarantine was right to
  refuse it rather than relabel it. `rubrics/_unattested/` is now empty and kept only for the invariant.

- **The rubric catalog now publishes the dimension→category map, closing the one score-moving input it did not
  pin.** A dimension's influence on the score IS its category — dimensions in one category average together
  (confidence-weighted) before their lens's worst-first fold sees them — but the catalog only ever published the
  `lens`, and several categories share a lens (`code-quality` and `explicit-debt` both feed Code Health), so the
  assignment was not recoverable from the published document. It lived solely as a property on each analyzer class in
  the producer. Re-homing a dimension therefore moved published scores for unchanged evidence while the rubric version
  stood still — precisely what [ADR-0004](docs/adr/0004-versioned-frozen-rubrics.md) states is impossible. From
  `rubric-2026.08.18` every scored dimension carries its `category`, and `CaiScorer.Score(bundle, catalog)` /
  `Verify(bundle, catalog)` fold under the CATALOG's assignment rather than the producer's: a bundle that contradicts
  the frozen map is refused, naming both sides, instead of being scored under a map no verifier can fetch, and a
  catalog naming a category the scorer does not implement fails closed. `/api/score` and `/api/verify` now resolve the
  catalog for the version the bundle names. **No score changed**: the published categories are exactly the ones the
  evidence already declared, so the same bundle folds identically with and without the catalog (proved by a test), and
  the engine's golden rubric snapshot moved only its version string.
- **`rubric-2026.08.18` is published** — `.17` plus the `category` field on all 42 scored dimensions. A pure
  catalog-schema addition (backward-compatible; old consumers ignore the field), versioned because the catalog
  *contract* changed and catalogs are versioned by rubric — the same reason `.17` was minted for `scoringPolarity`.
- **`CatalogDimension` models `scoringPolarity`.** `.17` added it to the archive but the scorer's model did not carry
  it, so `/api/rubrics/{version}/catalog` — which serves the re-serialized model — silently dropped it on the way out.
  An archive that cannot serve what it holds is not an archive; a test now pins the id/category/lens/polarity
  round-trip. (`deepScan` remains a non-nullable `bool`, so it is still emitted as `false` for catalogs that omit it —
  cosmetic, and changing it would break the public model's shape.)
- **Anyone can now verify a signed survey, anonymously** — `POST /api/verify-delivery` plus a working tool on
  `/verify`. Signature checking previously existed only in the CLI and on registry ingest, which put it exactly where
  the person who needs it is not: the party HANDED a signed survey is who the signature is for, and the least likely
  to install tooling to use it. The endpoint reports the two claims separately and refuses to let one vouch for the
  other — authenticity (Ed25519 over the canonical payload, against the published key set) and reproducibility
  (re-folding the embedded evidence). A package that is genuinely ours but states a number its own evidence does not
  produce is reported as signed-but-not-reproducing, never as trustworthy. The response echoes the subject
  (repository, commit, rubric, issuer, key) because a valid signature attests the document, not that it describes
  the code the recipient was shown.
- **`rubric-2026.08.16` and `rubric-2026.08.17` are published.** The archive had stopped at `.15` while the engine
  had been stamping surveys `.16` and `.17`, so a freshly signed survey named a rubric the public could not fetch and
  its recipient could not verify it. Both were generated from the engine commits that actually set
  `RubricVersion.Current` to each version, not written by hand: `.16` adds D40–D42 (runtime hardening), `.17` adds
  `scoringPolarity` metadata — matching the documented bump notes exactly.

### Changed
- **The archive now serves only catalogs it can attest.** `RubricCatalogStore` enforces that a catalog's declared
  `rubricVersion` matches the directory it is published under; mismatched or unparseable catalogs are withheld from
  `Versions()`/`Get()` and reported by the new `UnattestedVersions()` so the gap is visible rather than silent.
- **`Cai.Scoring` and `Cai.Delivery` are published to nuget.org at 0.1.3**, with GitHub Packages kept as a mirror.
  The shipped scorer was never actually public: consumers used `0.1.3-ws-e` vendored as a file, while the only
  published artifact was `0.1.0` on GitHub Packages — which requires a GitHub account even for public feeds, so
  "read our algorithm and check our number" was not true for an anonymous third party. `Cai.Delivery` had no publish
  pipeline at all. Requires the `NUGET_API_KEY` repository secret; without it the workflow warns and publishes only
  the mirror rather than failing.

### Fixed
- **Both packages now declare Apache-2.0**, matching the repository `LICENSE`, and ship the licence text inside the
  package. They previously declared MIT in `PackageLicenseExpression` — a standard whose reference implementation
  carries contradictory licence metadata is not credibly open.
- **`cai sign` emitted an invalid RFC 3339 `issuedAt`** on any machine whose locale does not use `:` as the time
  separator. `':'` in a custom format string is the *current culture's* time separator, so on a Danish-locale box it
  produced `2026-07-19T09.28.09Z` — dots for colons — baked into the signed payload, where it cannot be corrected
  without invalidating the signature. Now formatted with `InvariantCulture`, with a regression test that pins the
  behaviour under a hostile locale.

### Withheld
- **`rubric-2026.08.13` is withdrawn from publication** to `rubrics/_unattested/`. The catalog published under that
  name declares itself `rubric-2026.08.14` and is not a copy of `.14`'s file, so its provenance is unknown; it had
  been served that way since `191649a`. Relabelling it would assert provenance we do not have. The commit that set
  `Current` to `.13` predates the engine's move into the kennel repository and was not available when this was
  found — `rubrics/_unattested/README.md` records the recovery procedure.
- **The rate limiter no longer throttles the registry's own principals** (production operability): the open API's
  anonymous per-IP budget (1/s · 3/min · 15/day) also covered registry traffic, and since Watchdog + Assay call from
  ONE LAN IP it 429ed `/api/registry/keys` and delivery GETs mid-loop. The limiter is now traffic-class aware
  (`ApiRateLimiting`): a VALID registry bearer rides a generous per-PRINCIPAL budget (600/min — a runaway-client
  fuse; the credential is the abuse control), the registry's anonymous public probes `keys` + `health` get their own
  per-IP budget (300/min — an offline-verify loop over a whole corpus cannot trip it, a flood still does), and all
  other anonymous `/api` traffic keeps the tight open-API budget — including requests with an INVALID token, which
  also throttles token guessing. Covered by a dedicated rate-limiting suite (authenticated burst past the public
  budget, exhausted-IP + credential, tampered token, offline-verify loop, flood ceiling).
- **`/api/auth/session` and `/api/auth/signin` fail closed and clean, never `500`**: the prod smoke probed these
  paths (there is no interactive sign-in surface on this host) and the deployed build answered `500` — the same
  family as the unconfigured-registry bug below: unmatched paths fall to the default-deny fallback policy, whose
  challenge threw while the bearer scheme was registered conditionally. With the scheme unconditional they answer
  `401` + `WWW-Authenticate: Bearer` + a JSON error (an authenticated probe gets an honest `404`); a dedicated test
  suite now pins that contract for the `/api/auth/*` family on both the zero-config boot and the configured app.
- **Unconfigured registry is safe-by-default, never `500`** ([spec §2/§3.4](docs/spec/cai-registry.md)): production
  ran the default-deny fallback policy with `AddAuthentication()` registering NO scheme, so every request without
  endpoint-level `AllowAnonymous` — all of `/api/registry/*` included — threw
  `No authenticationScheme was specified` and returned `500`. The `RegistryBearer` scheme is now registered
  unconditionally (empty principal set included), so denied requests are challenged with `401`; the new public
  `GET /api/registry/health` answers `200 Healthy` / `200 Degraded` (unconfigured — publishes rejected) /
  `503 Unhealthy` (store unreachable), and `GET /api/registry/keys` stays public (empty set when unconfigured).
  Covered by a dedicated unconfigured-boot test suite (no principals, no key file).

### Added
- **The registry API** (`/api/registry`, [ADR-0010](docs/adr/0010-signed-cai-delivery-package-and-registry.md)
  addendum + [contract](docs/spec/cai-registry.md)): producer push of signed CAI-delivery packages with
  verification on ingest (versioned JSON-schema + trusted-active-key + Ed25519 over the canonical payload +
  verdict-reproduces; tampered/unsigned/schema-invalid rejected), immutable storage (idempotent identical
  re-push, `409` on same-id/different-content), consumer pull (verbatim package, metadata, list), seller→buyer
  access grants (delivery/repo scope, expiry, revoke; ungranted reads are indistinguishable `404`s), the public
  `GET /api/registry/keys` key set, and a registry-store `/health` contribution. First identity-gated surface:
  bearer-token principals with a fixed claim contract as the Keycloak seam; authenticated registry calls are
  exempt from the open-API rate budget. Storage = SQLite behind the `IRegistryStore` (Postgres) seam.
- Architecture Decision Records under `docs/adr/` and a high-level `docs/architecture.md` with C4 /
  component diagrams.
- `Cai.slnx` repository solution so Roslyn-based tooling loads the whole project graph
  ([ADR-0007](docs/adr/0007-repository-solution-file.md)).
- Observability in `Cai.Web`: OpenTelemetry tracing + metrics, a `/health` readiness check, and
  structured logging at the request boundaries.
- Resilience pipeline (timeout + retry + circuit breaker) on the outbound surveyor call.
- Security response headers (CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy), secure
  antiforgery cookie flags, and inbound validation of `/api/score` + `/api/verify` payloads.
- `supply-chain` workflow: SPDX SBOM, keyless cosign signing, and SLSA build provenance; all GitHub
  Actions pinned to commit SHAs ([ADR-0006](docs/adr/0006-supply-chain-attestation.md)).

### Changed
- Deploy workflow binds to a `production` environment and gates the swap on the `/health` probe.

## [scoring-v0.1.0] — 2026-06-26

### Added
- Initial public release of `Cai.Scoring`, the deterministic reference scorer (evidence bundle → CAI
  headline + per-lens contributions), published as a NuGet package on GitHub Packages.
- Full rubric fold: category layer, architecture surface floor, quality-bar bands, coherence, and
  critical gating.
- `cai.canine.dev`: the standard's UI, the rubric + scoring JSON API, an in-browser calculator, a
  public registry view, `/llms.txt`, and a schema.org JSON-LD glossary.

[Unreleased]: https://github.com/CanineCC/CAI/compare/scoring-v0.1.0...HEAD
[scoring-v0.1.0]: https://github.com/CanineCC/CAI/releases/tag/scoring-v0.1.0
