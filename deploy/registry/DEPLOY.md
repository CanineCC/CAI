# Deploying the cai registry — api.cai.canine.dev

The registry (ADR-0010, [spec](../../docs/spec/cai-registry.md)) ships **inside Cai.Web** — SQLite
store + bearer-token principals, under `/api/registry`. So `api.cai.canine.dev` is **a second
hostname on the existing `cai-web.service`** (canine-wrx1 :8090), not a new deployable: the normal
[`deploy.yml`](../../.github/workflows/deploy.yml) pipeline (push to main → wrx1 runner → verify →
publish → bounce → health-gate → rollback) deploys it.

```
client ──https──▶ dgx1 nginx (TLS)
  cai.canine.dev      ──http──▶ wrx1 192.168.1.10:8090 ┐
  api.cai.canine.dev  ──http──▶ wrx1 192.168.1.10:8090 ┴─ cai-web.service (Cai.Web incl. /api/registry)
                                          │
                                          └─ /home/jimmy/apps/cai-web/registry-data/   ← survives deploys
                                             ├─ registry.db          (SQLite: deliveries + grants)
                                             └─ trusted-keys.json    (DeliveryPublicKeySet)
```

## Server config (wrx1) — persistence must survive the deploy swap

The deploy swaps `~/apps/cai-web/app` wholesale; the registry's default `data/registry.db` resolves
under the content root and would be deleted. The systemd drop-in
[`cai-web.service.d-registry.conf`](cai-web.service.d-registry.conf) (installed at
`/etc/systemd/system/cai-web.service.d/registry.conf`, **applied 2026-07-02, takes effect on the
next deploy/restart**) points the store at the stable `registry-data/` dir and loads secrets from
`~/.config/cai/registry.env` (0600, optional until configured):

```bash
# ~/.config/cai/registry.env — SECRET bearer tokens (closed-loop v1 auth; Keycloak comes later)
Registry__Principals__0__Token=<openssl rand -hex 32>
Registry__Principals__0__OrgId=canine
Registry__Principals__0__Name=watchdog.canine.dev
Registry__Principals__0__Roles__0=producer
# add consumer principals (Assay) as __1__, etc. — roles: producer may publish; everyone reads own/granted
```

`trusted-keys.json` is the ACTIVE/retired public-key set publishes are verified against (shape =
`examples/cai-delivery.keys.json`; empty/absent ⇒ the registry rejects every publish). Rotate =
update the file + restart. **Back up `registry-data/`** — it is the system of record for issued
deliveries + grants (nightly `sqlite3 registry.db ".backup ..."` or plain file copy while quiesced).

## Edge (dgx1) — founder-gated

Install [`../nginx/api.cai.canine.dev.conf`](../nginx/api.cai.canine.dev.conf) on the edge
(192.168.1.159), issue the cert (`sudo certbot certonly --webroot -w /var/www/html -d
api.cai.canine.dev`), `nginx -t && systemctl reload nginx`. DNS is already covered by the
`*.canine.dev` wildcard.

## Rate limits (RESOLVED — the limiter is traffic-class aware)

The open API's per-IP budget (1/s · 3/min · 15/day) used to apply to registry traffic too, and it
throttled the delivery loop live: Watchdog + Assay both call from ONE LAN IP (wrx1 behind the dgx1
proxy), and 429s hit `/api/registry/keys` and delivery GETs mid-loop. The limiter now classifies
(`ApiRateLimiting` in Cai.Web):

| class | who | budget |
|---|---|---|
| trusted | loopback, partner key | none |
| principal | VALID registry bearer | **600/min per principal** (fuse, not quota — the credential is the abuse control; partitioned by org/name, never IP) |
| registry-public | anonymous `/api/registry/keys` + `/api/registry/health` | **300/min per IP** (an offline-verify loop over a whole corpus cannot trip it; a flood still does) |
| public | everything else under `/api` | 1/s · 3/min · 15/day per IP (unchanged — an INVALID token stays here, throttling token guessing) |

If a legitimate batch ever approaches the principal fuse, raise
`ApiRateLimiting.PrincipalPermitsPerMinute` — do not put the loop back inside a per-IP budget.

## Verify after the registry merges + deploys

```bash
curl -s https://api.cai.canine.dev/api/registry/health          # 200 "Healthy"; "Degraded" until trusted-keys.json is provisioned (NEVER 500)
curl -s https://api.cai.canine.dev/api/registry/keys            # 200, public key set (empty set until provisioned)
curl -s -o /dev/null -w '%{http_code}' https://api.cai.canine.dev/api/registry/deliveries  # 401 (default-deny challenge)
ls -la /home/jimmy/apps/cai-web/registry-data/                  # registry.db present, survives next deploy
```

The unconfigured state (no `registry.env` principals, no `trusted-keys.json`) is SAFE, not broken: `/health` answers
`Degraded`, `/keys` serves an empty set, everything else is `401`, and every publish is rejected — see the spec's
safe-by-default contract (§2).

## Rollback

- Code: the existing deploy rollback (health-check failure restores `app.prev` + bounces).
- Config: `sudo rm -r /etc/systemd/system/cai-web.service.d && sudo systemctl daemon-reload` (+
  restart at the next convenient moment); `rm ~/.config/cai/registry.env`.
- Data: restore `registry-data/` from backup. Deleting it wipes issued deliveries/grants — don't.
