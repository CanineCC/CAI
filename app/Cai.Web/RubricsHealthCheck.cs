using Cai.Scoring;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cai.Web;

/// <summary>Readiness probe (P2): healthy only when the rubric catalog store has at least one published version to
/// serve. The app's whole job is serving the versioned rubric catalogs, so an empty store means "not ready" — this is
/// what the deploy workflow polls before swapping the live app.</summary>
public sealed class RubricsHealthCheck(RubricCatalogStore store) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var latest = store.Latest();
        return Task.FromResult(latest is null
            ? HealthCheckResult.Unhealthy("no rubric versions published")
            : HealthCheckResult.Healthy($"latest rubric {latest}"));
    }
}
