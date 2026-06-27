namespace Cai.Web;

/// <summary>
/// Imperative access guard (C2) for the public API. The CAI standard's API is open by default — but an operator can flip
/// it to partner-only by setting <c>Api:RequirePartnerKey=true</c>. After that, every API handler that calls
/// <see cref="EnsureAllowed"/> denies — throws <see cref="ForbiddenException"/> — any request that is neither a loopback
/// caller (the co-located surveyor) nor presenting a valid <c>X-CAI-Partner</c> key.
///
/// This is default-deny-capable access control enforced at the handler, not middleware magic: the authorization decision
/// is an explicit, throw-on-violation call each endpoint makes, so a new endpoint is access-controlled by writing the
/// guard, and the policy (open vs partner-only) is one config switch.
/// </summary>
internal static class ApiAccess
{
    /// <summary>Thrown when a request is denied API access. Mapped to HTTP 403 by the pipeline.</summary>
    public sealed class ForbiddenException(string message) : Exception(message);

    /// <summary>Authorize the current request for the API, or throw. A no-op when the API is open (the default).</summary>
    public static void EnsureAllowed(HttpContext ctx)
    {
        var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
        if (!cfg.GetValue("Api:RequirePartnerKey", false))
        {
            return; // open standard API — the default
        }

        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is not null && System.Net.IPAddress.IsLoopback(ip))
        {
            return; // the co-located surveyor calls locally
        }

        var partnerKey = cfg["RateLimit:PartnerKey"];
        if (!string.IsNullOrEmpty(partnerKey) &&
            string.Equals(ctx.Request.Headers["X-CAI-Partner"], partnerKey, StringComparison.Ordinal))
        {
            return; // valid partner
        }

        throw new ForbiddenException("API access requires a valid partner key.");
    }
}
