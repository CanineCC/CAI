using Cai.Delivery;
using Microsoft.Extensions.Options;

namespace Cai.Web.Registry;

/// <summary>
/// The registry's trusted signing key set — the ONLY keys a published delivery may be signed under. Loaded once at
/// startup from <see cref="RegistryOptions.KeysPath"/> (a <see cref="DeliveryPublicKeySet"/> JSON file; rotation =
/// update the file, restart the app — the same operational shape as the rubric catalogs). The full set (active AND
/// retired) is served publicly at <c>GET /api/registry/keys</c> so consumers can verify offline; only ACTIVE keys can
/// publish NEW deliveries — a retired key keeps old artifacts verifiable but mints nothing (spec §7's frozen-forever
/// discipline, applied at the ingest gate).
/// </summary>
public sealed class TrustedKeyProvider
{
    /// <summary>Load the key set once at startup. A missing path or file yields an EMPTY set — the registry then
    /// rejects every publish, which is the safe failure mode (never "accept anything").</summary>
    public TrustedKeyProvider(IOptions<RegistryOptions> options, IHostEnvironment env, ILogger<TrustedKeyProvider> logger)
    {
        var path = options.Value.KeysPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogWarning("Registry:KeysPath is not configured — the registry will reject every publish");
            Keys = new DeliveryPublicKeySet();
            return;
        }

        var resolved = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(env.ContentRootPath, path));
        if (!File.Exists(resolved))
        {
            logger.LogWarning("Registry trusted key file {Path} does not exist — the registry will reject every publish", resolved);
            Keys = new DeliveryPublicKeySet();
            return;
        }

        Keys = DeliveryPublicKeySet.Parse(File.ReadAllText(resolved));
        logger.LogInformation("Registry trusted keys loaded from {Path}: {Active} active, {Retired} retired",
            resolved,
            Keys.Keys.Count(k => k.Status == "active"),
            Keys.Keys.Count(k => k.Status != "active"));
    }

    /// <summary>The published key set (active + retired) — what <c>GET /api/registry/keys</c> serves.</summary>
    public DeliveryPublicKeySet Keys { get; }
}
