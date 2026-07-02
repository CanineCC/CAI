namespace Cai.Web.Registry;

/// <summary>
/// Registry configuration (bound from the <c>Registry</c> section). The registry is the first identity-gated surface
/// on cai (ADR-0008 extension): everything under <c>/api/registry</c> except <c>/keys</c> requires an authenticated
/// principal, and access to a stored delivery is governed by ownership + grants (ADR-0010).
/// </summary>
public sealed class RegistryOptions
{
    /// <summary>The configuration section name.</summary>
    public const string Section = "Registry";

    /// <summary>Path of the SQLite database file the registry stores deliveries + grants in. Relative paths resolve
    /// against the content root. PRODUCTION NOTE: the blue-green deploy keeps two app dirs — point this OUTSIDE the
    /// app dir (e.g. <c>/var/lib/cai/registry.db</c>) so both slots share one store.</summary>
    public string DbPath { get; set; } = Path.Combine("data", "registry.db");

    /// <summary>Path of the trusted public key set (a <c>DeliveryPublicKeySet</c> JSON file, same shape as
    /// <c>examples/cai-delivery.keys.json</c>). A delivery is accepted at publish only when its signature verifies
    /// against an ACTIVE key in this set; retired keys keep already-stored deliveries verifiable but cannot publish
    /// new ones. Loaded at startup (rotate = update file + restart). Empty/absent ⇒ the registry accepts nothing.</summary>
    public string? KeysPath { get; set; }

    /// <summary>The principals that may call the registry (closed-loop v1 credential store; see
    /// <see cref="RegistryTokenAuthenticationHandler"/> for the Keycloak seam).</summary>
    public List<RegistryPrincipalOptions> Principals { get; set; } = [];
}

/// <summary>
/// One registry principal — closed-loop v1 authentication is a configured bearer token per calling service/org.
/// The claim contract this produces is exactly what the future Keycloak scheme must map to: <c>name</c>,
/// <c>cai:org</c> (the org the caller acts as) and one role claim per role.
/// </summary>
public sealed class RegistryPrincipalOptions
{
    /// <summary>The opaque bearer token the caller presents (<c>Authorization: Bearer …</c>). SECRET.</summary>
    public string Token { get; set; } = "";

    /// <summary>The org this principal acts as — the identity ownership and grants are evaluated against.</summary>
    public string OrgId { get; set; } = "";

    /// <summary>A human-readable principal name (e.g. <c>watchdog.canine.dev</c>), for logs and provenance.</summary>
    public string Name { get; set; } = "";

    /// <summary>Roles: <c>producer</c> may publish deliveries; every authenticated principal may read what it owns
    /// or is granted, and manage grants for its own org.</summary>
    public List<string> Roles { get; set; } = [];
}
