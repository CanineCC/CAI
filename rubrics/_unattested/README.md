# Unattested rubric catalogs — withheld from publication

Catalogs in this directory are **not served** by `RubricCatalogStore` and do not appear in `GET /api/rubrics`.
They are kept here as evidence, not withdrawn from the repository, so the gap is auditable rather than erased.

A catalog is moved here when the archive cannot attest what it is — specifically when the `rubricVersion` the
document declares does not match the directory it was published under. Serving such a file would hand a consumer a
definition of the standard under a version label that is not the one the document claims, which is precisely the
failure the frozen-rubric contract exists to prevent (see `docs/adr/0004-versioned-frozen-rubrics.md`).

The invariant is enforced in code (`RubricCatalogStore`, attestation) and in
`tests/Cai.Tests/RubricArchiveTests.Every_published_catalog_declares_the_version_it_is_published_under`.

## Current contents

**None.** The directory is kept because the invariant it enforces still applies: a catalog whose
declared `rubricVersion` does not match the directory it sits in is withheld here rather than served.

### Resolved

#### `rubric-2026.08.13` — recovered 2026-08-09

The catalog published under `rubric-2026.08.13` declared `"rubricVersion": "rubric-2026.08.14"`, and its
provenance could not be established from the machine where the quarantine was created — the engine commit
that set `RubricVersion.Current = "rubric-2026.08.13"` predates the engine's move into
`kennel.canine.dev` (`ab6ab1d7`), and the former repository was not checked out there.

It was still on GitHub, as `CanineCC/RETIRED.watchdog.canine.dev`. Regenerating from commit
`dc4bef3e` emitted a catalog declaring `rubric-2026.08.13`, which is now published. The recovered
document is **not** the quarantined one — 121 dimensions against 122 — so the quarantine was correct
to refuse it, and relabelling it would have asserted a false provenance.

The same recovery published twelve further versions that production runs referenced but the archive
never held: `2026.06.0`–`.5`, `.7`, `.10`, `.11`, `.17`, and `2026.08.10`, `.11`.
