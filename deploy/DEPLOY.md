# Deploying cai.canine.dev

cai is served the same way as watchdog.canine.dev: a published .NET app runs as a systemd service on **canine-wrx1**,
and **canine-dgx1** runs nginx that terminates SSL and reverse-proxies to it.

```
client ──https──▶ dgx1 nginx (SSL, cai.canine.dev) ──http──▶ wrx1 192.168.1.10:8090 (cai-web.service)
                                                                         ▲
watchdog (co-located on wrx1) ──http──▶ 127.0.0.1:8090 ──────────────────┘  (loopback ⇒ rate-limit-exempt)
```

## Host: canine-wrx1 (the app)
- **Service:** `cai-web.service` (see [`cai-web.service`](cai-web.service)) — runs `dotnet Cai.Web.dll` from
  `/home/jimmy/apps/cai-web/app`, binds `0.0.0.0:8090`, reads the rubric catalogs from
  `/home/jimmy/apps/cai-web/rubrics`. `Restart=on-failure`.
- **Firewall:** `ufw allow from 192.168.1.0/24 to any port 8090 proto tcp` (lets dgx1's nginx reach it; the LAN only).
- The public API rate-limits anonymous traffic per client IP (1/s · 3/min · 15/day); loopback callers are exempt, so
  watchdog calls `http://127.0.0.1:8090` and is never limited. Registry traffic has its own classes — authenticated
  principals ride a per-principal budget and the anonymous `keys`/`health` probes a generous per-IP one (see
  [`registry/DEPLOY.md`](registry/DEPLOY.md), "Rate limits").

## Host: canine-dgx1 (nginx + SSL)
- **vhost:** [`nginx/cai.canine.dev.conf`](nginx/cai.canine.dev.conf) at `/etc/nginx/sites-available/cai` (symlinked
  into `sites-enabled`). Port 80 serves the ACME challenge + redirects to 443; port 443 terminates SSL and
  `proxy_pass`es to `http://192.168.1.10:8090` with `X-Forwarded-For` (so the rate limiter sees the real client IP).
- **Cert:** Let's Encrypt — `certbot certonly --webroot -w /var/www/html -d cai.canine.dev` (auto-renews).
- DNS: `cai.canine.dev` → 5.103.135.44 (already pointed here, same as watchdog).

## CI/CD
[`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml) — on push to `main`, a **self-hosted runner on wrx1**
verifies the build + scorer tests, publishes to `/home/jimmy/apps/cai-web/app` (keeping `app.prev`), bounces the
service, health-checks `127.0.0.1:8090/api/rubrics`, and rolls back on failure. **Requires a self-hosted Actions runner
registered for `CanineCC/CAI` on wrx1** (register with a repo runner token, same as the watchdog/unfold runners).

## api.cai.canine.dev (the registry)

The registry ships **inside Cai.Web** — `api.cai.canine.dev` is a second hostname on this same
service, with its own edge vhost ([`nginx/api.cai.canine.dev.conf`](nginx/api.cai.canine.dev.conf))
and a stable server-side store that survives deploys. Setup, secrets, backup and rollback:
[`registry/DEPLOY.md`](registry/DEPLOY.md).

## app.cai.canine.dev (the interactive tools)

> ⚠️ **The apex is the marketing site, not the app.** `cai.canine.dev` is served by the imprint CMS;
> only `/api/*` reaches Cai.Web from there. `cai.canine.dev/calculator` returns "Page not found" even
> though the app renders that page. The apex vhost in
> [`nginx/cai.canine.dev.conf`](nginx/cai.canine.dev.conf) is **stale** and does not describe this.

That left the two pages that make the standard *checkable* — the calculator, and the verifier that
reproduces a headline and validates an Ed25519 delivery signature — deployed but unlinkable at the
hostname every page points at. `app.cai.canine.dev` is a third hostname on the same service
([`nginx/app.cai.canine.dev.conf`](nginx/app.cai.canine.dev.conf)), mirroring the split watchdog
already uses (`watchdog.canine.dev` marketing / `app.watchdog.canine.dev` app).

One-time setup on **canine-dgx1**:

```bash
sudo cp app.cai.canine.dev.conf /etc/nginx/sites-available/app.cai.canine.dev
sudo ln -s /etc/nginx/sites-available/app.cai.canine.dev /etc/nginx/sites-enabled/
sudo certbot certonly --webroot -w /var/www/html -d app.cai.canine.dev   # wildcard DNS already resolves
sudo nginx -t && sudo systemctl reload nginx
curl -sI https://app.cai.canine.dev/verify | head -1                      # expect 200
```

Then point the imprint marketing pages at the tools instead of describing them:
`app.cai.canine.dev/verify` and `app.cai.canine.dev/calculator`. Until that link exists, a reader who
is told "reproduce the number yourself" has no button to press.
