using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Cai.Web.Noise;

/// <summary>
/// One repository the draw may consider — objective attributes ONLY.
/// </summary>
/// <remarks>
/// ★★ Nothing about any scanner's output appears here, and that absence is the standard's central
/// guarantee. Filtering repositories by how many findings they produce conditions the sample on the very
/// thing being measured; it was tested on Watchdog's cycle 1, where a 50–250 finding cap moved csharp
/// −15.5 points and java +9.3. A field naming a finding, verdict or rate must never be added — the
/// shape is asserted structurally in the tests for exactly that reason.
/// </remarks>
/// <param name="Reserved">
/// ★★ PERMANENTLY PRISTINE: a repository no participant develops against, ever. 02 §1 needs an endpoint for the
/// decay curve — the recency strata compare "never trained" against "trained N cycles ago", and if every
/// repository is eventually developed against, that bucket empties as the standard matures. The overfitting gap
/// would become uncomputable exactly when the tools have had time to overfit.
/// <para>A reserved repository is in EVERY draw, and declaring one as trained is refused — that declaration IS
/// the reservation being broken, and it is the only moment anybody outside the vendor can see it happen.</para>
/// </param>
public sealed record HoldoutCandidate(
    string RepoId,
    string Language,
    int ProductionLoc,
    string Licence,
    string PinnedSha,
    bool Reserved = false);

/// <summary>
/// The pre-registered rules a draw is made under. Published before the draw, never after.
/// </summary>
/// <param name="TargetProductionLocPerLanguage">
/// ★ The sample is sized by LoC, not by repository count and not by finding count. Precision follows the
/// number of FINDINGS, so the target should express expected findings — but finding count is an OUTCOME
/// and an outcome must never reach the draw. LoC is the outcome-blind proxy, calibrated from the
/// historical LoC→findings relationship: a findings-calibrated target with a blind draw.
/// </param>
/// <param name="MaxRepositoryLoc">
/// ★ A ceiling for tractability, expressed in LoC and NEVER in findings. A size cap is defensible only
/// when pre-registered; a finding cap is selection on the outcome whenever it is declared.
/// </param>
/// <param name="MinRepositoriesPerLanguage">
/// ★ 500 findings from one repository carry far less information than 500 from twenty — same codebase,
/// same conventions, correlated errors. The floor holds even once the LoC target is met.
/// </param>
/// <param name="MinRepositoriesPerSlice">The floor any published slice must reach to be reportable.</param>
public sealed record HoldoutRules(
    int TargetProductionLocPerLanguage,
    int MaxRepositoryLoc,
    int MinRepositoriesPerLanguage,
    int MinRepositoriesPerSlice);

/// <summary>
/// The draw: a pure function of a published seed and a public candidate pool.
/// </summary>
/// <remarks>
/// <para>★★ A holdout nobody can re-derive is worthless — the reader has only our word that it was not
/// chosen to flatter somebody. Anyone holding the seed and the pool runs this and gets the same
/// repositories, which is what turns "we drew it blind" from a promise into a checkable fact.</para>
/// <para>Deterministic by construction: candidates are ranked by <c>SHA-256(seed + repoId)</c>, which
/// depends on nothing but its inputs — no clock, no random source, no iteration order. The pool may
/// arrive in any order.</para>
/// </remarks>
public static class HoldoutSampler
{
    /// <summary>Draw the holdout. Pure: same inputs, same output, forever.</summary>
    public static IReadOnlyList<HoldoutCandidate> Draw(
        string seed, IReadOnlyList<HoldoutCandidate> pool, HoldoutRules rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(rules);

        var drawn = new List<HoldoutCandidate>();

        // ★★ THE RESERVED SLICE IS IN EVERY DRAW, BEFORE THE SEEDED RANKING RUNS. A holdout that happened to miss
        // the reserved repositories in a given period would have no never-trained bucket for that period, and
        // nothing in the published number would say so — so this is not left to the ranking to get right. It is
        // "reserved", not "usually included".
        drawn.AddRange(pool
            .Where(c => c.Reserved)
            .OrderBy(c => c.RepoId, StringComparer.Ordinal));

        foreach (var group in pool
            .Where(c => c.ProductionLoc <= rules.MaxRepositoryLoc)
            .GroupBy(c => c.Language, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            // ★ Ranked by the seeded hash, then by id to break a tie deterministically. Sorting by the
            // hash alone would leave two candidates whose digests collide in whatever order the pool
            // happened to hold them — and the draw would stop being reproducible in exactly the rare
            // case nobody would think to test.
            var ranked = group
                .Where(c => !c.Reserved)   // ★ already drawn above; ranking them again would duplicate them
                .OrderBy(c => Rank(seed, c.RepoId), StringComparer.Ordinal)
                .ThenBy(c => c.RepoId, StringComparer.Ordinal)
                .ToList();

            // ★ The reserved repositories in THIS language already count toward its targets: they are part of the
            // sample, not an addition to it, so a language with a large reserved repo does not get a double share.
            var loc = group.Where(c => c.Reserved).Sum(c => c.ProductionLoc);
            var taken = group.Count(c => c.Reserved);
            foreach (var candidate in ranked)
            {
                // Stop only when BOTH conditions are satisfied: the LoC target buys precision, the
                // repository floor buys independence, and neither substitutes for the other.
                if (loc >= rules.TargetProductionLocPerLanguage
                    && taken >= rules.MinRepositoriesPerLanguage)
                {
                    break;
                }

                drawn.Add(candidate);
                loc += candidate.ProductionLoc;
                taken++;
            }
        }

        return drawn;
    }

    /// <summary>
    /// A candidate's rank under a seed — <c>SHA-256(seed + "\0" + repoId)</c>, hex.
    /// </summary>
    /// <remarks>
    /// The NUL separator matters: without it, seed "ab" + repo "c" and seed "a" + repo "bc" hash
    /// identically, so two different draws could rank a repository the same way and a third party
    /// reproducing one could silently reproduce the other.
    /// </remarks>
    public static string Rank(string seed, string repoId) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Create(CultureInfo.InvariantCulture, $"{seed}\0{repoId}"))));
}
