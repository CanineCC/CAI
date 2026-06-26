# 0006 — Supply-chain attestation for build artifacts

- Status: Accepted
- Date: 2026-06-26

## Context

CAI publishes a reference scorer (the `Cai.Scoring` library, distributed as a NuGet package) and
runs a public site. As an assurance project, it should hold its own build pipeline to the
supply-chain-integrity bar it measures in others: are build actions pinned, is there an SBOM, are
artifacts signed, is provenance attested?

## Decision

Add a dedicated `supply-chain` GitHub Actions workflow (separate from the in-place deploy so the
critical deploy path stays minimal) that, for each build of the app:

- generates an **SPDX SBOM** of the published output (anchore/sbom-action → syft),
- **keyless-signs** the artifact and SBOM with cosign (GitHub OIDC → the public Sigstore/Rekor
  transparency log),
- attests **SLSA build provenance** (actions/attest-build-provenance).

Every GitHub Action across all workflows is **pinned to a commit SHA** (with the human-readable tag
in a trailing comment), not a floating tag.

## Consequences

- Consumers can verify what was built, that it was signed by this repository's identity, and how it
  was produced.
- Keyless signing and provenance publish to the public Sigstore transparency log on every run — an
  intentional, public footprint appropriate for an open project.
- SHA-pinned actions remove a class of supply-chain risk (a moved tag changing build behaviour) at
  the cost of periodic, deliberate pin bumps.
- The deploy workflow is unaffected: attestation lives in its own workflow and never gates the live
  service swap.
