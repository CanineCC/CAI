# Deploying codeassuranceindex.info

cai is served the same way as watchdog.canine.dev: a published .NET app runs as a systemd service on **canine-wrx1**,
and **canine-dgx1** runs nginx that terminates SSL and reverse-proxies to it.

```
client ──https──▶ dgx1 nginx (SSL, codeassuranceindex.info) ──http──▶ wrx1 192.168.1.10:8090 (cai-web.service)
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

Four vhosts, all copies of what is installed and live:

| vhost file | `/etc/nginx/sites-available/` | serves |
| --- | --- | --- |
| [`nginx/codeassuranceindex.info.conf`](nginx/codeassuranceindex.info.conf) | `codeassuranceindex.info` | the apex + `www` — a **split** host (see below) |
| [`nginx/api.codeassuranceindex.info.conf`](nginx/api.codeassuranceindex.info.conf) | `api.codeassuranceindex.info` | the API + registry, all of Cai.Web |
| [`nginx/app.codeassuranceindex.info.conf`](nginx/app.codeassuranceindex.info.conf) | `app.codeassuranceindex.info` | the interactive tools, all of Cai.Web |
| *(imprint-owned)* | `cai` | the retired `cai.canine.dev`, 301-only |

Port 80 serves the ACME challenge + redirects to 443; port 443 terminates SSL and `proxy_pass`es to
`http://192.168.1.10:8090` with `X-Forwarded-For`, so the rate limiter sees the real client IP.

- **Certs:** Let's Encrypt via webroot, auto-renewing —
  `certbot certonly --webroot -w /var/www/html -d <host>`.
- **DNS** is at **one.com** (`ns01/ns02.one.com`). The apex `codeassuranceindex.info` is an A record to
  `5.103.135.44` (dgx1), and a wildcard covers `api.` / `app.`, so subdomains need no DNS work. `www` is the
  one exception: it carries its own A record and must point at the apex IP too, or it never reaches dgx1.

## CI/CD
[`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml) — on push to `main`, a **self-hosted runner on wrx1**
verifies the build + scorer tests, publishes to `/home/jimmy/apps/cai-web/app` (keeping `app.prev`), bounces the
service, health-checks `127.0.0.1:8090/api/rubrics`, and rolls back on failure. **Requires a self-hosted Actions runner
registered for `CanineCC/CAI` on wrx1** (register with a repo runner token, same as the watchdog/unfold runners).

## The domains

The standard moved off `cai.canine.dev` onto its own name. Four hostnames, one `cai-web.service`:

| Hostname | Backed by | What lives there |
| --- | --- | --- |
| `codeassuranceindex.info` | imprint CMS (wrx1 :8153), + Cai.Web for three paths | The standard, as a site: the spec, the dimensions, the rubric versions, the survey corpus. **The canonical origin** — every canonical link, `og:url`, hreflang and sitemap entry names it. |
| `api.codeassuranceindex.info` | Cai.Web (wrx1 :8090) | The open JSON API and the signed registry. What machines call. |
| `app.codeassuranceindex.info` | Cai.Web (wrx1 :8090) | The interactive tools — the calculator, the verifier. |
| `www.codeassuranceindex.info` | — | 301 to the apex. |

### Why the apex is split

The API is reachable **two ways on purpose**:

- `https://api.codeassuranceindex.info/api/…` — the name programmatic callers should use. Stable, roomy
  request body (16m), independent of whatever serves the marketing pages.
- `https://codeassuranceindex.info/api/…` — the *same* endpoints, proxied through the apex so the marketing
  widgets (`cai-verifier`, `cai-calculator`, `cai-dimensions`) call them **same-origin**. This is not a
  convenience: it is what lets those widgets work without a CORS grant, and it means a reader's browser never
  makes a cross-origin request to run the standard's own proof tools.

The apex also proxies two machine identifiers to Cai.Web, so that the URLs the standard *asserts about itself*
actually resolve on the domain that owns them:

- `/glossary.jsonld` — the vocabulary as a schema.org `DefinedTermSet`.
- `/schemas/cai-delivery-1.0.schema.json` — the `$schema` a signed delivery package names.

Everything else on the apex is imprint-published static content.

### What the old hostnames do now

`cai.canine.dev`, `api.cai.canine.dev` and `app.cai.canine.dev` are **kept forever as redirects**, never
deleted. Roughly 2,400 published survey pages, plus every signed package issued before the move, carry absolute
`cai.canine.dev` links; a removed hostname would break all of them retroactively. The old apex keeps proxying
`/api/` rather than redirecting it, because a 301 is not safe for the `POST`s programmatic callers make.

### The signed package's identity

`payload.issuer.name` and the schema `$id` moved with the domain — new packages are issued by
`codeassuranceindex.info`. **Old packages still verify.** Verification checks the Ed25519 signature over the
canonical payload and the `schemaVersion` MAJOR; it has never checked the issuer string or the schema URL, so a
package signed as `cai.canine.dev` verifies now exactly as it did before. `examples/cai-delivery.sample.json` is
deliberately left signed under the old issuer, and the tests that verify it are the regression proof of this.

## api.codeassuranceindex.info (the registry)

The registry ships **inside Cai.Web** — `api.codeassuranceindex.info` is a second hostname on this same
service, with its own edge vhost ([`nginx/api.codeassuranceindex.info.conf`](nginx/api.codeassuranceindex.info.conf))
and a stable server-side store that survives deploys. Setup, secrets, backup and rollback:
[`registry/DEPLOY.md`](registry/DEPLOY.md).

## app.codeassuranceindex.info (the interactive tools)

> ⚠️ **The apex is the marketing site, not the app.** `codeassuranceindex.info` is served by the imprint CMS.
> Only `/api/*`, `/glossary.jsonld` and `/schemas/` reach Cai.Web from there;
> `codeassuranceindex.info/calculator` returns "Page not found" even though the app renders that page.

That left the two pages that make the standard *checkable* — the calculator, and the verifier that
reproduces a headline and validates an Ed25519 delivery signature — deployed but unlinkable at the
hostname every page points at. `app.codeassuranceindex.info` is a third hostname on the same service
([`nginx/app.codeassuranceindex.info.conf`](nginx/app.codeassuranceindex.info.conf)), mirroring the split watchdog
already uses (`watchdog.canine.dev` marketing / `app.watchdog.canine.dev` app).

One-time setup on **canine-dgx1**:

```bash
sudo cp app.codeassuranceindex.info.conf /etc/nginx/sites-available/app.codeassuranceindex.info
sudo ln -s /etc/nginx/sites-available/app.codeassuranceindex.info /etc/nginx/sites-enabled/
sudo certbot certonly --webroot -w /var/www/html -d app.codeassuranceindex.info   # wildcard DNS already resolves
sudo nginx -t && sudo systemctl reload nginx
curl -sI https://app.codeassuranceindex.info/verify | head -1                      # expect 200
```

Then point the imprint marketing pages at the tools instead of describing them:
`app.codeassuranceindex.info/verify` and `app.codeassuranceindex.info/calculator`. Until that link exists, a reader who
is told "reproduce the number yourself" has no button to press.
