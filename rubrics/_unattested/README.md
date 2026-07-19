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

### `rubric-2026.08.13/`

**Problem.** The catalog published under `rubric-2026.08.13` declares `"rubricVersion": "rubric-2026.08.14"`. It is
*not* a copy of the `.14` catalog (the two files differ), so it is neither a duplicate nor obviously the `.13`
content — its provenance is genuinely unknown. It was committed in `191649a` ("cai.canine.dev becomes the standard
authority") and had been served, mislabelled, ever since.

**Why it was not simply relabelled.** Editing the `rubricVersion` field to say `.13` would assert a provenance we
cannot demonstrate. A rubric catalog is an attestation; hand-editing one to make a check pass is the exact behaviour
the standard's credibility depends on never doing.

**How to restore it.** Regenerate from the engine commit that set `RubricVersion.Current = "rubric-2026.08.13"`. That
commit predates the relocation of the engine into the `kennel.canine.dev` repository (`ab6ab1d7`), so it lives in the
former `CodeHealth` / `watchdog.canine.dev` repository, which is not checked out on the machine where this quarantine
was created. With that repository available:

```bash
git checkout <commit-where-Current-was-rubric-2026.08.13>
dotnet run --project src/CodeHealth.Cli -- dimensions --catalog rubric-catalog.json
# confirm the emitted rubricVersion is rubric-2026.08.13, then:
mv rubric-catalog.json <CAI>/rubrics/rubric-2026.08.13/rubric-catalog.json
```

Then move the directory back out of `_unattested/` and delete this entry. The archive test will confirm it.

**If it cannot be recovered**, leave it withheld and record `rubric-2026.08.13` as an unrecoverable gap in
`CHANGELOG.md`. A version the archive admits it cannot serve is honest; a mislabelled one being served is not.
