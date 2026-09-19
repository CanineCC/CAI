using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cai.Web.Registry;

/// <summary>
/// The registry's health contract, surfaced twice: it contributes to the global <c>/health</c> (the deploy's
/// verify-before-swap gate, ADR-0005) and it IS the public <c>GET /api/registry/health</c> endpoint. Three states:
/// <list type="bullet">
/// <item><b>Unhealthy</b> (503) — the store is unreachable; a deploy must not go live on this slot.</item>
/// <item><b>Degraded</b> (200) — alive but UNCONFIGURED: no ACTIVE trusted signing key, so every publish is rejected
/// (<see cref="TrustedKeyProvider"/>'s safe failure mode). Degraded maps to 200 on purpose — a fresh box must still
/// pass the deploy gate BEFORE credentials/keys are provisioned; safe-by-default, never dead-by-default.</item>
/// <item><b>Healthy</b> (200) — store reachable and at least one active trusted key.</item>
/// </list>
/// </summary>
public sealed class RegistryHealthCheck(IRegistryStore store, TrustedKeyProvider trusted) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            !store.IsHealthy()
                ? HealthCheckResult.Unhealthy("registry store unreachable")
                : trusted.Keys.Keys.Any(k => k.Status == "active")
                    ? HealthCheckResult.Healthy("registry store reachable, trusted keys loaded")
                    : HealthCheckResult.Degraded("registry store reachable, but no ACTIVE trusted signing key is configured — every publish is rejected"));
}
