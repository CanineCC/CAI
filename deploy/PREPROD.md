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
| A delivery it accepts is signed by **preprod's** key, not production's | `Registry__KeysPath` on the preprod unit points at a key set holding only `kennel-preprod` — the public half of the key `provision-preprod.sh` mints for the preprod kennel tier |

★ **The kennel-side leak this closed.** `deploy-preprod.yml` in the kennel repo writes
`Kennel__Cai__NoiseApiUrl=https://api.codeassuranceindex.info` into the **production** env files, and says in
its own comment that it is set there rather than in `appsettings` so that preprod cannot "file a real
publication". But `provision-preprod.sh` derived the preprod env by **copying that file** and blanking only
`CaiRegistry` and SMTP — so the override was defeated by the copy it was written to survive. It had not yet
fired (checked 2026-08-21: both preprod env files carried zero entries, because they were provisioned before
that deploy step landed), and it would have fired on the next `provision`.

★ **The registry could not accept anything at all until 2026-09-03.** `TrustedKeyProvider` reads
`Registry:KeysPath`, and an unset path yields an EMPTY key set — so every publish was rejected. That is the
right failure mode for a box that has not been provisioned yet, and it is deliberately *Degraded, not
Unhealthy*, so a fresh slot still passes the deploy gate. The cost is that it looks like a tier waiting to be
finished rather than one that is broken, and nothing says which: `cai-web-preprod.service` had simply never
carried the line, while `cai-web.service` had carried it since standup. The health detail said so in as many
words — "no ACTIVE trusted signing key is configured — every publish is rejected" — on every read, for as
long as the tier had existed. A rehearsal of a submit would have failed at the ingest gate, which is
precisely the step this tier exists to prove.

The key it trusts is **preprod's own** (`kennel-preprod`), never `cai-ed25519-2026-07`. Trusting production's
signing key here would let the preprod register accept production-signed deliveries — the same identity
confusion the separate key was minted to prevent, pointed the other way.

## Standing it up

Once, from anywhere — no LAN needed, the runner is on the box:

```bash
gh workflow run "Deploy CAI to preprod" -R CanineCC/CAI -f provision=true
```

Then point the kennel preprod tier at it. **On the box** (five seconds, and the path an on-prem
operator should use):

```bash
bash deploy/preprod/set-noise-api.sh --status                # read it first
bash deploy/preprod/set-noise-api.sh http://127.0.0.1:8240   # then set it
```

Or off-network, which runs that same script on the runner — correct, but it queues behind every
other job on a single-capacity runner, and one push to main fans out into seven serialized builds:

```bash
gh workflow run "Preprod ops" -R CanineCC/kennel.canine.dev \
  -f action=set-preprod-noise-api -f value=http://127.0.0.1:8240
```

★ This key is **not** carried by any deploy. `deploy-preprod.yml` writes it into the PROD env files,
and `provision-preprod.sh` copies the prod env once at provision time; nothing re-derives it after
that. So it has to be set directly — "it will land on the next deploy" was never true for it.

Afterwards every push to `main` redeploys it, like the production tier.

## What this tier does NOT give you

- **A browsable preprod site.** The kennel preprod tier reaches this over loopback, which needs no vhost, no
  certificate and no DNS — and loopback is also how it stays exempt from the public rate limits. Fronting it
  at a `preprod.*` hostname needs an nginx vhost on **canine-dgx1**, which is the one host no GitHub runner
  can reach. See `~/Hentet/onprem-cai-preprod-setup.txt` for that hand-over.
- **A lifted embargo.** The embargo date is inside the **signed** corpus manifest and embedded in
  `Cai.Web.dll`; a preprod deployment built from `main` carries exactly the same one. A period publishes when
  its date arrives, here as in production — that is the point of signing it. Testing the published half of
  the register is what the fixture corpus in `kennel:tools/localdev/e2e/cai-e2e.py` is for, and it does it in
  a throwaway worktree rather than by putting a switch in production.
