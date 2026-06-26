using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Cai.Scoring;
using Cai.Web;
using Cai.Web.Components;
using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

// Server-side fetch of the surveyor's public aggregate scan stats (LoC scanned + completed scans) for /registry —
// the standard shows the scanned-corpus SCALE; the survey records themselves stay on the surveyor (data ownership).
// Base URL is configurable (Watchdog:BaseUrl), defaulting to production; the page degrades gracefully if unreachable.
// The named "watchdog" client carries a standard resilience pipeline (timeout + retry-with-backoff + circuit breaker)
// so a slow or failing surveyor can never stall the /registry render — timeouts are tightened for an SSR best-effort
// call (the page renders without the stats rather than hanging on a dependency).
builder.Services.AddHttpClient("watchdog").AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
    o.Retry.MaxRetryAttempts = 2;
});

// cai.canine.dev OWNS the rubric catalogs (the versioned, archived standard). Resolve their root from config, else the
// repo's /rubrics dir relative to the app — so it runs from a clone with no extra setup.
var rubricsRoot = builder.Configuration["Rubrics:Root"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "rubrics"));
builder.Services.AddSingleton(new RubricCatalogStore(rubricsRoot));

// ── Observability (P2): ILogger is on by default; add OpenTelemetry tracing + metrics and a readiness health check so
// the app is diagnosable in production. The OTLP exporter only activates when OTEL_EXPORTER_OTLP_ENDPOINT is set, so an
// environment with no collector gets no failed-export noise.
builder.Services.AddHealthChecks().AddCheck<RubricsHealthCheck>("rubrics");

var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("cai-web"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation());
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    otel.UseOtlpExporter();
}

// Inbound validation (S1): the public /score + /verify endpoints take an evidence bundle off the wire — validate its
// shape (rubric version present, scores/confidence/coverage in range) before it reaches the deterministic scorer.
builder.Services.AddScoped<IValidator<EvidenceBundle>, EvidenceBundleValidator>();

// Secure-cookie hygiene (S1): the only cookie the app sets is the antiforgery token — mark it Secure/HttpOnly/SameSite.
// Behind the TLS-terminating dgx1 proxy, UseForwardedHeaders (below) makes the request read as HTTPS so it actually flows.
builder.Services.AddAntiforgery(o =>
{
    // SameAsRequest, not Always: behind the TLS-terminating proxy real traffic reads as HTTPS (UseForwardedHeaders) so
    // the Secure flag is set for browsers, but a plain-HTTP request (loopback health check, a misrouted internal call)
    // won't throw "not an SSL request" while rendering an antiforgery-protected form.
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
});

// Access control (C2): secure-by-default. The fallback authorization policy DENIES any endpoint that does not
// explicitly opt out — so a future endpoint is protected unless deliberately made public. Every endpoint on this open,
// read-only standard site is then explicitly .AllowAnonymous(): the public API *is* the product, gated by rate limiting
// (not identity). The value is that the default flips from open to closed, so a new protected surface can't be added by
// omission.
builder.Services.AddAuthentication();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Behind the dgx1 nginx reverse proxy: trust X-Forwarded-* so the rate limiter partitions by the REAL client IP.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear(); // the proxy is a different host; accept the forwarded chain (we only use it for limiting, never auth)
});

// API rate limiting (public read API): 1/second AND 3/minute AND 15/day, per client IP, chained so a request must pass
// all three. EXEMPT: loopback callers (the co-located Watchdog surveyor calls cai locally) and an optional partner key.
// Only /api is limited — the UI and the client-side WASM calculator never hit it.
var partnerKey = builder.Configuration["RateLimit:PartnerKey"];
bool Exempt(HttpContext ctx) =>
    (ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip))
    || (!string.IsNullOrEmpty(partnerKey) && ctx.Request.Headers["X-CAI-Partner"] == partnerKey);

RateLimitPartition<string> Window(HttpContext ctx, string tag, int permit, TimeSpan window)
{
    if (!ctx.Request.Path.StartsWithSegments("/api") || Exempt(ctx))
    {
        return RateLimitPartition.GetNoLimiter("exempt");
    }

    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter($"{tag}:{ip}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = window, QueueLimit = 0 });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => Window(ctx, "s", 1, TimeSpan.FromSeconds(1))),
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => Window(ctx, "m", 3, TimeSpan.FromMinutes(1))),
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => Window(ctx, "d", 15, TimeSpan.FromDays(1))));
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "1";
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
        {
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = "rate limit exceeded — cache the rubric you use; see https://cai.canine.dev/api-reference" }, ct).ConfigureAwait(false);
    };
});

var app = builder.Build();

app.Logger.LogInformation("CAI web starting — rubrics root {RubricsRoot}", rubricsRoot);

app.UseForwardedHeaders();

// Security response headers (S1): defense in depth on every response, even though the dgx1 nginx proxy could also set
// them. The CSP is permissive enough for the static-SSR pages + the in-browser calculator (inline styles/scripts, WASM)
// while still locking down framing, base-uri and object/embed.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Content-Security-Policy"] =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'self'; " +
        "img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'; connect-src 'self'";
    await next();
});

// Map the API access guard's throw-on-violation to 403 (C2). The guard itself is called explicitly by each API handler.
app.Use(async (context, next) =>
{
    try
    {
        await next().ConfigureAwait(false);
    }
    catch (ApiAccess.ForbiddenException ex)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message }).ConfigureAwait(false);
    }
});

app.UseRateLimiter();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

// Readiness probe (P2): /health is 200 only when the rubric catalog store has versions to serve. Exempt from the API
// rate limiter (it is not under /api). The deploy workflow polls this before swapping the live app.
app.MapHealthChecks("/health").AllowAnonymous();

// ── The standard API (what the Watchdog surveyor and anyone else calls) ───────────────────────────────────────────
// The whole API is intentionally public (a read-only, rate-limited standard). Each endpoint opts out of the default-deny
// fallback policy with an explicit [AllowAnonymous] attribute (C2) — authorization is default-closed; this is the
// deliberate, documented public surface.
var api = app.MapGroup("/api");

// The published rubric versions, newest first.
api.MapGet("/rubrics", [AllowAnonymous] (HttpContext http, RubricCatalogStore store) =>
{
    ApiAccess.EnsureAllowed(http);
    return Results.Ok(new { latest = store.Latest(), versions = store.Versions() });
});

// A version's full catalog — the 124 dimensions × 10 lenses it defines. "latest" resolves to the newest.
api.MapGet("/rubrics/{version}/catalog", [AllowAnonymous] (string version, HttpContext http, RubricCatalogStore store) =>
{
    ApiAccess.EnsureAllowed(http);
    var resolved = version == "latest" ? store.Latest() : version;
    if (resolved is null)
    {
        return Results.NotFound(new { error = "no rubric versions published" });
    }

    var catalog = store.Get(resolved);
    return catalog is null
        ? Results.NotFound(new { error = $"unknown rubric version '{version}'", published = store.Versions() })
        : Results.Text(catalog.ToJson(), "application/json");
});

// Score an evidence bundle — the open, reproducible fold. POST the bundle JSON; get the CAI + per-lens contributions.
api.MapPost("/score", [AllowAnonymous] async (HttpRequest req, HttpContext http, IValidator<EvidenceBundle> validator, ILogger<Program> log) =>
{
    ApiAccess.EnsureAllowed(http);
    try
    {
        using var reader = new StreamReader(req.Body);
        var bundle = EvidenceBundle.Parse(await reader.ReadToEndAsync().ConfigureAwait(false));
        var validation = await validator.ValidateAsync(bundle).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            log.LogWarning("Rejected /score bundle ({Count} error(s))", validation.Errors.Count);
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var s = CaiScorer.Score(bundle);
        return Results.Ok(new
        {
            cai = Math.Round(s.Headline, 2),
            band = s.Band.Label(),
            rubricVersion = s.RubricVersion,
            aggregate = Math.Round(s.Aggregate, 2),
            categoryMean = s.CategoryMean is { } m ? Math.Round(m, 2) : (double?)null,
            coherenceNote = s.CoherenceNote,
            lenses = s.Lenses.Select(l => new
            {
                l.Lens,
                score = Math.Round(l.Score, 2),
                band = l.Band.Label(),
                criticalGated = l.CriticalGated,
                weight = Math.Round(l.Weight, 4),
                contribution = Math.Round(l.Contribution, 2),
            }),
            categories = s.Categories.Select(c => new
            {
                c.Category,
                c.Lens,
                score = c.Score is { } cs ? Math.Round(cs, 2) : (double?)null,
                c.DimensionCount,
            }),
        });
    }
    catch (Exception e)
    {
        log.LogWarning(e, "Malformed /score payload");
        return Results.BadRequest(new { error = e.Message });
    }
});

// Verify a published headline reproduces from its evidence.
api.MapPost("/verify", [AllowAnonymous] async (HttpRequest req, HttpContext http, IValidator<EvidenceBundle> validator, ILogger<Program> log) =>
{
    ApiAccess.EnsureAllowed(http);
    try
    {
        using var reader = new StreamReader(req.Body);
        var bundle = EvidenceBundle.Parse(await reader.ReadToEndAsync().ConfigureAwait(false));
        var validation = await validator.ValidateAsync(bundle).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            log.LogWarning("Rejected /verify bundle ({Count} error(s))", validation.Errors.Count);
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var v = CaiScorer.Verify(bundle);
        return Results.Ok(new { reproduced = v.Reproduced, computed = Math.Round(v.Computed, 2), claimed = v.Claimed, delta = Math.Round(v.Delta, 2), tolerance = v.Tolerance });
    }
    catch (Exception e)
    {
        log.LogWarning(e, "Malformed /verify payload");
        return Results.BadRequest(new { error = e.Message });
    }
});

// ── /llms.txt — teach the CAI term + the standard's structure to LLMs/agents (the "referenceable" pillar) ──────────
app.MapGet("/llms.txt", [AllowAnonymous] (RubricCatalogStore store) =>
{
    var latest = store.Latest();
    var catalog = latest is null ? null : store.Get(latest);
    var dimCount = catalog?.Dimensions.Count ?? 124;
    var lensCount = LensCatalog.All.Count;
    var coreCount = LensCatalog.All.Count(l => l.Core);
    var lensLines = string.Join("\n", LensCatalog.All.Select(l =>
        $"- {l.DisplayName} ({l.Key}) — {(l.Core ? "core, always on" : "model-aware")}"));

    var text =
$@"# CAI — the Codebase Assurance Index

> CAI is an open, reproducible 0–100 standard for the health of a C#/.NET codebase. Same evidence in, same score out — a measurement anyone can verify, not an opinion. The method is open and free; the independent, signed survey (the deductions and what to do about them) is a service from the surveyor, watchdog.canine.dev. Stewarded by Watchdog.

The score is deterministic: identical evidence under the same rubric version always folds to the same number. The weights ship in the evidence, so anyone can reproduce — or falsify — a published CAI with no access to the analyzer. That is the difference from an LLM (a different answer each run) and from a secret proprietary score (uncheckable).

## How it is computed
- {dimCount} dimensions, each scored 0-10 from evidence, grouped into {lensCount} lenses ({coreCount} core, always on; the rest model-aware — they light up only when the architecture calls for them).
- Dimensions fold into their lens, lenses fold into the headline — both by a rank-weighted ordered weighted average (Yager OWA), worst-first, so the weakest areas drag hardest. Never an equal-weight mean.
- Bands: Exemplary 90-100, Strong 70-89, Adequate 50-69, Weak 25-49, Critical 0-24.
- Frozen, versioned rubric (latest: {latest ?? "unpublished"}). Any change that can move a score for unchanged evidence mints a new version; old versions are retained, so a score is always reproducible to the exact criteria.
- The firewall: the deterministic measurement (score, findings, algorithm) is the open standard; the advisory deductions and non-score enhancements are the surveyor's paid judgment. That boundary is the free/paid line.

## The {lensCount} lenses
{lensLines}

## Licensing
- Reference scorer (C#, evidence to CAI): Apache-2.0 at github.com/CanineCC/CAI.
- Spec: versioned, CC-BY. Free to copy, protected to call it CAI — only spec-reproducible results may carry the CAI mark.

## Pages
- The standard and definition: https://cai.canine.dev/
- The open algorithm, versioned: https://cai.canine.dev/spec
- The {lensCount} lenses and the dimensions under each: https://cai.canine.dev/lenses
- The {dimCount}-dimension catalog, by lens and rubric version: https://cai.canine.dev/dimensions
- Compute it yourself (the reference CLI): https://cai.canine.dev/cli
- Score your evidence in the browser: https://cai.canine.dev/calculator
- Verify a published number reproduces: https://cai.canine.dev/verify
- The public registry of signed surveys: https://cai.canine.dev/registry
- The badge and mark-usage policy: https://cai.canine.dev/badge
- The JSON API (rubric and scoring): https://cai.canine.dev/api-reference
- The vocabulary as schema.org DefinedTermSet (JSON-LD): https://cai.canine.dev/glossary.jsonld

## Get an independent survey
The standard is free to use. An independent, signed CAI survey — with the deductions and what to do about them — is a service from the surveyor: https://watchdog.canine.dev
";
    return Results.Text(text, "text/plain; charset=utf-8");
});

// The CAI vocabulary as a schema.org DefinedTermSet (JSON-LD) — the citable, machine-readable definition (referenceable pillar).
app.MapGet("/glossary.jsonld", [AllowAnonymous] () => Results.Text(Cai.Web.CaiGlossary.Build(), "application/ld+json; charset=utf-8"));

// Browsers auto-request /favicon.ico; we only ship favicon.svg. Redirect so non-HTML responses (e.g. /llms.txt) don't 404.
app.MapGet("/favicon.ico", [AllowAnonymous] () => Results.Redirect("/favicon.svg", permanent: true));

// The standard's UI (static-SSR Blazor). Public — opt out of the default-deny fallback policy (C2).
app.MapRazorComponents<App>().AllowAnonymous();

app.Run();
