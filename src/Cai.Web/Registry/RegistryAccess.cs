using System.Globalization;

namespace Cai.Web.Registry;

/// <summary>
/// The registry's authority axis (registry spec §5, kennel ADR-0018): can this principal READ that signed delivery?
/// Ownership always can; otherwise only an ACTIVE grant from the delivery's owner to the caller's org, whose scope
/// covers the delivery, confers read. Pending (email-invite) and revoked grants confer nothing; expiry is evaluated at
/// read time. Pure functions — the store fetches, this decides.
/// </summary>
public static class RegistryAccess
{
    /// <summary>Grant scope: the refs are delivery ids.</summary>
    public const string ScopeDelivery = "delivery";

    /// <summary>Grant scope: the refs are repository names — covers the grantor's current AND future deliveries of
    /// those repositories while the grant is active.</summary>
    public const string ScopeRepo = "repo";

    /// <summary>True when <paramref name="orgId"/> may read <paramref name="delivery"/>, given the grants where that
    /// org is the grantee.</summary>
    public static bool CanRead(string orgId, DeliveryRecord delivery, IEnumerable<GrantRecord> grantsToOrg, DateTimeOffset now)
    {
        if (string.Equals(orgId, delivery.OwnerOrgId, StringComparison.Ordinal))
        {
            return true;
        }

        return grantsToOrg.Any(g => IsActive(g, now) && Covers(g, delivery));
    }

    /// <summary>True when the grant is currently in force: active status, granted to an org (not a pending email
    /// invite), and not past its expiry.</summary>
    public static bool IsActive(GrantRecord grant, DateTimeOffset now)
    {
        if (grant.Status != "active" || grant.GranteeOrgId is null)
        {
            return false;
        }

        if (grant.ExpiresAt is { } expires
            && DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var at)
            && at <= now)
        {
            return false;
        }

        return true;
    }

    /// <summary>True when the grant's scope covers the delivery. A grant only ever covers deliveries OWNED BY ITS
    /// GRANTOR — a seller cannot grant access to someone else's evidence.</summary>
    public static bool Covers(GrantRecord grant, DeliveryRecord delivery)
    {
        if (!string.Equals(grant.OwnerOrgId, delivery.OwnerOrgId, StringComparison.Ordinal))
        {
            return false;
        }

        return grant.Scope switch
        {
            ScopeDelivery => grant.ScopeRefs.Contains(delivery.DeliveryId, StringComparer.Ordinal),
            ScopeRepo => grant.ScopeRefs.Contains(delivery.Repository, StringComparer.Ordinal),
            _ => false,
        };
    }
}
