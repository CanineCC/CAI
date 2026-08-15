namespace Cai.Web.Noise;

/// <summary>
/// The noise-measurement standard's public contract: the verdict set an engine implements, and the
/// method's own rules.
/// </summary>
/// <remarks>
/// <para>★ WHY THIS LIVES IN CAI. A self-measured number is not a claim a buyer can use — the reader
/// cannot tell one vendor's rigour from another's assertion, and today no scanner in this market
/// publishes a measured error rate at all. A shared method makes numbers commensurable, which is the
/// only thing that lets anyone compare.</para>
/// <para><b>CAI specifies and verifies; it does not referee.</b> It publishes the method, the verdict
/// set and the holdout with its seed, and checks that a submitted run reproduces. It never runs anyone's
/// scanner and never owns a verdict. The standard is owned by a participant, and a referee that plays
/// for one team is worth nothing — so the design removes the conflict rather than promising to manage
/// it.</para>
/// <para><b>Anonymous.</b> A standard nobody can read without credentials is not a standard.</para>
/// </remarks>
public static class NoiseStandardEndpoints
{
    /// <summary>
    /// The method version. ★ Versioned because it WILL change, and a standard that changes silently is
    /// worse than none — a number published against v1 must stay readable when v2 exists.
    /// </summary>
    public const string MethodVersion = "noise-1.0-draft";

    /// <summary>
    /// ★ Combined exclusions above this VOID a run.
    /// </summary>
    /// <remarks>
    /// Items nobody could judge leave the rate, because scoring agreement on a question with no
    /// determinate answer measures coin flips. But they are NOT randomly distributed — they concentrate
    /// where the evidence is thin, which is where judging is worst — so unbounded exclusion flatters a
    /// result for reasons having nothing to do with the tool. A bounded, published exclusion is a
    /// diagnostic; an unbounded one is a laundry.
    /// </remarks>
    public const double MaxExclusionRate = 0.05;

    public static void MapNoiseStandard(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/noise/verdicts", () => Results.Ok(new
        {
            methodVersion = MethodVersion,
            note = "The verdict set every participating engine implements. A human and a machine answer "
                 + "from the SAME set — agreement between two different questions is not a measurement.",
            verdicts = Enum.GetValues<NoiseVerdict>().Select(v => new
            {
                value = v.Wire(),
                meaning = v.Meaning(),
                countsTowardRate = v.CountsTowardRate(),
                isNoise = v.IsNoise(),
                isActionable = v.IsActionable(),
                isProcessDefect = v.IsProcessDefect(),

                // ★ Published rather than assumed: an implementer who gets this backwards reports a
                // flattering number in good faith.
                excludesForHuman = v.ExcludesFor(NoiseRater.Human),
                excludesForMachine = v.ExcludesFor(NoiseRater.Machine),
                escalatesForHuman = v.EscalatesFor(NoiseRater.Human),
                escalatesForMachine = v.EscalatesFor(NoiseRater.Machine),
            }),
        }))
        .AllowAnonymous()
        .WithName("NoiseVerdicts");

        endpoints.MapGet("/api/noise/method", () => Results.Ok(new
        {
            version = MethodVersion,

            // ★★ The single most important sentence in the standard.
            noiseRateIsAQualityScore = false,
            requiresRecallCounterpart = true,
            precisionOnlyWarning =
                "A noise rate is a PRECISION measure. It says nothing about what a tool failed to find, "
              + "and the cheapest way to improve one is to report less — so a specification publishing "
              + "precision alone would reward under-detection across every tool that adopted it.",

            // The absolutes are what expose suppression; the ratio alone hides it. A tool at 42 valid /
            // 8 noise per 100k LoC has a WORSE ratio than one at 12 valid / 2 noise, and is plainly the
            // better instrument.
            requiredWithEveryRate = new[]
            {
                "validPer100kLoc",
                "noisePer100kLoc",
                "recallEstimate",
                "recallMethod",
                "exclusionCount",
                "exclusionRate",
                "actionabilityRate",
                "claimClassBreakdown",
                "toolVersion",
                "holdoutSeed",
                "minimumDetectableDifference",
            },

            maxExclusionRate = MaxExclusionRate,
            exclusionRule =
                "Items nobody could judge leave the rate. Their counts publish per dimension, and a run "
              + "whose combined exclusions exceed the ceiling is VOID — not a pass with a caveat and not "
              + "a verdict on the tool: the instrument was unfit to run, so it is fixed and run again.",

            // ★ A noise rate compares only tools making comparably falsifiable claims. "Line 42
            // dereferences null" can be a false positive; "this file is a hotspot" cannot be, in the
            // same sense — so a naive pooled rate penalises the more specific tool.
            claimClasses = new[] { "pointwise", "structural", "statistical", "advisory" },
            claimClassRule =
                "Every dimension declares its class, and rates publish PER CLASS as well as pooled. A "
              + "tool that is 95% pointwise and one that is 80% statistical do not have comparable "
              + "pooled rates, and presenting them side by side is a category error.",

            reportingRule =
                "Publish both the pooled (micro) and cluster-weighted (macro) average, and the "
              + "leave-one-out range. Excluding an outlying repository is NOT permitted — dropping a "
              + "repo for having a high or low rate is selecting on the outcome.",
        }))
        .AllowAnonymous()
        .WithName("NoiseMethod");

        // ── The holdout ───────────────────────────────────────────────────────────────────────────
        //
        // ★★ Published WITH ITS SEED AND RULES, so a third party re-derives the draw and confirms it was
        // not chosen to flatter anybody. A holdout published without them is an assertion, and the whole
        // standard rests on it being a fact.
        endpoints.MapGet("/api/noise/holdout/{period}", (string period) =>
        {
            if (!NoiseCorpus.Draws.TryGetValue(period, out var draw))
            {
                // ★ 404, never an empty draw. An empty holdout reads as "we measured nothing there",
                // which is a different and false claim from "no draw has been published for that period".
                return Results.NotFound(new
                {
                    period,
                    error = "no holdout has been published for that period",
                    published = NoiseCorpus.Draws.Keys.OrderBy(k => k, StringComparer.Ordinal),
                });
            }

            var repos = HoldoutSampler.Draw(draw.Seed, NoiseCorpus.Candidates, NoiseCorpus.Rules);

            return Results.Ok(new
            {
                period,
                methodVersion = MethodVersion,
                samplerVersion = NoiseCorpus.SamplerVersion,

                // Everything needed to re-run the draw, and nothing that could have steered it.
                seed = draw.Seed,
                drawnAt = draw.DrawnAt,
                rules = new
                {
                    targetProductionLocPerLanguage = NoiseCorpus.Rules.TargetProductionLocPerLanguage,
                    maxRepositoryLoc = NoiseCorpus.Rules.MaxRepositoryLoc,
                    minRepositoriesPerLanguage = NoiseCorpus.Rules.MinRepositoriesPerLanguage,
                    minRepositoriesPerSlice = NoiseCorpus.Rules.MinRepositoriesPerSlice,
                },
                reproduce =
                    "Rank each candidate by SHA-256(seed + NUL + repoId), ascending, tie-broken by "
                  + "repoId; per language take in that order until BOTH the LoC target and the "
                  + "repository floor are met. Candidates above maxRepositoryLoc are excluded first.",

                languages = repos.GroupBy(r => r.Language, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => new
                    {
                        language = g.Key,
                        repositories = g.Count(),
                        productionLoc = g.Sum(r => r.ProductionLoc),
                    }),

                repositories = repos.Select(r => new
                {
                    repoId = r.RepoId,
                    language = r.Language,
                    pinnedSha = r.PinnedSha,
                    productionLoc = r.ProductionLoc,
                    licence = r.Licence,
                }),
            });
        })
        .AllowAnonymous()
        .WithName("NoiseHoldout");

        // ★ The pool publishes too. A third party re-deriving a draw needs the seed AND the candidates
        // it was drawn from — publishing only the seed proves nothing, because the pool could have been
        // chosen after the fact.
        endpoints.MapGet("/api/noise/corpus", () => Results.Ok(new
        {
            samplerVersion = NoiseCorpus.SamplerVersion,
            note = "Public repositories only. Everything a human rater is shown must already be public.",
            count = NoiseCorpus.Candidates.Count,
            repositories = NoiseCorpus.Candidates
                .OrderBy(c => c.RepoId, StringComparer.Ordinal)
                .Select(c => new
                {
                    repoId = c.RepoId,
                    language = c.Language,
                    productionLoc = c.ProductionLoc,
                    licence = c.Licence,
                    pinnedSha = c.PinnedSha,
                }),
        }))
        .AllowAnonymous()
        .WithName("NoiseCorpus");
    }
}
