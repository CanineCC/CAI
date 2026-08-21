# The preprod copy of the standard

`cai-web-preprod.service` on **canine-wrx1:8240**, deployed by
[`.github/workflows/deploy-preprod.yml`](../.github/workflows/deploy-preprod.yml) on every push to `main`.

```
kennel preprod (admin :8230 / watchdog :8210) ──http──▶ 127.0.0.1:8240  (cai-web-preprod.service)
                                                             │
                                             /home/jimmy/apps/cai-web-preprod/registry-data/cai-registry.db
                                                     (its OWN store — never production's)
```

## Why it exists

The operator console's submit posts an **intent record** and a **publication**, and decision #14's
no-withdrawal rule makes both permanent — there is deliberately no delete and no replace. So before this
tier, a rehearsal on preprod had two options, and both were bad:

- point the preprod console at the **live** standard, and file a real, unretractable result under our own
  tool name; or
- point it at **nothing**, and rehearse none of the thing you came to rehearse.

This is the third option. Preprod writes here, production writes to production, and neither can see the
other's register.

## What makes it safe, and how that is enforced rather than assumed

| Property | Enforced by |
| --- | --- |
| Its database is not production's | a step in `deploy-preprod.yml` reads both units' `Registry__DbPath`, **refuses to deploy** if they match, and refuses if preprod sets none (which would fall back to production's default) |
| The kennel preprod tier cannot reach the live standard | `deploy/preprod/provision-preprod.sh` in the kennel repo blanks `Kennel__Cai__NoiseApiUrl`, and the `set-preprod-noise-api` action refuses the live hostname outright |
| It cannot mint the corpus signing key | the deploy fails if production's key is absent, rather than generating one |
| It cannot rewrite the published corpus | the workflow has `contents: read`; only the production deploy commits the public key back |

★ **The kennel-side leak this closed.** `deploy-preprod.yml` in the kennel repo writes
`Kennel__Cai__NoiseApiUrl=https://api.codeassuranceindex.info` into the **production** env files, and says in
its own comment that it is set there rather than in `appsettings` so that preprod cannot "file a real
publication". But `provision-preprod.sh` derived the preprod env by **copying that file** and blanking only
`CaiRegistry` and SMTP — so the override was defeated by the copy it was written to survive. It had not yet
fired (checked 2026-08-21: both preprod env files carried zero entries, because they were provisioned before
that deploy step landed), and it would have fired on the next `provision`.

## Standing it up

Once, from anywhere — no LAN needed, the runner is on the box:

```bash
gh workflow run "Deploy CAI to preprod" -R CanineCC/CAI -f provision=true
```

Then point the kennel preprod tier at it:

```bash
gh workflow run "Preprod ops" -R CanineCC/kennel.canine.dev \
  -f action=set-preprod-noise-api -f value=http://127.0.0.1:8240
gh workflow run "Preprod ops" -R CanineCC/kennel.canine.dev -f action=noise-api-status   # read it back
```

Afterwards every push to `main` redeploys it, like the production tier.

## What this tier does NOT give you

- **A browsable preprod site.** The kennel preprod tier reaches this over loopback, which needs no vhost, no
  certificate and no DNS — and loopback is also how it stays exempt from the public rate limits. Fronting it
  at a `preprod.*` hostname needs an nginx vhost on **canine-dgx1**, which is the one host no GitHub runner
  can reach. See `~/Hentet/onprem-cai-preprod-edge.txt` for that hand-over.
- **A lifted embargo.** The embargo date is inside the **signed** corpus manifest and embedded in
  `Cai.Web.dll`; a preprod deployment built from `main` carries exactly the same one. A period publishes when
  its date arrives, here as in production — that is the point of signing it. Testing the published half of
  the register is what the fixture corpus in `kennel:tools/localdev/e2e/cai-e2e.py` is for, and it does it in
  a throwaway worktree rather than by putting a switch in production.
