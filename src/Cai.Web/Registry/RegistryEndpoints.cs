using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cai.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cai.Web.Registry;

/// <summary>
/// The registry API (<c>/api/registry</c>) — the binding middle of the closed evidence loop (ADR-0010, registry spec):
/// the producer (Watchdog) PUBLISHES signed CAI-delivery packages, consumers (Assay) FETCH the ones they own or were
/// granted, and sellers manage GRANTS. The registry's trust job on ingest is verification, not creation: schema-valid,
/// signed under a trusted ACTIVE key, signature verifies over the canonical payload, and the verdict reproduces from
/// the embedded evidence — exactly the offline checks a consumer will re-run (<see cref="DeliveryVerifier"/>). The
/// score is never recomputed INTO the artifact here; the stored package is byte-for-byte what the producer published.
/// Deliveries are immutable: the same id can only ever hold the same artifact.
/// </summary>
public static class RegistryEndpoints
{
    /// <summary>Log-category marker for the registry endpoints (they are static, so they cannot be one themselves).</summary>
    internal sealed class Log;

    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    /// <summary>Not-found and not-authorized reads share ONE response so an ungranted caller cannot probe which
    /// delivery ids exist (the visibility axis of ADR-0018 — discovery is opt-in, never a side channel).</summary>
    private static IResult NotFoundOrNotAccessible() =>
        Results.NotFound(new { error = "delivery not found or not accessible" });

    /// <summary>Map the registry endpoints. Everything here requires an authenticated registry principal via the
    /// default-deny fallback policy (ADR-0008) — except <c>/keys</c>, which is deliberately public (public keys are
    /// not secret; consumers need them for offline verification).</summary>
    public static void MapRegistryEndpoints(this IEndpointRouteBuilder app)
    {
        var registry = app.MapGroup("/api/registry");

        // ── Public keys (the one anonymous registry endpoint) ────────────────────────────────────────────────────
        registry.MapGet("/keys", [AllowAnonymous] (TrustedKeyProvider keys) =>
            Results.Text(keys.Keys.ToJson(), "application/json"));

        // ── Producer push ────────────────────────────────────────────────────────────────────────────────────────
        registry.MapPost("/deliveries", PublishAsync).RequireAuthorization(RegistryClaims.ProducerPolicy);

        // ── Consumer pull ────────────────────────────────────────────────────────────────────────────────────────
        registry.MapGet("/deliveries/{id}", GetDelivery);
        registry.MapGet("/deliveries/{id}/metadata", GetDeliveryMetadata);
        registry.MapGet("/deliveries", ListDeliveries);

        // ── Access grants (seller → buyer) ───────────────────────────────────────────────────────────────────────
        registry.MapPost("/grants", CreateGrantAsync);
        registry.MapGet("/grants", ListGrants);
        registry.MapDelete("/grants/{grantId}", RevokeGrant);
    }

    // ════ POST /api/registry/deliveries ══════════════════════════════════════════════════════════════════════════
    // Body: { "ownerOrgId": "...", "package": <signed CAI-delivery package> }
    // 201 stored · 200 identical re-push (idempotent) · 400 malformed/schema-invalid · 409 id already holds a
    // DIFFERENT artifact · 422 trust rejection (unknown/retired key, bad signature, unsupported MAJOR, non-reproducing)
    private static async Task<IResult> PublishAsync(
        HttpContext http, IRegistryStore store, TrustedKeyProvider trusted, ILogger<RegistryEndpoints.Log> log)
    {
        string ownerOrgId;
        string rawPackage;
        try
        {
            using var doc = await JsonDocument.ParseAsync(http.Request.Body).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Results.BadRequest(new { error = "request body must be a JSON object: { ownerOrgId, package }" });
            }

            if (!doc.RootElement.TryGetProperty("ownerOrgId", out var ownerEl)
                || ownerEl.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(ownerEl.GetString()))
            {
                return Results.BadRequest(new { error = "ownerOrgId is required — the seller org this delivery belongs to" });
            }

            if (!doc.RootElement.TryGetProperty("package", out var packageEl) || packageEl.ValueKind != JsonValueKind.Object)
            {
                return Results.BadRequest(new { error = "package is required — the signed CAI-delivery package" });
            }

            // Schema gate: the VERSIONED wire contract, checked before any cryptography.
            var violations = DeliveryPackageSchema.Validate(packageEl);
            if (violations.Count > 0)
            {
                log.LogWarning("Registry publish rejected: schema-invalid ({Count} violation(s))", violations.Count);
                return Results.BadRequest(new
                {
                    error = "package does not validate against the CAI-delivery schema",
                    schema = DeliverySchema.SchemaId,
                    details = violations,
                });
            }

            ownerOrgId = ownerEl.GetString()!;
            rawPackage = packageEl.GetRawText();
        }
        catch (JsonException e)
        {
            return Results.BadRequest(new { error = $"malformed JSON: {e.Message}" });
        }

        var package = DeliveryPackage.Parse(rawPackage);

        // Trust gate: the signing key must be one of the registry's TRUSTED keys and still ACTIVE — a retired key
        // keeps already-stored deliveries verifiable but cannot mint new ones.
        var key = trusted.Keys.Resolve(package.Signature.KeyId);
        if (key is null)
        {
            log.LogWarning("Registry publish rejected: unknown signing key {KeyId}", package.Signature.KeyId);
            return UnprocessableEntity($"signing key '{package.Signature.KeyId}' is not a trusted registry key");
        }

        if (key.Status != "active")
        {
            log.LogWarning("Registry publish rejected: retired signing key {KeyId}", package.Signature.KeyId);
            return UnprocessableEntity($"signing key '{package.Signature.KeyId}' is retired — new deliveries must be signed by an active key");
        }

        // Verification gate: the SAME two-fold offline check a consumer runs — Ed25519 over the canonical payload
        // (authenticity: tampered/unsigned/wrong-key ⇒ reject) AND the verdict reproduces from the embedded evidence
        // (honesty: a signed-but-dishonest number is refused distribution). Cai.Scoring is used to CHECK the number,
        // never to change it — the stored artifact stays byte-for-byte the producer's.
        var verification = DeliveryVerifier.Verify(package, trusted.Keys, reproduce: true);
        if (!verification.SignatureValid)
        {
            log.LogWarning("Registry publish rejected: {Reason}", verification.Reason);
            return UnprocessableEntity($"signature verification failed: {verification.Reason}");
        }

        if (verification.Reproduced is false)
        {
            log.LogWarning("Registry publish rejected: {Reason}", verification.Reason);
            return UnprocessableEntity($"verdict does not reproduce from the embedded evidence: {verification.Reason}");
        }

        var payload = package.Payload;
        var record = new DeliveryRecord(
            DeliveryId: payload.DeliveryId,
            OwnerOrgId: ownerOrgId,
            Repository: payload.Subject.Repository,
            Commit: payload.Subject.Commit,
            Host: payload.Subject.Host,
            Producer: payload.Producer.Name,
            RubricVersion: payload.RubricVersion,
            Cai: payload.Verdict.Cai,
            Band: payload.Verdict.Band,
            IssuedAt: payload.IssuedAt,
            KeyId: package.Signature.KeyId,
            CanonicalSha256: Convert.ToHexStringLower(SHA256.HashData(CanonicalJson.Canonicalize(payload))),
            SignatureValue: package.Signature.Value,
            PackageJson: rawPackage,
            PublishedAt: DateTimeOffset.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture));

        var location = $"/api/registry/deliveries/{Uri.EscapeDataString(record.DeliveryId)}";
        switch (store.InsertDelivery(record))
        {
            case PublishOutcome.Created:
                log.LogInformation("Registry stored delivery {Id} for {Repo} (owner {Owner}, CAI {Cai})",
                    record.DeliveryId, record.Repository, record.OwnerOrgId, record.Cai);
                return Results.Created(location, Metadata(record));

            case PublishOutcome.AlreadyStored:
                // Idempotent re-push of the exact same artifact — return the stored record, no duplicate.
                return Results.Ok(Metadata(store.GetDelivery(record.DeliveryId)!));

            default:
                log.LogWarning("Registry publish rejected: delivery id {Id} already holds a different artifact", record.DeliveryId);
                return Results.Conflict(new
                {
                    error = $"delivery '{record.DeliveryId}' already exists with different content — deliveries are immutable; a new scan mints a NEW delivery id",
                });
        }
    }

    // ════ GET /api/registry/deliveries/{id} ══════════════════════════════════════════════════════════════════════
    // 200 the stored signed package, verbatim · 404 unknown id OR no read authority (indistinguishable by design)
    private static IResult GetDelivery(string id, HttpContext http, IRegistryStore store)
    {
        var (delivery, canRead) = Authorize(id, http, store);
        return delivery is null || !canRead
            ? NotFoundOrNotAccessible()
            : Results.Text(delivery.PackageJson, "application/json");
    }

    // ════ GET /api/registry/deliveries/{id}/metadata ═════════════════════════════════════════════════════════════
    // 200 the light header (no evidence) · 404 as above
    private static IResult GetDeliveryMetadata(string id, HttpContext http, IRegistryStore store)
    {
        var (delivery, canRead) = Authorize(id, http, store);
        return delivery is null || !canRead
            ? NotFoundOrNotAccessible()
            : Results.Ok(Metadata(delivery));
    }

    // ════ GET /api/registry/deliveries?repository=&producer=&ownerOrgId=&limit=&offset= ══════════════════════════
    // 200 { deliveries: [ metadata… ] } — everything the caller OWNS plus everything actively GRANTED to it,
    // newest first. Filters are exact-match; limit defaults 50 (max 200).
    private static IResult ListDeliveries(
        HttpContext http, IRegistryStore store,
        [FromQuery] string? repository, [FromQuery] string? producer, [FromQuery] string? ownerOrgId,
        [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var org = RegistryClaims.OrgOf(http.User);
        if (org is null)
        {
            return NotFoundOrNotAccessible();
        }

        if (limit is < 1 or > 200 || offset < 0)
        {
            return Results.BadRequest(new { error = "limit must be 1..200 and offset >= 0" });
        }

        var now = DateTimeOffset.UtcNow;
        var activeGrants = store.ListGrantsByGrantee(org).Where(g => RegistryAccess.IsActive(g, now)).ToList();

        var reachable = new Dictionary<string, DeliveryRecord>(StringComparer.Ordinal);
        foreach (var d in store.ListOwned(org))
        {
            reachable[d.DeliveryId] = d;
        }

        var grantedIds = activeGrants.Where(g => g.Scope == RegistryAccess.ScopeDelivery).SelectMany(g => g.ScopeRefs).ToHashSet(StringComparer.Ordinal);
        foreach (var d in store.GetDeliveries(grantedIds))
        {
            reachable.TryAdd(d.DeliveryId, d);
        }

        foreach (var repoGrant in activeGrants.Where(g => g.Scope == RegistryAccess.ScopeRepo))
        {
            foreach (var d in store.ListByOwnerAndRepositories(repoGrant.OwnerOrgId, repoGrant.ScopeRefs.ToList()))
            {
                reachable.TryAdd(d.DeliveryId, d);
            }
        }

        var deliveries = reachable.Values
            .Where(d => RegistryAccess.CanRead(org, d, activeGrants, now))
            .Where(d => repository is null || d.Repository == repository)
            .Where(d => producer is null || d.Producer == producer)
            .Where(d => ownerOrgId is null || d.OwnerOrgId == ownerOrgId)
            .OrderByDescending(d => d.IssuedAt, StringComparer.Ordinal)
            .ThenBy(d => d.DeliveryId, StringComparer.Ordinal)
            .Skip(offset)
            .Take(limit)
            .Select(Metadata)
            .ToList();

        return Results.Ok(new { deliveries });
    }

    // ════ POST /api/registry/grants ══════════════════════════════════════════════════════════════════════════════
    // Body: { grantee: { orgId | email }, scope: "delivery"|"repo", scopeRefs: [...], expiresAt?, purpose? }
    // 201 the grant (orgId grants are active immediately; email grants stay pending and confer no access — the
    // invite/claim flow is deferred) · 400 invalid
    private static async Task<IResult> CreateGrantAsync(HttpContext http, IRegistryStore store, ILogger<RegistryEndpoints.Log> log)
    {
        var org = RegistryClaims.OrgOf(http.User);
        if (org is null)
        {
            return Results.BadRequest(new { error = "this credential carries no org identity" });
        }

        GrantRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<GrantRequest>().ConfigureAwait(false);
        }
        catch (JsonException e)
        {
            return Results.BadRequest(new { error = $"malformed JSON: {e.Message}" });
        }

        if (request is null)
        {
            return Results.BadRequest(new { error = "request body is required" });
        }

        var granteeOrg = Normalize(request.Grantee?.OrgId);
        var granteeEmail = Normalize(request.Grantee?.Email);
        if ((granteeOrg is null) == (granteeEmail is null))
        {
            return Results.BadRequest(new { error = "grantee must carry exactly one of orgId or email" });
        }

        if (granteeOrg == org)
        {
            return Results.BadRequest(new { error = "cannot grant access to your own org — you already own these deliveries" });
        }

        if (request.Scope is not (RegistryAccess.ScopeDelivery or RegistryAccess.ScopeRepo))
        {
            return Results.BadRequest(new { error = "scope must be 'delivery' (refs = delivery ids) or 'repo' (refs = repository names)" });
        }

        var refs = (request.ScopeRefs ?? []).Select(Normalize).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (refs.Count == 0)
        {
            return Results.BadRequest(new { error = "scopeRefs must name at least one delivery id or repository" });
        }

        if (request.ExpiresAt is { } expiresAt
            && !DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out _))
        {
            return Results.BadRequest(new { error = "expiresAt must be an RFC 3339 timestamp" });
        }

        if (request.Scope == RegistryAccess.ScopeDelivery)
        {
            // A grant can only ever cover the grantor's own evidence — validate the refs up front so a seller can't
            // create a dangling (or someone-else's) grant. "Unknown or not owned" is deliberately one message.
            foreach (var d in refs.Where(r => store.GetDelivery(r)?.OwnerOrgId != org))
            {
                return Results.BadRequest(new { error = $"scopeRefs delivery '{d}' does not exist or is not owned by your org" });
            }
        }

        var record = new GrantRecord(
            GrantId: $"gr_{Guid.NewGuid():N}",
            OwnerOrgId: org,
            GranteeOrgId: granteeOrg,
            GranteeEmail: granteeEmail,
            Scope: request.Scope,
            ScopeRefs: refs,
            Status: granteeOrg is not null ? "active" : "pending",
            Purpose: Normalize(request.Purpose),
            CreatedAt: DateTimeOffset.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture),
            ExpiresAt: Normalize(request.ExpiresAt),
            RevokedAt: null);

        store.InsertGrant(record);
        log.LogInformation("Registry grant {Id} created: {Owner} -> {Grantee} ({Scope}: {Refs})",
            record.GrantId, org, granteeOrg ?? granteeEmail, record.Scope, string.Join(",", refs));
        return Results.Created($"/api/registry/grants/{record.GrantId}", GrantView(record));
    }

    // ════ GET /api/registry/grants?direction=outgoing|incoming ══════════════════════════════════════════════════
    // 200 { grants: [ … ] } — outgoing = grants your org issued; incoming = grants naming your org as grantee.
    private static IResult ListGrants(HttpContext http, IRegistryStore store, [FromQuery] string? direction)
    {
        var org = RegistryClaims.OrgOf(http.User);
        if (org is null)
        {
            return Results.Ok(new { grants = Array.Empty<object>() });
        }

        return direction switch
        {
            "outgoing" => Results.Ok(new { grants = store.ListGrantsByOwner(org).Select(GrantView).ToList() }),
            "incoming" => Results.Ok(new { grants = store.ListGrantsByGrantee(org).Select(GrantView).ToList() }),
            _ => Results.BadRequest(new { error = "direction is required: outgoing (grants you issued) or incoming (grants issued to you)" }),
        };
    }

    // ════ DELETE /api/registry/grants/{grantId} ══════════════════════════════════════════════════════════════════
    // 204 revoked (idempotent) · 404 unknown or not yours. Revocation stops FUTURE registry reads; a copy the buyer
    // already fetched stays cryptographically valid by design (grants govern distribution, not authenticity — spec §5).
    private static IResult RevokeGrant(string grantId, HttpContext http, IRegistryStore store)
    {
        var org = RegistryClaims.OrgOf(http.User);
        var grant = store.GetGrant(grantId);
        if (grant is null || org is null || grant.OwnerOrgId != org)
        {
            return Results.NotFound(new { error = "grant not found or not accessible" });
        }

        store.RevokeGrant(grantId, DateTimeOffset.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        return Results.NoContent();
    }

    // ── shared shapes ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Load a delivery and decide read authority for the calling org — owner, or covered by an active grant.</summary>
    private static (DeliveryRecord? Delivery, bool CanRead) Authorize(string id, HttpContext http, IRegistryStore store)
    {
        var delivery = store.GetDelivery(id);
        if (delivery is null || RegistryClaims.OrgOf(http.User) is not { } org)
        {
            return (delivery, false);
        }

        if (org == delivery.OwnerOrgId)
        {
            return (delivery, true);
        }

        var grants = store.ListGrantsByGrantee(org);
        return (delivery, RegistryAccess.CanRead(org, delivery, grants, DateTimeOffset.UtcNow));
    }

    /// <summary>The delivery metadata header — the light, no-evidence shape used by publish responses, the metadata
    /// endpoint and list items.</summary>
    private static object Metadata(DeliveryRecord d) => new
    {
        deliveryId = d.DeliveryId,
        ownerOrgId = d.OwnerOrgId,
        subject = new { repository = d.Repository, commit = d.Commit, host = d.Host },
        producer = d.Producer,
        rubricVersion = d.RubricVersion,
        verdict = new { cai = Math.Round(d.Cai, 2), band = d.Band },
        issuedAt = d.IssuedAt,
        publishedAt = d.PublishedAt,
    };

    private static object GrantView(GrantRecord g) => new
    {
        grantId = g.GrantId,
        ownerOrgId = g.OwnerOrgId,
        grantee = new { orgId = g.GranteeOrgId, email = g.GranteeEmail },
        scope = g.Scope,
        scopeRefs = g.ScopeRefs,
        status = g.Status,
        purpose = g.Purpose,
        createdAt = g.CreatedAt,
        expiresAt = g.ExpiresAt,
        revokedAt = g.RevokedAt,
    };

    private static IResult UnprocessableEntity(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The grant-creation request body.</summary>
public sealed record GrantRequest
{
    /// <summary>Who receives read access — exactly one of orgId (active immediately) or email (pending invite).</summary>
    [JsonPropertyName("grantee")] public GrantRequestGrantee? Grantee { get; init; }

    /// <summary><c>delivery</c> (scopeRefs = delivery ids) or <c>repo</c> (scopeRefs = repository names, covering
    /// current and future deliveries of those repositories owned by the grantor).</summary>
    [JsonPropertyName("scope")] public string? Scope { get; init; }

    /// <summary>The delivery ids or repository names the grant covers.</summary>
    [JsonPropertyName("scopeRefs")] public IReadOnlyList<string>? ScopeRefs { get; init; }

    /// <summary>Optional RFC 3339 expiry — evaluated at read time.</summary>
    [JsonPropertyName("expiresAt")] public string? ExpiresAt { get; init; }

    /// <summary>Optional free-text purpose (e.g. "due diligence Q3").</summary>
    [JsonPropertyName("purpose")] public string? Purpose { get; init; }
}

/// <summary>The grantee of a grant request.</summary>
public sealed record GrantRequestGrantee
{
    /// <summary>The buyer org (grant becomes active immediately).</summary>
    [JsonPropertyName("orgId")] public string? OrgId { get; init; }

    /// <summary>An email invite (grant stays pending; confers no access until claimed — claim flow deferred).</summary>
    [JsonPropertyName("email")] public string? Email { get; init; }
}
