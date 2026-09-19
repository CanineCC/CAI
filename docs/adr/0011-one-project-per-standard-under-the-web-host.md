# 0011 — One project per standard, under a thin web host

- Status: Accepted
- Date: 2026-09-19

## Context

`Cai.Web` started as the site plus a small JSON API and ended up carrying three unrelated jobs: the
calculator site, the signed-delivery registry ([ADR-0010](0010-signed-cai-delivery-package-and-registry.md))
and the Noise Standard. At roughly 10,000 lines and 139 public types across four namespaces, it had
become the place new code landed by default rather than by decision.

The namespaces already told the truth the project file did not. `Cai.Web.Noise` used
`Cai.Web.Registry`, `Cai.Web.Registry` used `Cai.Delivery` and `Cai.Scoring`, and neither reached
back into the host's own types. The layering was real; only the compiler was not being told about it,
so nothing stopped the next edit from wiring the registry into a page or the site into the corpus.

## Decision

Give each standard its own project, and leave `Cai.Web` as the host that maps them in:

- `src/Cai.Web.Registry` — the registry's endpoints, store, access rules, trusted keys and the
  embedded package schema.
- `src/Cai.Web.Noise` — the Noise Standard's endpoints, store, signed corpus, judging pipeline and its
  three Blazor pages (`/noise/mark`, `/noise/rate`, `/noise/record`), which read that store directly and
  so belong beside it. It references `Cai.Web.Registry`; the reverse reference does not exist and must
  not be added.
- `src/Cai.Web` — `Program.cs`, the site's own pages, the API edge (access control, rate limiting,
  evidence validation) and nothing else. It references both libraries.

Both libraries map endpoints into a host rather than starting one, so neither uses the Web SDK:
`Cai.Web.Registry` builds under the plain SDK and `Cai.Web.Noise` under the Razor SDK (it carries
components), both with a `FrameworkReference` to `Microsoft.AspNetCore.App`. Namespaces are unchanged,
so the split moved files and project files only. Each library carries the package references it
actually uses, and the embedded resources (the delivery schema, the signed corpus) moved with the code
that reads them, keeping their existing logical names so no lookup changed.

Each module registers its own services behind one extension method — `AddNoiseStandard()` for the Noise
Standard — and each library is **internal by default**: it exposes what the host calls and nothing
else. `Cai.Web.Noise` publishes two types (`NoiseStandardEndpoints`, `NoiseStandardRegistration`) and
keeps its eighty-odd records, stores and judging types to itself.

## Consequences

- The dependency rule is now enforced by the build rather than by habit: a page cannot reach into the
  registry's store, and the registry cannot come to depend on the Noise Standard, without someone
  adding a `ProjectReference` and having to justify it in review.
- The host no longer names the Noise Standard's storage or its health check: `AddNoiseStandard()` wires
  both, and which database the submission register lives in is the module's own decision again. The
  same move keeps the module's data model internal, which is why the split did not cost a public API.
- The Noise pages are discovered from a second assembly, so the router is told about it
  (`AdditionalAssemblies` on `Routes.razor`, `AddAdditionalAssemblies` on `MapRazorComponents`). A new
  page in that project needs no further wiring; a new *project* of pages would.
- The publish path is unchanged — `dotnet publish src/Cai.Web` pulls both libraries in — so the
  verify-before-swap deploy ([ADR-0005](0005-verify-before-swap-deploy.md)) needed no change.
- Three projects means three project files to keep current, and a shared change now spans more of
  them. That is the cost of the boundary; it is the point of it too.
- The convention from [ADR-0009](0009-conventional-src-tests-layout.md) extends: a new standard gets a
  project under `src/`, not a folder under the host.
