using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Cai.Web.Registry;

/// <summary>The registry's claim vocabulary — the contract ANY authentication scheme must produce. The v1 bearer-token
/// handler emits these; the future Keycloak (OIDC JWT) scheme maps its token claims to the same three, and nothing
/// downstream changes. That mapping is the whole auth seam.</summary>
public static class RegistryClaims
{
    /// <summary>The org the caller acts as — ownership and grant checks run against this value.</summary>
    public const string Org = "cai:org";

    /// <summary>The role value that permits publishing deliveries.</summary>
    public const string ProducerRole = "producer";

    /// <summary>The authorization policy name for publish (requires the <see cref="ProducerRole"/> role).</summary>
    public const string ProducerPolicy = "registry:producer";

    /// <summary>The org of an authenticated principal, or null when the request is anonymous/unmapped.</summary>
    public static string? OrgOf(ClaimsPrincipal user) => user.FindFirstValue(Org);
}

/// <summary>
/// Closed-loop v1 registry authentication: a configured opaque bearer token per principal
/// (<c>Registry:Principals</c>), compared in fixed time. This is deliberately the SMALLEST real implementation of the
/// auth seam — the endpoints and the access model only ever see a <see cref="ClaimsPrincipal"/> carrying the
/// <see cref="RegistryClaims"/> contract, so swapping this scheme for Keycloak JWT-bearer validation later is a
/// composition-root change (register the JWT handler, map <c>sub</c>/org/roles to the same claims), not an API change.
/// </summary>
public sealed class RegistryTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    IOptions<RegistryOptions> registry,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme name (hides the base class's per-instance scheme accessor on purpose — this IS that name).</summary>
    public new const string Scheme = "RegistryBearer";

    /// <summary>The header the Kennel service principal uses on a GET read to assert which customer org the read
    /// is for — the registry then scopes the request to that org (Kennel reading on a buyer's behalf).</summary>
    public const string OnBehalfHeader = "X-Cai-On-Behalf-Org";

    /// <summary>A well-formed org id: <c>org_</c> then lowercase alphanumerics/underscore/hyphen, ordinal, length-capped.
    /// A malformed on-behalf assertion must FAIL CLOSED — never silently fall back to the broad service org.</summary>
    private static readonly Regex OrgIdPattern = new(@"^org_[a-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant);

    private readonly RegistryOptions _registry = registry.Value;

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Resolve(Request, _registry) is not { } principal)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Effective org. A request acts as the caller's own configured org, UNLESS it names a customer org via
        // the on-behalf header (Kennel acting for a buyer/seller) — then it runs as that org for ALL of its
        // authority: reads scope to it AND a grant it writes is owned by it, so the give-access share loop is
        // consistent (a grant only covers deliveries of the same owner org). Malformed/absent → own org.
        // Closed-loop: every principal is a trusted Canine service, so any may assert it; gate this
        // per-principal the day an untrusted third-party producer exists (spec defers their conformance).
        var effectiveOrg = principal.OrgId;
        var onBehalf = Request.Headers[OnBehalfHeader].ToString();
        if (!string.IsNullOrEmpty(onBehalf) && OrgIdPattern.IsMatch(onBehalf))
        {
            effectiveOrg = onBehalf;
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, principal.Name),
            new Claim(RegistryClaims.Org, effectiveOrg),
            .. principal.Roles.Select(r => new Claim(ClaimTypes.Role, r)),
        ], Scheme);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }

    /// <inheritdoc />
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        await Response.WriteAsJsonAsync(new { error = "authentication required — present a registry bearer token" }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(new { error = "this credential is not authorized for that operation" }).ConfigureAwait(false);
    }

    /// <summary>Resolve a request's bearer token to a configured principal, or null. Fixed-time comparison so the
    /// lookup cannot be timed; also used by the rate-limiter exemption (an AUTHENTICATED registry call is not subject
    /// to the anonymous open-API budget — its abuse control is the credential itself).</summary>
    public static RegistryPrincipalOptions? Resolve(HttpRequest request, RegistryOptions registry)
    {
        string auth = request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return null;
        }

        var presented = Encoding.UTF8.GetBytes(auth["Bearer ".Length..].Trim());
        foreach (var p in registry.Principals)
        {
            if (string.IsNullOrEmpty(p.Token))
            {
                continue; // an empty configured token must never match anything
            }

            var expected = Encoding.UTF8.GetBytes(p.Token);
            if (presented.Length == expected.Length && CryptographicOperations.FixedTimeEquals(presented, expected))
            {
                return p;
            }
        }

        return null;
    }
}
