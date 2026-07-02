using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cai.Web.Registry;

/// <summary>/health contributes the registry store's reachability — the deploy's verify-before-swap (ADR-0005) then
/// refuses to go live on a slot whose registry database is broken or unwritable-by-misconfiguration.</summary>
public sealed class RegistryHealthCheck(IRegistryStore store) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.IsHealthy()
            ? HealthCheckResult.Healthy("registry store reachable")
            : HealthCheckResult.Unhealthy("registry store unreachable"));
}
