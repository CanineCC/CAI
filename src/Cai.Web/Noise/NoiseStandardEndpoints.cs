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
    /// <summary>
    /// Kept as a forwarder so nothing reads a second copy of the ceiling.
    /// </summary>
    /// <remarks>
    /// ★★ It USED to be the definition, declared here and compared against nothing while being echoed to
    /// readers at <c>/method</c> as though it governed something. The one definition now lives beside the
    /// check that applies it — see <see cref="PublicationContract.MaxExclusionRate"/>.
    /// </remarks>
    public const double MaxExclusionRate = PublicationContract.MaxExclusionRate;

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

            // ★★ And the counterpart is REACHABLE, not merely required in prose. A rule that names an
            // obligation without providing a way to meet it gets skipped by everyone and blamed on nobody.
            recallEndpoint = "/api/noise/pooled",

            // ★★ Required WITH EVERY PUBLICATION, not merely offered. A number nobody is obliged to fetch
            // does not get fetched: the noise rate has an audience and a marketing use, the fix rate has
            // neither, so left optional the published claim stays "our tool is quiet" instead of "our tool
            // is acted upon". Send the observations, or send a reason — the reason publishes too.
            requiresFixRateAnchor = true,
            fixRateEndpoint = "/api/noise/fixrate",
            recallMethodNote =
                "Recall has no ground truth on real repositories, so the reference is POOLED: the union of "
              + "what participating tools reported and a human adjudicated as valid. POST findings from two "
              + "or more tools to /api/noise/pooled. One tool alone gets no recall figure — its recall "
              + "against a union it alone defines is 100% by construction.",

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

            // ★★ ENFORCED, not merely published. This was a constant echoed here and compared against
            // nothing: exclusions above the ceiling now VOID a publication at /api/noise/publication.
            maxExclusionRate = PublicationContract.MaxExclusionRate,
            exclusionCeilingVoidsTheRun = true,

            // ★ Which recall methods the standard recognises. They do not measure the same thing, so an
            // estimate must name one — and "none" is a legitimate answer that publishes with its reason.
            recallMethods = PublicationContract.RecallMethods,

            // ★★ Where the judging is published. 01 promises every prompt, model version and raw verdict with
            // its reasoning; this is the endpoint that keeps that promise, and naming it here is what makes it
            // findable by somebody who wants to disagree with a verdict.
            verdictRecordEndpoint = "/api/noise/record/{period}",

            // ★★ The pre-publication gate from 05: a run without readable git history cannot publish, because
            // its noise on the history-derived dimensions is an environment artefact rather than a capability
            // gap, and those are exactly the dimensions facing competitors who publish no error rate at all.
            requiresGitMiningVerified = true,

            // ★★ How the tool was CONFIGURED. Every other check constrains the run — the version, the shas,
            // the seed, the model set, the recency declaration — and none of them constrains which rules were
            // switched on, so the right version against the right shas with the noisiest rules off passes all
            // of them. Required, and it publishes.
            requiresConfigurationDeclaration = true,
            exclusionRule =
                "Items nobody could judge leave the rate. Their counts publish per dimension, and a run "
              + "whose combined exclusions exceed the ceiling is VOID — not a pass with a caveat and not "
              + "a verdict on the tool: the instrument was unfit to run, so it is fixed and run again.",

            // ★ A noise rate compares only tools making comparably falsifiable claims. "Line 42
            // dereferences null" can be a false positive; "this file is a hotspot" cannot be, in the
            // same sense — so a naive pooled rate penalises the more specific tool.
            // ★★ Was a bare string array — the vocabulary was PUBLISHED and never implemented, so a
            // submitter could read it and have nowhere to send it. Now each class carries what it asserts
            // and whether it admits a rate at all, and /publication refuses a result without the breakdown.
            claimClasses = Enum.GetValues<ClaimClass>().Select(c => new
            {
                claimClass = ClaimSpecificity.Wire(c),
                describes = ClaimSpecificity.Describes(c),
                admitsANoiseRate = c != ClaimClass.Statistical,
            }),
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

        // ── The verdict record, published in full ─────────────────────────────────────────────────
        //
        // ★★ THE OPEN-JUDGING CLAIM, WITH SOMETHING BEHIND IT. 01-scope-and-governance: "every judge prompt,
        // every model and version, every raw verdict with its reasoning, and every human adjudication.
        // Published in full. A reader who disagrees with a verdict must be able to find it, read the
        // reasoning, and say so." Until this existed the cascade resolved in memory and kept nothing, so the
        // claim a sceptic tests first was the one with nothing to test.
        //
        // ★ Anonymous, like the method and the holdout. A judging record only readable by the people who
        // produced it is not published.
        endpoints.MapGet("/api/noise/record/{period}", (string period, INoiseStore store) =>
        {
            var verdicts = store.ListVerdicts(period);
            var resolutions = store.ListResolutions(period);
            var prompts = store.ListPrompts(period);
            var submissions = store.ListSubmissions(period);

            return Results.Ok(new
            {
                period,
                methodVersion = MethodVersion,

                // ★ An empty record says so rather than looking like a clean one. "Nothing has been judged
                // for this period" and "everything was judged and agreed" are different facts.
                judged = resolutions.Count,
                rawVerdicts = verdicts.Count,
                note = resolutions.Count == 0
                    ? "no judging has been recorded for this period yet — this is an absence, not a clean run."
                    : null,

                // ★★ The prompts, in full and once each. The same prompt answers thousands of findings; a
                // record that repeats it per verdict is a record nobody downloads.
                prompts = prompts.Select(p => new { promptId = p.PromptId, text = p.Text, firstSeenAt = p.FirstSeenAt }),

                resolutions = resolutions.Select(r => new
                {
                    findingId = r.FindingId,
                    state = r.State,
                    verdict = r.Verdict,
                    settledAtRound = r.SettledAtRound,
                    actionabilityContested = r.ActionabilityContested,
                    actionable = r.Actionable,
                    reason = r.Reason,
                    recordedAt = r.RecordedAt,
                }),

                verdicts = verdicts.Select(v => new
                {
                    findingId = v.FindingId,
                    round = v.Round,
                    judge = v.Judge,
                    model = v.Model,
                    modelVersion = v.ModelVersion,
                    promptId = v.PromptId,
                    verdict = v.Verdict,
                    reasoning = v.Reasoning,
                    recordedAt = v.RecordedAt,
                }),

                // ★ The register belongs here too: who submitted, when, and whether it was accepted —
                // including the runs that were refused. A register of only the accepted ones is a register
                // that has been edited.
                submissions = submissions.Select(r => new
                {
                    submissionId = r.SubmissionId,
                    tool = r.Tool,
                    toolVersion = r.ToolVersion,
                    receivedAt = r.ReceivedAt,
                    accepted = r.Accepted,
                    problems = r.Problems,

                    // ★★ The declaration publishes BESIDE the number. A configuration nobody can read is not
                    // a disclosure — the whole value of the declaration is that a competitor or a buyer can
                    // point at it.
                    configuration = store.ConfigurationJson(r.SubmissionId) is { Length: > 0 } json
                        ? System.Text.Json.JsonDocument.Parse(json).RootElement
                        : (System.Text.Json.JsonElement?)null,
                }),
            });
        })
        .AllowAnonymous()
        .WithName("NoiseRecord");

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

        // ── Submissions ───────────────────────────────────────────────────────────────────────────
        //
        // ★ CAI never runs anyone's scanner. A vendor runs their own tool against the published holdout,
        // on their own infrastructure, and submits findings — so CAI needs no credentials, no access and
        // no licence to anybody's product. What it does is VERIFY the run covered the holdout that was
        // published, at the shas that were published.
        endpoints.MapPost("/api/noise/submissions", (NoiseSubmission submission, INoiseStore store) =>
        {
            if (submission is null || string.IsNullOrWhiteSpace(submission.Period))
            {
                return Results.BadRequest(new { error = "a submission names the period it answers" });
            }

            if (!NoiseCorpus.Draws.TryGetValue(submission.Period, out var draw))
            {
                return Results.NotFound(new
                {
                    submission.Period,
                    error = "no holdout has been published for that period",
                });
            }

            // ★★ NO WITHDRAWAL, and therefore no quiet re-run. Otherwise a vendor runs, dislikes the
            // result and submits again, and the published set silently becomes "the results people were
            // happy with" — which is the whole failure the rule exists to prevent.
            if (store.AlreadySubmitted(submission.Tool, submission.Period))
            {
                return Results.Conflict(new
                {
                    submission.Tool,
                    submission.Period,
                    error = "this tool has already submitted for this period, and a submission cannot be "
                          + "withdrawn or replaced. Register intent before the next draw instead.",
                });
            }

            var holdout = HoldoutSampler.Draw(draw.Seed, NoiseCorpus.Candidates, NoiseCorpus.Rules);
            // ★ The draw's own publication timestamp goes in, so the ordering check has something to compare
            // against rather than trusting the submitter's word about which came first.
            var receipt = NoiseSubmissions.Accept(
                submission, holdout, DateTimeOffset.UtcNow, draw.DrawnAt);

            // ★★ THE REGISTER IS THE DATABASE. A rejected run is stored too — it is evidence, and a run a
            // vendor would like to forget is exactly the kind the no-withdrawal rule exists to keep. Only an
            // ACCEPTED one claims the (tool, period) slot, and the claim is a UNIQUE index: losing that race
            // is the rule working, so it comes back as the same conflict a second attempt would get.
            // ★ Serialised here from the parsed declaration so the record publishes what the gate checked —
            // the two cannot disagree about what was declared.
            var configurationJson = submission.Configuration is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(
                    submission.Configuration,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

            if (!store.TryRecordSubmission(receipt, submission.RunStartedAt, configurationJson))
            {
                return Results.Conflict(new
                {
                    submission.Tool,
                    submission.Period,
                    error = "this tool has already submitted for this period, and a submission cannot be "
                          + "withdrawn or replaced. Register intent before the next draw instead.",
                });
            }

            return Results.Ok(Render(receipt));
        })
        .AllowAnonymous()
        .WithName("NoiseSubmit");

        endpoints.MapGet("/api/noise/submissions/{submissionId}", (string submissionId, INoiseStore store) =>
        {
            var receipt = store.FindSubmission(submissionId);
            return receipt is null
                ? Results.NotFound(new { submissionId, error = "no such submission" })
                : Results.Ok(Render(receipt));
        })
        .AllowAnonymous()
        .WithName("NoiseSubmission");

        // ── The cascade ───────────────────────────────────────────────────────────────────────────
        //
        // ★ Published as an endpoint so every participant resolves a disagreement THE SAME WAY. Two
        // vendors applying different escalation rules would produce numbers that look comparable and are
        // not — which is the failure a shared method exists to prevent. It is pure: votes in, outcome
        // out, no model and no state.
        endpoints.MapPost("/api/noise/cascade/resolve", (CascadeRequest request, INoiseStore store) =>
        {
            if (request?.Round1 is null || request.Round1.Count != 2)
            {
                return Results.BadRequest(new
                {
                    error = "a round is exactly two independent judges — one is not a cascade, and three "
                          + "invites a majority that hides a genuine split.",
                });
            }

            var round1 = request.Round1.Select(ToVote).ToList();
            var round2 = (request.Round2 ?? []).Select(ToVote).ToList();

            if (round1.Any(v => v is null) || round2.Any(v => v is null))
            {
                return Results.BadRequest(new
                {
                    error = "every vote must be one of the six published verdicts",
                    verdicts = Enum.GetValues<NoiseVerdict>().Select(v => v.Wire()),
                });
            }

            var outcome = JudgingCascade.Resolve([.. round1!], [.. round2!]);

            // ── The verdict record ────────────────────────────────────────────────────────────────
            //
            // ★★ 01 PROMISES THIS AND NOTHING STORED IT. "Every judge prompt, every model and version, every
            // raw verdict with its reasoning, and every human adjudication. Published in full." The cascade
            // resolved votes in memory and returned an answer, so the one claim a sceptic tests first was the
            // one with nothing behind it. A judged finding now leaves a record, and the record publishes at
            // /api/noise/record/{period}.
            //
            // ★ Recording is refused rather than half-done: a verdict without its model version or its
            // reasoning is not a record a reader can argue with, and storing it would let the endpoint report
            // "recorded" for something unusable.
            var recorded = false;
            List<string> unrecordable = [];
            if (request.Period is { Length: > 0 } period && request.FindingId is { Length: > 0 } findingId)
            {
                var now = DateTimeOffset.UtcNow;
                var all = request.Round1!.Select(v => (Round: 1, Vote: v))
                    .Concat((request.Round2 ?? []).Select(v => (Round: 2, Vote: v)))
                    .ToList();

                foreach (var (round, vote) in all)
                {
                    if (string.IsNullOrWhiteSpace(vote.Model)
                        || string.IsNullOrWhiteSpace(vote.ModelVersion)
                        || string.IsNullOrWhiteSpace(vote.PromptId)
                        || string.IsNullOrWhiteSpace(vote.Reasoning))
                    {
                        unrecordable.Add(
                            $"round {round} judge '{vote.Judge}' — a recorded verdict needs model, "
                          + "modelVersion, promptId and reasoning. A verdict a reader cannot argue with is "
                          + "not open judging.");
                    }
                }

                if (unrecordable.Count == 0)
                {
                    foreach (var (round, vote) in all)
                    {
                        if (vote.Prompt is { Length: > 0 } text)
                        {
                            store.RegisterPrompt(vote.PromptId!, text, now);
                        }

                        store.RecordVerdict(new VerdictRecord(
                            period, findingId, round,
                            vote.Judge ?? "unnamed", vote.Model!, vote.ModelVersion!, vote.PromptId!,
                            NoiseVerdicts.ParseOrNull(vote.Verdict)!.Value.Wire(),
                            vote.Reasoning!, now));
                    }

                    store.RecordResolution(new ResolutionRecord(
                        period, findingId, outcome.State.ToString(), outcome.Verdict?.Wire(),
                        outcome.SettledAtRound, outcome.ActionabilityContested, outcome.Actionable,
                        outcome.Reason, now));
                    recorded = true;
                }
            }

            return Results.Ok(new
            {
                // ★ Says plainly whether this judgement is ON THE RECORD. A caller that meant to record and
                // silently did not would believe the standard was keeping its promise on its behalf.
                recorded,
                unrecordable,

                methodVersion = MethodVersion,
                state = outcome.State.ToString(),
                verdict = outcome.Verdict?.Wire(),
                settledAtRound = outcome.SettledAtRound,

                // ★ Published separately: the judges can agree a finding is valid and split on whether
                // anyone could act on it. Picking one view would put a figure nobody agreed on into the
                // actionability axis.
                actionabilityContested = outcome.ActionabilityContested,
                actionable = outcome.Actionable,

                reason = outcome.Reason,
            });

            static JudgeVote? ToVote(CascadeVote v) =>
                NoiseVerdicts.ParseOrNull(v.Verdict) is { } parsed
                    ? new JudgeVote(v.Judge ?? "unnamed", parsed)
                    : null;
        })
        .AllowAnonymous()
        .WithName("NoiseCascadeResolve");

        // ── The crowd layer ───────────────────────────────────────────────────────────────────────
        //
        // ★★ The only check in the method that comes from OUTSIDE the model family. Four judges agreeing
        // shows they are consistent; it does not show they are right, and adding judges never converts
        // one into the other. So a sample of what they AGREED on goes to people too — not only the
        // contested tail, which is the efficient choice and exactly where the independence is wasted.
        endpoints.MapPost("/api/noise/crowd/queue", (CrowdQueueRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Period) || request.Candidates is not { Count: > 0 })
            {
                return Results.BadRequest(new { error = "a period and at least one candidate are required" });
            }

            List<CrowdCandidate> candidates = [];
            foreach (var c in request.Candidates)
            {
                if (CrowdQueues.ParseState(c.State) is not { } state)
                {
                    return Results.BadRequest(new
                    {
                        error = $"unrecognised cascade state '{c.State}' — a candidate CAI cannot place is "
                              + "rejected, never dropped, because a sample that silently shrank looks "
                              + "exactly like one drawn correctly.",
                        states = new[] { "accepted", "needs-round-2", "needs-human" },
                    });
                }

                candidates.Add(new CrowdCandidate(c.FindingId ?? "", state, c.OwnerId ?? ""));
            }

            var queue = CrowdQueue.Build(
                candidates, request.Seed ?? request.Period, Math.Max(0, request.SpotCheck));
            CrowdQueues.Register(request.Period, queue);

            // ★ COUNTS, never names. The operator needs to know a sample was drawn; publishing which
            // findings are in it lets a participant recognise them, and a spot-check you can identify is
            // a spot-check you can prepare for.
            return Results.Ok(new
            {
                methodVersion = MethodVersion,
                period = request.Period,
                queued = queue.Count,
                contested = queue.Count(i => i.Reason == CrowdReason.Contested),
                spotCheck = queue.Count(i => i.Reason == CrowdReason.SpotCheck),
            });
        })
        .AllowAnonymous()
        .WithName("NoiseCrowdQueue");

        // ★ ONE ITEM. The nine-second median in the pilot came from a 500-item list to get through;
        // there is no slog to race when the ask is a single question.
        endpoints.MapGet("/api/noise/crowd/next", (string period, string raterId) =>
        {
            if (CrowdQueues.Find(period) is not { } round)
            {
                return Results.NotFound(new { error = $"no crowd queue is registered for period '{period}'" });
            }

            var now = DateTimeOffset.UtcNow;
            var answered = round.Answers.Where(a => Same(a.RaterId, raterId)).Select(a => a.FindingId).ToList();

            // ★★ Dosed, or calibration is unreachable. The live round left both raters below the minimum
            // sample because honeypots came up only by chance; among hundreds of findings a person
            // answering one question a day would never be calibrated at all.
            var honeypotsAnswered = answered.Count(round.Honeypots.ContainsKey);
            var due = HoneypotDosing.IsDue(raterId, answered.Count, honeypotsAnswered);

            // ★★ Load-aware, or the queue's head goes to everybody — which is what the live run did,
            // handing eight raters the same finding while seven others, contested ones included, went
            // unanswered.
            if (CrowdQueue.Next(
                    round.Queue, raterId, answered, round.Load(now),
                    honeypots: [.. round.Honeypots.Keys], preferHoneypot: due) is not { } item)
            {
                return Results.NoContent();
            }

            round.Offered[(raterId, item.FindingId)] = now;

            // ★★ THE FINDING AND NOTHING ELSE. Told four judges already agreed, a reasonable person
            // reads "probably fine" and rubber-stamps — and the spot-check exists precisely to catch the
            // case where all four were wrong together. A reason on the wire would destroy the only
            // evidence it was built to gather, and nothing downstream would ever show that it had.
            return Results.Ok(new { findingId = item.FindingId });
        })
        .AllowAnonymous()
        .WithName("NoiseCrowdNext");

        endpoints.MapPost("/api/noise/crowd/answers", (CrowdAnswerRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Period) || CrowdQueues.Find(request.Period) is not { } round)
            {
                return Results.NotFound(new { error = "no crowd queue is registered for that period" });
            }

            if (NoiseVerdicts.ParseOrNull(request.Verdict) is not { } verdict)
            {
                return Results.BadRequest(new
                {
                    error = "an answer must be one of the six published verdicts",
                    verdicts = Enum.GetValues<NoiseVerdict>().Select(v => v.Wire()),
                });
            }

            // ★ An answer to a finding this rater was never handed is REFUSED. Without the check the
            // queue is only a suggestion, and a participant could answer the whole accepted pool —
            // including the items they were deliberately not shown, which is what the disguise exists
            // to prevent.
            if (!round.Offered.ContainsKey((request.RaterId ?? "", request.FindingId ?? "")))
            {
                return Results.Conflict(new
                {
                    error = "that finding was never handed to that rater",
                    findingId = request.FindingId,
                });
            }

            round.Answers.Add(new CrowdAnswer(
                request.FindingId!, request.RaterId!, verdict, NoiseVerdicts.ParseOrNull(request.MachineVerdict)));

            return Results.Ok(new { recorded = true, findingId = request.FindingId });
        })
        .AllowAnonymous()
        .WithName("NoiseCrowdAnswer");

        // ★★ Calibration against findings that were settled OUTSIDE the rating process. The obvious
        // construction — score raters against what the crowd agreed — measures conformity and calls it
        // accuracy: highest for repeating the majority, lowest for catching what everyone missed. So a
        // honeypot's truth must be a fact about the world that would hold if nobody had rated anything.
        endpoints.MapPost("/api/noise/crowd/honeypots", (HoneypotRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Period) || CrowdQueues.Find(request.Period) is not { } round)
            {
                return Results.NotFound(new { error = "no crowd queue is registered for that period" });
            }

            if (request.Honeypots is not { Count: > 0 })
            {
                return Results.BadRequest(new { error = "at least one honeypot is required" });
            }

            List<Honeypot> planted = [];
            foreach (var h in request.Honeypots)
            {
                if (RaterCalibration.ParseSource(h.Source) is not { } source)
                {
                    return Results.BadRequest(new
                    {
                        error = $"'{h.Source}' is not an earned source. A honeypot's answer must be settled "
                              + "outside the rating process — scoring raters against what the crowd agreed "
                              + "measures conformity, not accuracy, and rewards repeating the majority.",
                        sources = new[] { "upstream-fix-merged", "vendor-withdrew", "advisory-retracted" },
                    });
                }

                if (NoiseVerdicts.ParseOrNull(h.Truth) is not { } truth)
                {
                    return Results.BadRequest(new
                    {
                        error = "a honeypot's truth must be one of the six published verdicts",
                        verdicts = Enum.GetValues<NoiseVerdict>().Select(v => v.Wire()),
                    });
                }

                var honeypot = new Honeypot(h.FindingId ?? "", truth, source, h.Evidence);
                if (!RaterCalibration.IsWellFormed(honeypot))
                {
                    return Results.BadRequest(new
                    {
                        error = "evidence must be a link a third party can open — \"we checked\" is the "
                              + "same claim the honeypot exists to be independent of.",
                        findingId = h.FindingId,
                    });
                }

                // ★ Planted into the EXISTING queue. A honeypot that is not already a question somebody
                // could be asked is a separate exam, and a separate exam is one a rater can recognise.
                if (!round.Queue.Any(i => string.Equals(i.FindingId, honeypot.FindingId, StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.Conflict(new
                    {
                        error = "that finding is not in this period's queue",
                        findingId = honeypot.FindingId,
                    });
                }

                planted.Add(honeypot);
            }

            foreach (var honeypot in planted)
            {
                round.Honeypots[honeypot.FindingId] = honeypot;
            }

            return Results.Ok(new { period = request.Period, planted = planted.Count });
        })
        .AllowAnonymous()
        .WithName("NoiseCrowdHoneypots");

        // ★★ Who answered. Agreement statistics are blind to shared bias: ten raters who all work in one
        // language, or all work for the vendor, agree at a rate that reads as reliability and is nothing
        // of the kind. κ measures whether raters agree, never whether what they agree on is true.
        endpoints.MapPost("/api/noise/crowd/raters", (RaterDeclarationRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Period) || CrowdQueues.Find(request.Period) is not { } round)
            {
                return Results.NotFound(new { error = "no crowd queue is registered for that period" });
            }

            if (string.IsNullOrWhiteSpace(request.RaterId) || string.IsNullOrWhiteSpace(request.PrimaryLanguage))
            {
                return Results.BadRequest(new { error = "a raterId and a primaryLanguage are required" });
            }

            if (ParseAffiliation(request.Affiliation) is not { } affiliation)
            {
                return Results.BadRequest(new
                {
                    error = "an affiliation is required, and 'unknown' is not one of them — a vendor "
                          + "rating its own tool's findings is the conflict this standard exists to "
                          + "remove, and it cannot be declared by omission.",
                    affiliations = new[]
                    {
                        "independent", "vendor-employed", "vendor-contracted", "compensated-in-product",
                    },
                });
            }

            round.Strata[request.RaterId] = new RaterStratum(request.RaterId, request.PrimaryLanguage, affiliation);

            return Results.Ok(new { period = request.Period, raterId = request.RaterId });
        })
        .AllowAnonymous()
        .WithName("NoiseCrowdRaters");

        // ★★ What has to travel with a rate for the rate to mean anything: the funnel it was computed
        // over, the actionability split, and the difference this sample could actually detect.
        endpoints.MapPost("/api/noise/publication", (PublicationRequest request) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "a run is required" });
            }

            // ★ The census is checked FIRST. Whether the numbers can be trusted at all comes before
            // whether the publication is complete — told both at once, an operator fixes the easier one.
            var census = PublicationSurface.CheckCensus(
                request.Reported, request.Adjudicated, request.Excluded, request.Unrated);

            if (!census.Balances)
            {
                return Results.BadRequest(new
                {
                    error = "the census does not balance: reported must equal adjudicated + excluded + "
                          + "unrated. A funnel that does not add up has a step nobody is reporting, and "
                          + "the missing findings are exactly the ones a reader would want to see.",
                    reported = census.Reported,
                    adjudicated = census.Adjudicated,
                    excluded = census.Excluded,
                    unrated = census.Unrated,
                    shortfall = census.Shortfall,
                });
            }

            // ★★ THE WHOLE CONTRACT, IN ONE PASS. /api/noise/method has always published ten fields as
            // required with every rate; nothing checked them, and the exclusion ceiling it echoed was
            // compared against nothing at all. Every breach comes back together — told one at a time, a
            // submitter fixes six things over six round-trips and learns nothing about the shape of it.
            List<ClaimClassTally> claims = [];
            foreach (var entry in request.ClaimClasses ?? [])
            {
                if (ClaimSpecificity.ParseOrNull(entry.ClaimClass) is not { } parsed)
                {
                    return Results.BadRequest(new
                    {
                        error = $"unrecognised claim class '{entry.ClaimClass}'",
                        classes = Enum.GetValues<ClaimClass>().Select(ClaimSpecificity.Wire),
                    });
                }

                claims.Add(new ClaimClassTally(parsed, entry.Judged, entry.Noise));
            }

            // ★ The vendor's own declaration of which holdout repositories it has developed against. It
            // cannot be derived from the draw — "has this tool seen this code?" is a property of the tool —
            // so it is declared, and published, which is what makes it costly to get wrong.
            List<RecencyTally> recency = [];
            foreach (var entry in request.RecencyStrata ?? [])
            {
                if (Noise.RecencyStrata.ParseOrNull(entry.Stratum) is not { } parsed)
                {
                    return Results.BadRequest(new
                    {
                        error = $"unrecognised recency stratum '{entry.Stratum}'",
                        strata = Enum.GetValues<RecencyStratum>().Select(Noise.RecencyStrata.Wire),
                    });
                }

                recency.Add(new RecencyTally(parsed, entry.Judged, entry.Noise));
            }

            var breaches = PublicationContract.Check(
                request.LocCovered,
                request.RecallEstimate, request.RecallMethod, request.RecallNote,
                claims,
                request.ToolVersion, request.HoldoutSeed, request.ModelSet,
                request.GitMiningVerified,
                request.Adjudicated, request.Excluded,
                hasFixRateObservations: request.FixRateObservations is { Count: > 0 },
                fixRateUnavailable: request.FixRateUnavailable,
                fixRateWindowDays: request.FixRateWindowDays);

            if (breaches.Count > 0)
            {
                return Results.BadRequest(new
                {
                    error = "this result does not meet the contract /api/noise/method publishes. Every "
                          + "requirement below exists because a rate without it invites a comparison that "
                          + "cannot be made fairly.",
                    breaches = breaches.Select(b => new { field = b.Field, error = b.Error }),
                    methodVersion = MethodVersion,
                });
            }

            var hasObservations = request.FixRateObservations is { Count: > 0 };

            FixRateSummary? anchor = null;
            if (hasObservations)
            {
                List<FixObservation> observations = [];
                foreach (var o in request.FixRateObservations!)
                {
                    if (ParseOutcome(o.Outcome) is not { } outcome)
                    {
                        return Results.BadRequest(new
                        {
                            error = $"unrecognised fix-rate outcome '{o.Outcome}'",
                            outcomes = new[] { "cited-location-changed", "unchanged", "file-deleted", "not-observable" },
                        });
                    }

                    observations.Add(new FixObservation(
                        o.FindingId ?? "", o.RepoId ?? "", outcome, NoiseVerdicts.ParseOrNull(o.CrowdVerdict)));
                }

                anchor = FixRateAnchor.Compute(observations, request.FixRateWindowDays!.Value);
            }

            var summary = PublicationSurface.Summarise(
                request.Reported, request.Adjudicated, request.Excluded, request.Unrated,
                request.ValidAndActionable, request.ValidNotActionable, request.Noise,
                request.Clusters);

            var judged = request.ValidAndActionable + request.ValidNotActionable + request.Noise;
            var rate = judged > 0 ? (double?)request.Noise / judged : null;

            return Results.Ok(new
            {
                methodVersion = MethodVersion,

                census = new
                {
                    reported = summary.Census.Reported,
                    adjudicated = summary.Census.Adjudicated,
                    excluded = summary.Census.Excluded,
                    unrated = summary.Census.Unrated,
                    balances = summary.Census.Balances,
                },

                // ★ Published as counts as well as a rate. The absolutes are what expose suppression; the
                // ratio alone hides it.
                validAndActionable = summary.ValidAndActionable,
                validNotActionable = summary.ValidNotActionable,
                noise = summary.Noise,
                noiseRate = rate,

                // ★★ Over VALID findings only. Divided by everything reported it would mix precision in,
                // and a tool could improve its actionability by producing more noise.
                actionabilityRate = summary.ActionabilityRate,

                // ★★ THE ABSOLUTES, per 100k LoC. The ratio above hides suppression; these expose it. A tool
                // that stops reporting improves its rate and cannot improve these.
                locCovered = request.LocCovered,
                validPer100kLoc = Per100k(request.ValidAndActionable + request.ValidNotActionable, request.LocCovered),
                noisePer100kLoc = Per100k(request.Noise, request.LocCovered),

                // ★★ Per class, never only pooled. Two tools' pooled rates are not comparable unless their
                // output is comparably falsifiable, and the statistical class gets NO rate — "not measurable
                // under this method" is an honest cell; a blank that reads as clean is not.
                claimClasses = claims.Select(c => new
                {
                    claimClass = ClaimSpecificity.Wire(c.Class),
                    describes = ClaimSpecificity.Describes(c.Class),
                    judged = c.Judged,
                    noise = c.Noise,
                    noiseRate = c.NoiseRate,
                    measurable = c.Measurable,
                    notMeasurableReason = c.Measurable
                        ? null
                        : "a statistical claim has no false-positive state, so a rate over it would measure "
                        + "the raters' opinions rather than the tool.",
                }),
                measurableShare = ClaimSpecificity.MeasurableShare(claims),
                pooledRateComparable = !ClaimSpecificity.NothingFalsifiable(claims),

                // ★★ The ceiling, APPLIED. It was published here and compared against nothing.
                exclusionRate = PublicationContract.ExclusionRate(request.Adjudicated, request.Excluded),
                maxExclusionRate = PublicationContract.MaxExclusionRate,

                // ★★ THE OVERFITTING NUMBER — the most interesting figure the standard produces, and one no
                // vendor would publish about itself unprompted. Every other number here can be improved by
                // building a better tool; this one can only be improved by building one that generalises.
                recency = new
                {
                    declared = recency.Count > 0,
                    hasPristineSlice = Noise.RecencyStrata.HasPristineSlice(recency),
                    strata = recency.Select(t => new
                    {
                        stratum = Noise.RecencyStrata.Wire(t.Stratum),
                        means = Noise.RecencyStrata.Means(t.Stratum),
                        judged = t.Judged,
                        noise = t.Noise,
                        noiseRate = t.NoiseRate,
                    }),
                    overfittingGapPoints = Noise.RecencyStrata.OverfittingGap(recency),
                    gapIsNotable = Noise.RecencyStrata.GapIsNotable(Noise.RecencyStrata.OverfittingGap(recency)),
                    // ★ A missing gap and a gap of zero are OPPOSITE claims and look identical when one of
                    // them is a blank, so the absence is stated rather than left as null.
                    note = recency.Count == 0
                        ? "no recency strata declared: this run says nothing about whether its rate describes "
                        + "the instrument or the vendor's familiarity with the sample."
                        : Noise.RecencyStrata.HasPristineSlice(recency)
                            ? null
                            : "no pristine slice in this holdout, so the overfitting gap cannot be computed. "
                            + "Without a never-trained endpoint the decay curve measures nothing, and "
                            + "'one cycle of cooling off is enough' stays an assertion.",
                },

                // ★ The recall counterpart, beside the precision figure rather than in a side endpoint.
                recall = new
                {
                    estimate = request.RecallEstimate,
                    method = request.RecallMethod,
                    note = request.RecallNote,
                },

                // ★ 04 fix #1: the gap backlog IS a recall signal, and it costs a query. Published as a
                // standing figure so a falling noise rate beside a rising gap count is visible as what it is.
                gapsFoundSinceLastPeriod = request.GapsFoundSinceLastPeriod,

                provenance = new
                {
                    toolVersion = request.ToolVersion,
                    holdoutSeed = request.HoldoutSeed,
                    modelSet = request.ModelSet,
                    gitMiningVerified = request.GitMiningVerified,
                },

                clusters = summary.Clusters,
                intraClusterCorrelation = PublicationSurface.DefaultIntraClusterCorrelation,
                minimumDetectableDifference = summary.MinimumDetectableDifference,

                // ★★ And the threshold is APPLIED, not merely published: a move smaller than it is
                // neither an improvement nor a regression.
                distinguishableFromPrevious = request.PreviousRate is { } previous && rate is { } current
                    && PublicationSurface.Distinguishable(current, previous, summary.MinimumDetectableDifference),

                // ★★ Beside the judged numbers, not in a side endpoint. It is the only figure here that
                // no amount of shared bias among raters can move.
                fixRate = anchor is null
                    ? new
                    {
                        declared = false,
                        unavailableReason = request.FixRateUnavailable,
                        windowDays = (int?)null,
                        observed = (int?)null,
                        fixedFindings = (int?)null,
                        rate = (double?)null,
                        excludedFileDeleted = (int?)null,
                        unobservable = (int?)null,
                        calledNoiseThenFixed = Array.Empty<string>(),
                    }
                    : new
                    {
                        declared = true,
                        unavailableReason = (string?)null,
                        windowDays = (int?)anchor.WindowDays,
                        observed = (int?)anchor.Observed,
                        fixedFindings = (int?)anchor.Fixed,
                        rate = anchor.Rate,
                        excludedFileDeleted = (int?)anchor.ExcludedFileDeleted,
                        unobservable = (int?)anchor.Unobservable,

                        // ★★ Promoted with it: a finding the crowd called noise that the maintainer then
                        // fixed is evidence the crowd was wrong, from a source independent of every rater.
                        // In a side endpoint it is a curiosity; here it is a check on the rate above it.
                        calledNoiseThenFixed = anchor.CalledNoiseThenFixed.ToArray(),
                    },

                fixRateNote =
                    "An anchor, not a complement — never one minus the noise rate. Valid findings go "
                  + "unfixed for want of time, and worthless ones are 'fixed' by a refactor that touched "
                  + "the line. Read side by side; a sharp disagreement means one of them is wrong.",

                mddNote =
                    "Findings are not independent observations — they cluster by repository, so power is "
                  + "computed from the repository count with a design effect, not from the finding count. "
                  + "Treating 2,000 correlated findings as 2,000 observations is how a two-point move gets "
                  + "published as progress when it is a statement about which repositories were drawn.",
            });
        })
        .AllowAnonymous()
        .WithName("NoisePublication");

        // ★★ THE COUNTERWEIGHT TO THE NOISE RATE. Precision alone rewards under-firing: a tool reporting
        // one finding it is certain about scores a perfect 0%, and a tool reporting everything worth
        // knowing scores worse. There is no ground truth on real repositories, so the reference is the
        // union of what participating tools reported and a human adjudicated valid — which is the
        // standard's strongest reason to exist, because no single vendor can build it alone.
        endpoints.MapPost("/api/noise/pooled", (PooledRecallRequest request) =>
        {
            if (request?.Findings is not { Count: > 0 })
            {
                return Results.BadRequest(new { error = "at least one finding is required" });
            }

            var findings = request.Findings
                .Select(f => new PooledFinding(
                    f.Tool ?? "", f.RepoId ?? "", f.FilePath ?? "", f.Line ?? 0, f.Valid ?? false))
                .ToList();

            var summary = PooledRecall.Compute(
                findings,
                request.LineWindow ?? PooledRecall.DefaultLineWindow,
                request.SilentTools ?? []);

            return Results.Ok(new
            {
                methodVersion = MethodVersion,
                participatingTools = summary.ParticipatingTools,
                unionSize = summary.UnionSize,

                // ★ The matching window travels with the figures it produced — an undeclared tolerance is
                // a knob whoever computes the number can quietly turn.
                lineWindow = summary.LineWindow,

                tools = summary.Tools.Select(t => new
                {
                    tool = t.Tool,
                    reported = t.Reported,
                    valid = t.Valid,
                    matchedUnion = t.MatchedUnion,
                    uniqueContribution = t.UniqueContribution,
                    precision = t.Precision,
                    pooledRecall = t.PooledRecall,
                }),

                // ★★ In the response, not in a document nobody reads.
                caveat = summary.Caveat,
            });
        })
        .AllowAnonymous()
        .WithName("NoisePooledRecall");

        // ★★ The anchor that needs nobody's opinion. Every other number here rests on a judgement; this
        // one rests on commits, and no amount of shared bias among raters can move it.
        endpoints.MapPost("/api/noise/fixrate", (FixRateRequest request) =>
        {
            if (request?.Observations is not { Count: > 0 })
            {
                return Results.BadRequest(new { error = "at least one observation is required" });
            }

            if (request.WindowDays is not > 0)
            {
                return Results.BadRequest(new
                {
                    error = "a window in days is required — \"60% of findings get fixed\" without a period "
                          + "is unfalsifiable, because over a long enough window nearly all code changes "
                          + "and the number converges on the churn rate.",
                });
            }

            List<FixObservation> observations = [];
            foreach (var o in request.Observations)
            {
                if (ParseOutcome(o.Outcome) is not { } outcome)
                {
                    return Results.BadRequest(new
                    {
                        error = $"unrecognised outcome '{o.Outcome}'",
                        outcomes = new[] { "cited-location-changed", "unchanged", "file-deleted", "not-observable" },
                    });
                }

                observations.Add(new FixObservation(
                    o.FindingId ?? "", o.RepoId ?? "", outcome, NoiseVerdicts.ParseOrNull(o.CrowdVerdict)));
            }

            var summary = FixRateAnchor.Compute(observations, request.WindowDays.Value);

            return Results.Ok(new
            {
                methodVersion = MethodVersion,
                windowDays = summary.WindowDays,
                observed = summary.Observed,
                fixedFindings = summary.Fixed,
                rate = summary.Rate,
                minimumObservations = FixRateAnchor.MinimumObservations,

                // ★ Both exclusions are published. A deleted file is not a fix — counting it hands a
                // flattering rate to any repository mid-refactor — and a vanished repository is
                // unobservable rather than unfixed.
                excludedFileDeleted = summary.ExcludedFileDeleted,
                unobservable = summary.Unobservable,

                // ★★ The contradiction is the point: a finding the crowd called noise that the maintainer
                // then fixed is evidence the crowd was wrong, from a source independent of every rater.
                // Each is a candidate honeypot — an upstream fix is exactly the earned source they need.
                calledNoiseThenFixed = summary.CalledNoiseThenFixed,

                note = "an anchor, not a complement. The fix rate is not one minus the noise rate: valid "
                     + "findings go unfixed for want of time, and worthless ones are 'fixed' by a refactor "
                     + "that touched the line. Read them side by side; a sharp disagreement means one is wrong.",
            });
        })
        .AllowAnonymous()
        .WithName("NoiseFixRate");

        endpoints.MapGet("/api/noise/crowd/calibration/{period}", (string period) =>
        {
            if (CrowdQueues.Find(period) is not { } round)
            {
                return Results.NotFound(new { error = $"no crowd queue is registered for period '{period}'" });
            }

            var scores = RaterCalibration.Score([.. round.Answers], [.. round.Honeypots.Values]);

            return Results.Ok(new
            {
                methodVersion = MethodVersion,
                period,
                minimumSample = RaterCalibration.MinimumSample,
                planted = round.Honeypots.Count,

                // ★ The COUNT travels with the figure. A reader who wants to discount four-of-five can; a
                // reader shown only "80%" cannot. Uncalibrated raters are listed too — leaving them out
                // would make the published list look like the whole crowd.
                raters = scores.Select(s => new
                {
                    raterId = s.RaterId,
                    answered = s.Answered,
                    agreed = s.Agreed,
                    accuracy = s.Accuracy,
                    calibrated = s.Calibrated,
                }),

                // ★★ Stated on the surface itself, because the temptation is specific and strong: a poor
                // score never removes that rater's answers. Dropping them selects on the outcome and
                // leaves the subset that agreed — a cleaner number that means less.
                note = "scores are published, never applied. No answer is dropped for a poor score: "
                     + "excluding raters by the variable being measured is selection on the outcome.",
            });
        })
        .AllowAnonymous()
        .WithName("NoiseCrowdCalibration");

        endpoints.MapGet("/api/noise/crowd/results/{period}", (string period) =>
        {
            if (CrowdQueues.Find(period) is not { } round)
            {
                return Results.NotFound(new { error = $"no crowd queue is registered for period '{period}'" });
            }

            var byFinding = round.Queue.ToDictionary(i => i.FindingId, i => i.Reason, StringComparer.OrdinalIgnoreCase);

            // ★★ Honeypot answers leave the measurement they calibrate. Their answer was known before it
            // was asked, so counting them would measure the mixture of honeypots that happened to be
            // planted rather than anything about the tool.
            var measured = RaterCalibration.ExcludeHoneypots([.. round.Answers], [.. round.Honeypots.Values]);

            // ★★ REPORTED SEPARATELY, never merged. The contested items are hard BY CONSTRUCTION and the
            // accepted ones are the pipeline's own claim; averaging them hides exactly the disagreement
            // rate on auto-accepted findings that the layer exists to measure. There is deliberately no
            // combined figure — one would be quoted.
            return Results.Ok(new
            {
                methodVersion = MethodVersion,
                period,
                contested = Slice(CrowdReason.Contested),
                spotCheck = Slice(CrowdReason.SpotCheck),

                // ★ Its own slice, so the calibration work is visible rather than invisible. A round that
                // spent a third of its questions on honeypots and one that spent none look identical from
                // the measured slices alone.
                honeypots = new
                {
                    planted = round.Honeypots.Count,
                    answered = round.Answers.Count(a => round.Honeypots.ContainsKey(a.FindingId)),
                },

                // ★★ Published beside the figures, never as a footnote elsewhere. A reader who cannot see
                // that four fifths of the answers came from one language, or from the vendor, is reading
                // an agreement rate as if it measured truth.
                composition = Composition(),
            });

            object Composition()
            {
                var c = CrowdStratification.Summarise([.. measured], [.. round.Strata.Values]);
                return new
                {
                    answers = c.Answers,
                    independent = c.Independent,
                    vendorAffiliated = c.VendorAffiliated,

                    // ★★ Counted apart. A cohort granted a paid tier for answering is compensated by the
                    // vendor, and counting them as independent would let a vendor manufacture its own
                    // independent bucket — the most valuable number on the page and the cheapest to fake.
                    compensated = c.Compensated,

                    // ★ Undeclared is its own bucket. Counting it as independence lets the most
                    // interesting bias in the pool hide in a default.
                    undeclared = c.Undeclared,
                    largestLanguage = c.LargestLanguage,
                    largestLanguageShare = c.LargestLanguageShare,
                    dominated = c.Dominated,
                    byLanguage = c.ByLanguage,
                };
            }

            object Slice(CrowdReason reason)
            {
                var queued = round.Queue.Count(i =>
                    i.Reason == reason && !round.Honeypots.ContainsKey(i.FindingId));
                var answers = measured
                    .Where(a => byFinding.TryGetValue(a.FindingId, out var r) && r == reason)
                    .ToList();

                return new
                {
                    queued,
                    answered = answers.Count,

                    // ★ A contradiction is the whole point of the spot-check: four models agreed, and a
                    // person outside the family says otherwise.
                    contradicted = answers.Count(a =>
                        a.MachineVerdict is { } m && a.Verdict.IsNoise() != m.IsNoise()),

                    // ★ Answers with no machine verdict to compare against are counted HERE rather than
                    // as agreement — otherwise omitting one field hides every disagreement.
                    notComparable = answers.Count(a => a.MachineVerdict is null),
                };
            }
        })
        .AllowAnonymous()
        .WithName("NoiseCrowdResults");
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parse an affiliation, or null when it is not one of the three.
    /// </summary>
    /// <remarks>
    /// ★ There is deliberately no "unknown" to declare. Undeclared is a state the store can be IN — a
    /// rater who never said — but not a claim anyone may make, or the conflict of interest becomes
    /// something a vendor can assert its way out of.
    /// </remarks>
    private static RaterAffiliation? ParseAffiliation(string? affiliation) =>
        affiliation?.Trim().ToLowerInvariant() switch
        {
            "independent" => RaterAffiliation.Independent,
            "vendor-employed" => RaterAffiliation.VendorEmployed,
            "vendor-contracted" => RaterAffiliation.VendorContracted,

            // ★★ A rater earning the vendor's product by answering. Compensated in kind rather than in
            // cash — which changes the accounting and not the incentive.
            "compensated-in-product" => RaterAffiliation.CompensatedInProduct,
            _ => null,
        };

    private static FixOutcome? ParseOutcome(string? outcome) => outcome?.Trim().ToLowerInvariant() switch
    {
        "cited-location-changed" => FixOutcome.CitedLocationChanged,
        "unchanged" => FixOutcome.Unchanged,
        "file-deleted" => FixOutcome.FileDeleted,
        "not-observable" => FixOutcome.NotObservable,
        _ => null,
    };

    /// <summary>
    /// A run being published.
    /// </summary>
    /// <param name="Clusters">
    /// ★★ Repositories, not findings. Required, because computing power from the finding count treats
    /// correlated findings as independent observations and understates the detectable difference.
    /// </param>
    /// <param name="PreviousRate">The last published rate, when there is one to compare against.</param>
    /// <param name="FixRateUnavailable">
    /// ★ Why the anchor is missing, when it is. A first cycle genuinely has no window yet, and refusing
    /// that outright would push everyone towards inventing observations — so the reason publishes, and the
    /// absence becomes one a reader can weigh instead of one they cannot see.
    /// </param>
    public sealed record PublicationRequest(
        int Reported, int Adjudicated, int Excluded, int Unrated,
        int ValidAndActionable, int ValidNotActionable, int Noise,
        int Clusters, double? PreviousRate,
        int? FixRateWindowDays,
        IReadOnlyList<FixObservationEntry>? FixRateObservations,
        string? FixRateUnavailable,
        // ★★ The fields /api/noise/method has always listed as requiredWithEveryRate, and which this record
        // had nowhere to put — so the contract was published and unenforceable. See PublicationContract.
        long? LocCovered = null,
        double? RecallEstimate = null,
        string? RecallMethod = null,
        string? RecallNote = null,
        IReadOnlyList<ClaimClassEntry>? ClaimClasses = null,
        IReadOnlyList<RecencyEntry>? RecencyStrata = null,
        string? ToolVersion = null,
        string? HoldoutSeed = null,
        string? ModelSet = null,
        bool? GitMiningVerified = null,
        int? GapsFoundSinceLastPeriod = null);

    /// <summary>One claim class's share of the run, as it arrives on the wire.</summary>
    public sealed record ClaimClassEntry(string? ClaimClass, int Judged, int Noise);

    /// <summary>One recency stratum's share of the run, as it arrives on the wire.</summary>
    public sealed record RecencyEntry(string? Stratum, int Judged, int Noise);

    /// <summary>A count per 100k LoC, or null without a denominator.</summary>
    private static double? Per100k(int count, long? loc) =>
        loc is > 0 ? count * 100_000d / loc.Value : null;

    /// <summary>One tool's finding and its adjudication, as it arrives on the wire.</summary>
    public sealed record PooledFindingEntry(
        string? Tool, string? RepoId, string? FilePath, int? Line, bool? Valid);

    /// <summary>
    /// The pooled reference to build.
    /// </summary>
    /// <param name="SilentTools">
    /// ★ Tools that submitted a run and reported nothing. Named explicitly, or they vanish from the table
    /// and a tool that found nothing looks identical to one that never entered.
    /// </param>
    public sealed record PooledRecallRequest(
        int? LineWindow, IReadOnlyList<string>? SilentTools, IReadOnlyList<PooledFindingEntry>? Findings);

    /// <summary>What a rater declares about themselves, as it arrives on the wire.</summary>
    public sealed record RaterDeclarationRequest(
        string? Period, string? RaterId, string? PrimaryLanguage, string? Affiliation);

    /// <summary>One finding's fate in the repository, as the history reports it.</summary>
    public sealed record FixObservationEntry(
        string? FindingId, string? RepoId, string? Outcome, string? CrowdVerdict);

    /// <summary>A fix-rate calculation over a declared window.</summary>
    public sealed record FixRateRequest(int? WindowDays, IReadOnlyList<FixObservationEntry>? Observations);

    /// <summary>A finding the cascade has finished with, as it arrives on the wire.</summary>
    public sealed record CrowdCandidateRequest(string? FindingId, string? State, string? OwnerId);

    /// <summary>A period's crowd queue, as a participant registers it.</summary>
    public sealed record CrowdQueueRequest(
        string? Period, string? Seed, int SpotCheck, IReadOnlyList<CrowdCandidateRequest>? Candidates);

    /// <summary>A honeypot as it arrives on the wire.</summary>
    public sealed record HoneypotEntry(string? FindingId, string? Truth, string? Source, string? Evidence);

    /// <summary>Honeypots to plant into a period's queue.</summary>
    public sealed record HoneypotRequest(string? Period, IReadOnlyList<HoneypotEntry>? Honeypots);

    /// <summary>One person's answer to one finding.</summary>
    public sealed record CrowdAnswerRequest(
        string? Period, string? RaterId, string? FindingId, string? Verdict, string? MachineVerdict);

    /// <summary>One judge's vote as it arrives on the wire.</summary>
    /// <param name="Judge">The judge slot, e.g. <c>judge-a</c>.</param>
    /// <param name="Verdict">One of the six published verdicts.</param>
    /// <param name="Model">Which model answered. Required to RECORD a verdict.</param>
    /// <param name="ModelVersion">Its pinned version — without it the run cannot be re-derived.</param>
    /// <param name="PromptId">The prompt used; its full text is published beside the record.</param>
    /// <param name="Prompt">The prompt's text, registered once under its id on first use.</param>
    /// <param name="Reasoning">
    /// ★★ WHY. A verdict a reader cannot argue with is not open judging — 01 promises "a reader who disagrees
    /// with a verdict must be able to find it, read the reasoning, and say so".
    /// </param>
    public sealed record CascadeVote(
        string? Judge, string? Verdict,
        string? Model = null, string? ModelVersion = null,
        string? PromptId = null, string? Prompt = null, string? Reasoning = null);

    /// <summary>
    /// The votes to resolve. Round two is absent until round one has actually split — sending both at
    /// once would mean the second pair had been convened before there was anything to convene them for.
    /// </summary>
    /// <param name="Period">
    /// ★★ Supplying this AND <paramref name="FindingId"/> makes the call a RECORDED judgement rather than a
    /// calculation. Without them the endpoint stays a pure resolver, which is what the cascade's own unit
    /// tests use — but a real judging run must record, or the standard's open-judging promise has nothing
    /// behind it.
    /// </param>
    /// <param name="FindingId">Which finding was judged — the join a reader follows to argue with a verdict.</param>
    public sealed record CascadeRequest(
        IReadOnlyList<CascadeVote>? Round1, IReadOnlyList<CascadeVote>? Round2,
        string? Period = null, string? FindingId = null);

    /// <summary>The receipt as it publishes — what was checked, and what it found.</summary>
    private static object Render(SubmissionReceipt r) => new
    {
        submissionId = r.SubmissionId,
        period = r.Period,
        tool = r.Tool,
        toolVersion = r.ToolVersion,
        receivedAt = r.ReceivedAt,
        methodVersion = MethodVersion,
        samplerVersion = NoiseCorpus.SamplerVersion,
        accepted = r.Accepted,

        // ★★ "Accepted" must not be readable as "complete". A run covering two of twelve repositories
        // is well-formed and is accepted — refusing it outright would push a vendor whose tool genuinely
        // lacks a language into not participating at all. But a receipt saying only accepted:true can be
        // quoted as a clean bill by somebody who scanned a sixth of the holdout, so completeness is its
        // own flag and partial coverage says so in words.
        complete = r.Accepted && r.Uncovered.Count == 0,
        completenessNote = r.Uncovered.Count == 0
            ? "full coverage: every drawn repository appears in this run."
            : $"PARTIAL coverage: {r.CoveredRepositories} of {r.HoldoutRepositories} drawn repositories "
              + "appear in this run. A rate computed over a subset is not comparable with one computed "
              + "over the whole holdout.",

        problems = r.Problems,

        // ★ Coverage publishes whether or not it is complete — "zero uncovered" is a stated fact rather
        // than the absence of a complaint, and partial coverage is the most obvious route to a
        // flattering number.
        coverage = new
        {
            holdoutRepositories = r.HoldoutRepositories,
            coveredRepositories = r.CoveredRepositories,
            uncovered = r.Uncovered,
        },
    };
}
