using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cai.Web.Noise;

/// <summary>
/// Whether the noise standard can actually do its job — checked, not assumed.
/// </summary>
/// <remarks>
/// <para>★★ WRITTEN BECAUSE /health WAS GREEN FOR TWO DAYS WHILE THE STANDARD WAS DOWN. On 2026-08-20 an
/// unattended dotnet upgrade deleted the shared framework directory the running process was mapped to.
/// Everything already loaded kept working, so <c>/health</c> answered 200 and the deploy stayed green —
/// but <c>System.Formats.Asn1</c> is reached ONLY by the corpus signature verify, behind a
/// <see cref="Lazy{T}"/>, so the first request that needed it threw and the exception was cached. Every
/// noise endpoint returned 500 for two days and nothing said so.</para>
///
/// <para><b>So the rule this encodes:</b> a readiness probe must FORCE the lazily-initialised work, not
/// merely prove a process started. Each check below touches something that can actually be broken, and
/// each says what it means in words an operator can act on rather than a status code they must interpret.</para>
///
/// <para>★ Reported as a LIST with a traffic light per point, not one aggregate. "The standard is
/// unhealthy" sends somebody reading logs; "the corpus signature does not verify, re-run the deploy which
/// re-signs on the box" sends them to the fix. An aggregate that hides which measuring point failed is the
/// same defect as /health one level up.</para>
/// </remarks>
internal sealed class NoiseStandardHealthCheck(INoiseStore store) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        var failures = new List<string>();
        var warnings = new List<string>();

        // ── 1. The corpus, and its signature ──────────────────────────────────────────────────────
        // ★★ THE CHECK THAT WOULD HAVE CAUGHT THE OUTAGE. Load() is the Lazy<T> that cached the
        //    exception; touching it here means a readiness probe fails at the same moment the API does,
        //    instead of two days later when somebody happens to look.
        try
        {
            var manifest = CorpusManifest.Load();
            data["corpus.keyId"] = manifest.KeyId;
            data["corpus.version"] = manifest.Version;
            data["corpus.signatureValid"] = manifest.SignatureValid;
            data["corpus.candidates"] = manifest.Candidates.Count;
            data["corpus.draws"] = string.Join(",", manifest.Draws.Keys.OrderBy(k => k, StringComparer.Ordinal));

            if (!manifest.SignatureValid)
            {
                failures.Add(
                    "the corpus signature does not verify, so no draw can be served. Either the manifest "
                  + "was edited without re-signing it or it was signed by a different key — re-run the "
                  + "deploy, which signs on the box before it publishes. "
                  + (manifest.Problem ?? ""));
            }

            if (manifest.Draws.Count == 0)
            {
                failures.Add("the corpus publishes no draws, so no period can be measured at all.");
            }

            // ★ A pool below the reserved floor cannot keep the recency decay curve's endpoint — the
            //   never-trained bucket empties as the standard matures, which is exactly when it matters.
            var reserved = manifest.Candidates.Count(c => c.Reserved);
            data["corpus.reserved"] = reserved;
            if (reserved < NoiseCorpus.MinimumReservedRepositories)
            {
                warnings.Add(
                    $"only {reserved} reserved repositories, below the floor of "
                  + $"{NoiseCorpus.MinimumReservedRepositories} the decay curve needs an endpoint from.");
            }
        }
        catch (Exception ex)
        {
            // ★★ ANY exception, not just CryptographicException. The 2026-08-20 outage was a
            //    FileNotFoundException from the runtime — LoadCore catches only CryptographicException, so
            //    it sailed straight through as a 500. A probe that narrows the catch repeats the bug.
            failures.Add($"the corpus could not be loaded at all: {ex.GetType().Name}: {ex.Message}");
            data["corpus.exception"] = ex.GetType().FullName ?? "unknown";
        }

        // ── 2. The method contract ────────────────────────────────────────────────────────────────
        try
        {
            var versions = MethodVersions.History;
            data["method.versions"] = versions.Count;
            data["method.current"] = versions.Count > 0 ? versions[^1].Version : "(none)";
            if (versions.Count == 0)
            {
                failures.Add("no method version is published, so no rate can name the method that governed it.");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"the method contract could not be read: {ex.Message}");
        }

        // ── 3. The store ──────────────────────────────────────────────────────────────────────────
        // ★ Reads rather than writes: a readiness probe that inserts leaves rows behind, and this one runs
        //   on every deploy and every dashboard refresh.
        try
        {
            var published = store.PublishedPeriods();
            data["store.publishedPeriods"] = published.Count;
            data["store.latestPublished"] = published.Count > 0 ? published[0] : "(none)";
        }
        catch (Exception ex)
        {
            failures.Add($"the register's store is unreachable: {ex.GetType().Name}: {ex.Message}");
        }

        // ── verdict ───────────────────────────────────────────────────────────────────────────────
        data["failures"] = failures.Count;
        data["warnings"] = warnings.Count;

        if (failures.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(string.Join(" | ", failures), data: data));
        }

        // ★ Degraded, not Unhealthy: the standard still serves. Amber is a real state and collapsing it
        //   into either neighbour loses the one thing a dashboard is for — showing what is drifting before
        //   it breaks.
        return Task.FromResult(warnings.Count > 0
            ? HealthCheckResult.Degraded(string.Join(" | ", warnings), data: data)
            : HealthCheckResult.Healthy("the standard can serve its corpus, its method and its register", data));
    }
}
