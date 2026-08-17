using System.Text.Json;
using System.Text.Json.Nodes;

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

        endpoints.MapGet("/api/noise/method", (string? period) =>
        {
            // ★★ Asked about a PERIOD, answer with the version that governed it — not the newest. A reader
            // re-deriving an old number has to be able to find the rules it was judged under, or "versioned"
            // means no more than "we will tell you what the rules are now".
            if (period is { Length: > 0 })
            {
                var inForce = MethodVersions.InForceFor(period);
                return Results.Ok(new
                {
                    period,
                    version = inForce?.Version,
                    announcedAt = inForce?.AnnouncedAt,
                    effectiveFromPeriod = inForce?.EffectiveFromPeriod,
                    rationale = inForce?.Rationale,
                    changeControlRule = MethodVersions.Rule,

                    // ★ Null, never the earliest version: claiming a version governed a period that predates
                    // it is the same retroactive application the rule forbids, pointing the other way.
                    note = inForce is null
                        ? $"no method version was in force for {period} — the first version takes effect from "
                          + MethodVersions.History[0].EffectiveFromPeriod + "."
                        : null,
                });
            }

            return Results.Ok(new
        {
            version = MethodVersion,

            // ★★ THE WHOLE HISTORY, not just the current version. A reader judging whether a change was
            // self-serving needs when it was announced, which period it first applied to, and why — for every
            // version, because the interesting one is always the one before a number somebody disliked.
            versions = MethodVersions.History.Select(v => new
            {
                version = v.Version,
                announcedAt = v.AnnouncedAt,
                effectiveFromPeriod = v.EffectiveFromPeriod,
                rationale = v.Rationale,
            }),

            // ★★ The answer to "who can change this, and when". With no governance body in phase 1 the honest
            // answer was "Watchdog, unilaterally, at any time"; this is the constraint that replaces a meeting.
            versionTakesEffectFromNextHoldout = true,
            changeControlRule = MethodVersions.Rule,

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

            // ★★ HOW POOLED RECALL IS COMPUTED, published because it is the most attackable line the method
            // could contain. Scoring a tool against a union INCLUDING its own findings gives the tool that
            // alone found everything a perfect 100 % against a reference it wrote — and with one participant
            // that reference is ours, so "depth" would have meant "agrees with Watchdog".
            pooledRecall = new
            {
                reference = "leave-one-out: the union of what every OTHER participating tool reported and a "
                          + "human adjudicated as valid. A tool is never scored against its own findings.",
                minimumTools = PooledRecall.MinimumTools,
                belowMinimum = "refused, not computed. At two tools the leave-one-out reference is one other "
                             + "tool's findings, so the figure is pairwise agreement and a tool scores well by "
                             + "being SIMILAR rather than deep.",
                pseudoOracle = true,
                scope = PooledRecall.PooledScope,
                lineWindow = PooledRecall.DefaultLineWindow,
                caveat = PooledRecall.PooledCaveat,
                endpoint = "/api/noise/pooled",
            },

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

            // ★★ THE RE-JUDGE, published with its numbers rather than described as "a sample". The sample size,
            // the ceiling, WHY the ceiling is there, and what agreement is measured on — a reader would
            // otherwise assume class-level agreement, which would manufacture instability out of vocabulary.
            rejudge = new
            {
                sampleSize = Rejudge.DefaultSampleSize,
                tolerance = Rejudge.Tolerance,
                toleranceRationale = Rejudge.ToleranceRationale,
                fold = Rejudge.Fold,
                sampleRule =
                    "The sample is drawn from the period's published holdout seed, so a third party can "
                  + "re-derive it from values published before any result existed. A second pass may only "
                  + "answer findings in that sample, and every sampled finding it leaves unanswered blocks the "
                  + "tolerance — otherwise re-judging until the sample agrees is a rate over the agreeing part.",
                endpoint = "/api/noise/rejudge/{period}",
            },

            // ★★ WHAT VERIFICATION ACTUALLY CHECKS, enumerated. "Verified" is the only thing CAI does and the
            // whole neutrality argument, and a reader had no way to find out what it covered — so a submission
            // that passed the easy checks and skipped the hard ones read exactly like one that passed them all.
            verificationChecks = new object[]
            {
                new
                {
                    check = "holdout-membership",
                    asks = "is every finding on a repository this period's draw published?",
                },
                new
                {
                    check = "pinned-sha",
                    asks = "does each finding cite the revision the holdout pinned? A different revision is "
                         + "different code.",
                },
                new
                {
                    check = "run-ordering",
                    asks = "did the run START after the draw was published? A result produced before its own "
                         + "holdout answers something else.",
                },
                new
                {
                    check = "claim-class",
                    asks = "does each finding declare one of the published claim classes?",
                },
                new
                {
                    check = "recency-declaration",
                    asks = "for each drawn repository, has the tool been developed against it?",
                },
                new
                {
                    check = "configuration-declaration",
                    asks = "which ruleset ran, and is it what customers get?",
                },
                new
                {
                    // ★★ The one added by #7a, and the reason the list exists at all.
                    check = "finding-count",
                    asks = "does the number of findings submitted equal the number the run reports it "
                         + "produced? Dropping findings between the run and the submission is the simplest "
                         + "route to a flattering rate, and coverage cannot show it — a repository with one "
                         + "surviving finding is covered.",
                },
                new
                {
                    // ★★ Added by #7b, and the only check here that points at the standard rather than at a
                    // vendor: a rate produced by a process that disagrees with itself is not a measurement.
                    check = "rejudge",
                    asks = "does an independent second pass over a seed-drawn sample of the period's judged "
                         + "findings reach the same noise/not-noise answers, within the published tolerance?",
                },
                new
                {
                    check = "coverage",
                    asks = "which drawn repositories does the run not reach? Reported rather than refused, "
                         + "and a partial run is marked partial.",
                },
            },

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

            // ★★ THE COMPLIANCE MARK (#4), published as conditions rather than as a badge. Mechanical or it is
            // an unreviewable veto held by a participant over its rivals.
            complianceMark = new
            {
                label = ComplianceMark.Label,
                free = ComplianceMark.Free,
                changeRule = ComplianceMark.ChangeRule,
                wordingRule = ComplianceMark.WordingRule,
                appealRoute = ComplianceMark.AppealRoute,
                conditions = ComplianceMark.Conditions.Select(c => new { c.Name, c.Reads }),
                published = "/api/noise/mark/{period}",
                isNotAQualityBadge = "Three of the four conditions are about PROCESS. A tool with a poor noise "
                                   + "rate that ran the published draw, submitted in time and published in full "
                                   + "earns the mark — it states that the measurement happened properly, and the "
                                   + "rate states how it went.",
            },

            // ★★ THE INTENT REGISTER (#14). The no-withdrawal refusal told vendors to "register intent before the
            // next draw instead" and there was nowhere to do it — a rule whose remedy does not exist reads as an
            // excuse.
            intentRegister = new
            {
                endpoint = "/api/noise/intent",
                published = "/api/noise/intent/{period}",
                closesWhen = "the period's holdout is drawn. Intent registered after the draw is a decision made "
                           + "with the sample in hand, so it must be declared BEFORE — that ordering is the whole "
                           + "value of the register.",
                why = "The no-withdrawal rule cannot see a run that was never submitted: a vendor can simply skip "
                    + "the periods that went badly, and the published set quietly becomes 'the results people "
                    + "were happy with'. The register names who said they would take part, so not submitting is "
                    + "visible.",
                idempotent = "registering twice keeps the FIRST timestamp — the moment it was made is the claim.",
            },

            // ★★ THE TWO BEHAVIOURAL QUESTIONS (#13), verbatim. Two clients asking "would you fix this?" and
            // "is this worth fixing?" are asking different questions, and the answers stop being comparable.
            behaviouralQuestions = new
            {
                wouldFix = BehaviouralQuestions.WouldFix,
                wantInReport = BehaviouralQuestions.WantInReport,
                why = BehaviouralQuestions.Why,
                relationToTheRate = BehaviouralQuestions.RelationToTheRate,
                unansweredIsNotNo = "a missing behavioural answer is counted as NOT ASKED. Folding it into 'no' "
                                  + "would manufacture evidence that practitioners would not act on findings "
                                  + "nobody asked them about.",
            },

            // ★★ THE PANEL'S SHAPE (#10). The cascade recorded whatever it was handed, so "four judges agreed"
            // could have been one model counted four times — and 02 §2 is explicit that a single-family ensemble
            // cannot see a single-family blind spot.
            judgePanel = new
            {
                distinctModels = JudgePanel.FullPanelDistinctModels,
                rule = "no model may appear twice in a panel, and the panel must span at least two families. A "
                     + "round-one settle is therefore two distinct models from two traditions; a round-two panel "
                     + "is four. Requiring four to RECORD would contradict the cascade — round two convenes only "
                     + "when round one has split, so most findings could never be recorded at all.",
                familiesRequired = JudgePanel.RequiredFamilies,
                temperature = JudgePanel.RequiredTemperature,
                declaredPerVerdict = new[] { "model", "modelVersion", "modelFamily", "temperature" },
                why = JudgePanel.Why,
                undeclaredIsNotAPass = "an undeclared family does not count as a different family, and an "
                                     + "undeclared temperature does not count as 0. Either default would let a "
                                     + "panel pass by omitting the field.",
                appliesTo = "RECORDING a judgement. /api/noise/cascade/resolve still resolves votes as a "
                          + "calculation; what it refuses is to record a judgement from a panel that breaks the "
                          + "method.",
            },

            // ★★ THE PERMANENTLY PRISTINE SLICE. 02 §1: without an endpoint the decay curve measures nothing.
            reservedSlice = ReservedSliceRule(),

            // ★★ THE CONTESTATION ROUTE, published — a right nobody can find is not one. 01 §5 promises it and
            // nothing said where to go.
            contestation = new
            {
                endpoint = "/api/noise/verdicts/{findingId}/dispute",
                resolveEndpoint = "/api/noise/disputes/{disputeId}/resolve",
                requires = "the period, and a REASON. An unexplained objection cannot be argued with in either "
                         + "direction, and the reason publishes with the dispute.",
                publishes = "either way. Upheld and overturned appear identically in the period's record, with "
                          + "the resolution's reasoning — a dispute that only appeared when the vendor won "
                          + "would be a complaints box.",
                rawVerdictIsKept = "always. The verdict register is append-only and nothing here deletes from "
                                 + "it: a contestation mechanism that removed what it overturned would be a "
                                 + "withdrawal mechanism, and the register would become 'the verdicts nobody "
                                 + "objected to'.",
                effectOnAPublishedRate = "an overturned verdict does not silently change a published number. "
                                       + "The rate is corrected by publishing the period again, which the "
                                       + "append-only publication record shows as a correction.",
            },

            // ★★ THE ROLLING FIGURE, published as a rule rather than as a habit. 02 §5 lists it as required
            // with every rate and nothing computed one.
            twelveMonth = new
            {
                windowMonths = RollingFigure.WindowMonths,
                pooledFrom = "the published results for the window ending at the period, from the append-only "
                           + "publication record, plus the period being published. A corrected period counts "
                           + "ONCE, at its latest value.",
                why = "A single period's interval is wide enough to hide most movements, and the minimum "
                    + "detectable difference computed over repositories is wider still — so month-to-month "
                    + "comparison is mostly noise about noise. The rolling figure is the only rate here whose "
                    + "interval can support a claim about a trend.",
                shortWindowRule = "A window covering fewer than the full twelve periods is published as such: "
                                + "it is a real pooled rate and it is not yet a twelve-month figure. The two "
                                + "look identical, so `spansTheFullWindow` is what separates them.",
                interval = "wilson-95",
            },

            // ★★ AND NOW THE RULE HAS AN IMPLEMENTATION BEHIND IT. reportingRule above required both averages
            // while the publication carried only a COUNT of clusters, which cannot produce a cluster-weighted
            // anything — a rule published and unimplementable at the same time.
            clusterAverages = new
            {
                requires = "clusterTallies: one entry per repository with its judged and noise counts, "
                         + "optionally per claim class. A count of clusters is enough for the clustering "
                         + "interval and cannot produce a cluster-weighted average.",
                why = "The pooled rate is a count over a count, so a repository contributing half the findings "
                    + "contributes half the rate and can dominate the number unseen. The remedy is NOT to drop "
                    + "the outlier — excluding a repository for having an extreme rate is selecting on the "
                    + "outcome — it is a second average that weights repositories equally, published beside "
                    + "the first.",
                notableDivergence = ClusterAverages.NotableDivergence,
                divergenceMeans = "a run to READ twice, never a run to void: neither average is the wrong "
                                + "answer, and which one to quote depends on the question asked.",
                emptyClusters = "a repository the run reached and judged nothing in has NO rate and is "
                              + "excluded from the macro. Counting it as 0 % would improve the number for "
                              + "going unjudged.",
                talliesMustMatchTheCensus = true,
            },
            });
        })
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
        endpoints.MapGet("/api/noise/record/{period}", (string period, INoiseStore store, HttpContext http) =>
        {
            // ★★ THE EMBARGO (#15). 03 commits to it as one of the four things that make the standard's conflict
            // of interest survivable, and this endpoint served everything to everyone immediately — early sight of
            // a rival's result being the single most valuable thing Watchdog's position could be worth. The lift
            // date is in the SIGNED manifest, and there is no caller name with a different answer.
            // ★★ THE EMBARGO APPLIES TO A DRAWN PERIOD. A period with no draw is not one the standard measures —
            // a submission against it is refused outright ("no holdout has been published for that period"), so no
            // participant's material can exist there to protect. The fail-closed rule is about a DRAWN period
            // whose entry has no date: that is the leak case, and it is embargoed.
            var drawn = NoiseCorpus.Draws.TryGetValue(period, out var periodDraw);
            var publishesAt = drawn ? periodDraw.PublishesAt : null;
            var caller = http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name : null;

            var embargoed = drawn && Embargo.IsInForce(publishesAt, DateTimeOffset.UtcNow);

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

                // ★★ DISPUTES, BESIDE THE VERDICTS THEY CONTEST. An open one is the state a reader most needs
                // to see: it is where the standard has been challenged and has not answered, and absent from the
                // record "no disputes" and "three we have not got round to" look identical.
                disputes = RenderDisputes(store, period),

                // ★★ THE SECOND PASS, RAW, beside the first. A reproducibility claim is worth exactly what its
                // evidence is: a reader who doubts "the judging reproduces" must be able to read both answers
                // and the reasoning behind each, and decide for themselves which one was wrong.
                rejudge = RenderRejudge(store, period),

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

                    // ★★ The panel's shape, per verdict — a requirement whose inputs are not published cannot be
                    // checked by the reader it exists for.
                    modelFamily = v.ModelFamily,
                    temperature = v.Temperature,

                    promptId = v.PromptId,
                    verdict = v.Verdict,
                    reasoning = v.Reasoning,
                    recordedAt = v.RecordedAt,
                }),

                // ★★ THE EMBARGO APPLIES HERE, and only here (#15). The register is the one part of this record
                // that is attributable to a PARTICIPANT: it names each tool, when it submitted and whether the
                // run was accepted. The judging beside it — verdicts, resolutions, disputes — is CAI's own
                // material about findings and carries no tool at all, so it cannot be read as anybody's result.
                // Before the lift a caller sees only its own entries, Watchdog included; there is no caller name
                // with a different answer.
                embargo = new
                {
                    inForce = embargoed,
                    publishesAt,
                    readingAs = caller,
                    note = embargoed
                        ? Embargo.Note(publishesAt)
                        : "This period has published: the register is open to everyone.",
                },

                // ★ The register belongs here too: who submitted, when, and whether it was accepted —
                // including the runs that were refused. A register of only the accepted ones is a register
                // that has been edited.
                submissions = submissions
                    // ★★ FILTERED UNDER EMBARGO — see the `embargo` block above. Withheld rather than
                    // anonymised: a register showing "three tools submitted, two accepted" with the names
                    // removed still tells a rival what the field did before their own result published.
                    .Where(r => !embargoed || Embargo.MayRead(caller, r.Tool, publishesAt, DateTimeOffset.UtcNow))
                    .Select(r => new
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
            // ★★ FAIL CLOSED. An unverifiable corpus serves NO draws rather than serving them unsigned: a
            // holdout endpoint that quietly degrades to "here is the pool, unsigned" is worse than one that
            // stops, because the degradation is invisible in the thing it hands back — and a draw from a pool
            // nobody can check is not a draw. 503, because the fault is ours and it is fixable.
            if (CorpusUnverifiable() is { } unverifiable)
            {
                return unverifiable;
            }

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

                // ★★ WHICH MANIFEST THIS DRAW CAME FROM, and the signature over it. 01 §2 asks for the draw
                // "timestamped AND signed"; the timestamp was here and the signature was our word.
                manifest = ManifestIdentity(),

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

                    // ★★ A vendor cannot honour a reservation it cannot see. Published with the draw, and
                    // declaring one of these as trained is refused at submission.
                    reserved = r.Reserved,
                }),

                // ★ The reservation as a rule, beside the repositories it applies to.
                reservedSlice = ReservedSliceRule(),
            });
        })
        .AllowAnonymous()
        .WithName("NoiseHoldout");

        // ★ The pool publishes too. A third party re-deriving a draw needs the seed AND the candidates
        // it was drawn from — publishing only the seed proves nothing, because the pool could have been
        // chosen after the fact.
        endpoints.MapGet("/api/noise/corpus", () =>
        {
            // ★★ Fail closed here too — an unverifiable pool must not be served as though it were the published
            // one, and this is the endpoint a third party fetches to check a draw against.
            if (CorpusUnverifiable() is { } unverifiable)
            {
                return unverifiable;
            }

            return Results.Ok(new
        {
            samplerVersion = NoiseCorpus.SamplerVersion,

            // ★★ The manifest identity and how to check it — beside the pool, not in a document elsewhere.
            manifest = ManifestIdentity(),
            howToVerify = CorpusManifest.VerificationInstructions,

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
                    reserved = c.Reserved,
                }),
            reservedSlice = ReservedSliceRule(),
        });
        })
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
                          + "withdrawn or replaced. Register intent for a period whose holdout has not been "
                          + "drawn yet, at POST /api/noise/intent — it publishes, and it is what makes "
                          + "not submitting visible.",
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
                          + "withdrawn or replaced. Register intent for a period whose holdout has not been "
                          + "drawn yet, at POST /api/noise/intent — it publishes, and it is what makes "
                          + "not submitting visible.",
                });
            }

            return Results.Ok(Render(receipt));
        })
        .AllowAnonymous()
        .WithName("NoiseSubmit");

        endpoints.MapGet("/api/noise/submissions/{submissionId}", (string submissionId, INoiseStore store) =>
        {
            var receipt = store.FindSubmission(submissionId);
            if (receipt is null)
            {
                return Results.NotFound(new { submissionId, error = "no such submission" });
            }

            // ★★ THE CONFIGURATION BELONGS ON THE RECEIPT, found by the embargo (#15). It was published only
            // through the period record's register — which is embargoed until the period publishes — so a
            // participant could not read back its OWN declaration at all until then. The receipt is fetched by an
            // id only the submitter holds, which makes it the right place for it.
            var node = JsonSerializer.SerializeToNode(
                Render(receipt), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
            node["configuration"] = store.ConfigurationJson(receipt.SubmissionId) is { Length: > 0 } json
                ? JsonNode.Parse(json)
                : null;

            return Results.Json(node);
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

                // ★★ THE PANEL'S SHAPE (#10), checked once over both rounds. The cascade recorded whatever it was
                // handed: four votes from one model under four judge names would have been stored as a judgement,
                // and the record would have shown four agreeing judges where there was one opinion counted four
                // times. See JudgePanel — and note it constrains RECORDING, never the arithmetic below.
                unrecordable.AddRange(JudgePanel.Problems(
                    [.. all.Select(x => new JudgePanel.Declaration(
                        x.Vote.Judge ?? "unnamed", x.Vote.Model, x.Vote.ModelFamily, x.Vote.Temperature))]));

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
                            vote.Reasoning!, now,

                            // ★ Both travel with the raw verdict, like the model version already does: a
                            // requirement whose inputs are not published cannot be checked by the reader it
                            // exists for. Non-null here because JudgePanel.Problems refused otherwise.
                            vote.ModelFamily!, vote.Temperature!.Value));
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
            return Results.Ok(new
            {
                findingId = item.FindingId,

                // ★★ THE QUESTIONS TRAVEL WITH THE ITEM (#13). Without them every client invents its own wording,
                // and "would you fix this?" against "is this worth fixing?" are different questions whose answers
                // are not comparable. Still nothing about what the judges said — that disguise is what the
                // spot-check depends on.
                questions = new
                {
                    wouldFix = BehaviouralQuestions.WouldFix,
                    wantInReport = BehaviouralQuestions.WantInReport,
                },
            });
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
                request.FindingId!, request.RaterId!, verdict, NoiseVerdicts.ParseOrNull(request.MachineVerdict),

                // ★ Carried through as nullable: not asked and "no" are different answers (#13).
                request.WouldFix, request.WantInReport));

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
        // ── The compliance mark ───────────────────────────────────────────────────────────────────
        //
        // ★★ A MARK THAT CAN BE PULLED IS POWER OVER A COMPETITOR'S MARKETING, and pulling one is far more
        // newsworthy than granting one. So it is decided here by arithmetic over facts CAI already holds — no
        // judgement anywhere in the path — and a withheld mark names the condition and the fact it read.
        endpoints.MapGet("/api/noise/mark/{period}", (string period, INoiseStore store) =>
        {
            var marks = MarksFor(store, period);

            return Results.Ok(new
            {
                period,
                label = ComplianceMark.Label,
                free = ComplianceMark.Free,
                changeRule = ComplianceMark.ChangeRule,
                wordingRule = ComplianceMark.WordingRule,
                appealRoute = ComplianceMark.AppealRoute,
                conditions = ComplianceMark.Conditions.Select(c => new { c.Name, c.Reads }),

                deadline = NoiseCorpus.Draws.TryGetValue(period, out var d) ? d.SubmissionsCloseAt : null,

                marks = marks.Select(m => new
                {
                    tool = m.Tool,
                    granted = m.Granted,
                    statement = m.Statement,
                    failing = m.Failing.Select(f => new { condition = f.Condition, why = f.Why }),
                }),

                note = marks.Count == 0
                    ? "no tool has submitted for this period, so there is no mark to state either way."
                    : null,
            });
        })
        .AllowAnonymous()
        .WithName("NoiseComplianceMark");

        // ── The intent register ───────────────────────────────────────────────────────────────────
        //
        // ★★ THE NO-WITHDRAWAL RULE HAD NOWHERE TO SEND ANYBODY. Its refusal already said "register intent before
        // the next draw instead", and there was no endpoint to do it at. Worse, the rule without a before is only
        // half a rule: a vendor can simply never submit the periods that went badly, and the published set quietly
        // becomes "the results people were happy with".
        endpoints.MapPost("/api/noise/intent", (IntentRequest request, INoiseStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Period) || string.IsNullOrWhiteSpace(request.Tool))
            {
                return Results.BadRequest(new
                {
                    error = "registering intent names the period and the tool. Both publish.",
                });
            }

            // ★★ CLOSED ONCE THE HOLDOUT IS DRAWN. Intent registered after seeing the draw is not intent — it is
            // a decision made with the sample in hand, which is the one thing the ordering exists to prevent. The
            // draw date travels with the refusal, so it can be checked against the published draw rather than
            // taken on this endpoint's word.
            if (NoiseCorpus.Draws.TryGetValue(request.Period, out var draw))
            {
                return Results.Conflict(new
                {
                    request.Period,
                    request.Tool,
                    error = $"the holdout for {request.Period} has already been drawn, so intent for it can no "
                          + "longer be registered: a decision made with the sample in hand is not intent. "
                          + "Register for a period whose draw has not been published.",
                    drawnAt = draw.DrawnAt,
                });
            }

            var record = store.RegisterIntent(request.Period, request.Tool, DateTimeOffset.UtcNow);

            return Results.Ok(new
            {
                record.Period,
                record.Tool,
                record.RegisteredAt,
                note = "This registration publishes. A tool that registers and then does not submit is named in "
                     + "the register — which is the point: the no-withdrawal rule cannot catch a run that was "
                     + "never submitted, and this can.",
            });
        })
        .AllowAnonymous()
        .WithName("NoiseIntentRegister");

        endpoints.MapGet("/api/noise/intent/{period}", (string period, INoiseStore store) =>
        {
            var registered = store.ListIntent(period);
            var submitted = store.ListSubmissions(period)
                .Select(r => r.Tool)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // ★★ THE FIGURE THE REGISTER EXISTS FOR. A vendor who registered and then published nothing is the
            // case the no-withdrawal rule cannot catch on its own — they never submitted, so there is nothing to
            // withdraw. Naming them is the entire enforcement mechanism, and it costs a set lookup.
            var missing = registered.Where(r => !submitted.Contains(r.Tool)).Select(r => r.Tool).ToList();

            return Results.Ok(new
            {
                period,
                drawn = NoiseCorpus.Draws.TryGetValue(period, out var draw) ? draw.DrawnAt : (DateTimeOffset?)null,
                open = !NoiseCorpus.Draws.ContainsKey(period),

                registered = registered.Select(r => new
                {
                    tool = r.Tool,
                    registeredAt = r.RegisteredAt,
                    submitted = submitted.Contains(r.Tool),
                }),

                registeredAndDidNotSubmit = missing,

                note = registered.Count == 0
                    ? "nobody has registered intent for this period yet. That is an absence, not a statement "
                    + "about anybody."
                    : missing.Count == 0
                        ? "everybody who registered intent for this period has submitted."
                        : $"{missing.Count} tool(s) registered and did not submit: "
                        + string.Join(", ", missing)
                        + ". The no-withdrawal rule cannot see a run that was never submitted; this can.",
            });
        })
        .AllowAnonymous()
        .WithName("NoiseIntentPeriod");

        // ── Contestation ──────────────────────────────────────────────────────────────────────────
        //
        // ★★ 01 §5: "a vendor who thinks a verdict is wrong can contest it in public, against published
        // reasoning. 'The standard says so' is not an argument CAI gets to make." There was no way to say so —
        // which makes the standard the last word on its own judgements, the position it exists to avoid.
        endpoints.MapPost("/api/noise/verdicts/{findingId}/dispute",
            (string findingId, DisputeRequest request, INoiseStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Period))
            {
                return Results.BadRequest(new { error = "a dispute names the period the verdict was recorded in" });
            }

            // ★★ THE REASON IS THE POINT. "I disagree" is not contestation: 01 §5 is about arguing against
            // published reasoning, and the reason is the half that makes the dispute answerable rather than a vote.
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Results.BadRequest(new
                {
                    error = "a dispute requires a reason. It publishes with the dispute, and it is what makes "
                          + "the contest answerable rather than a vote — an unexplained objection cannot be "
                          + "argued with in either direction.",
                });
            }

            // ★ A dispute about a judgement nobody made would fill the register with noise of its own, and
            // "twelve disputes this period" would stop meaning anything.
            var judged = store.ListResolutions(request.Period)
                .Any(r => string.Equals(r.FindingId, findingId, StringComparison.OrdinalIgnoreCase));
            if (!judged)
            {
                return Results.NotFound(new
                {
                    findingId,
                    request.Period,
                    error = "no verdict has been recorded for that finding in that period, so there is nothing "
                          + "to contest yet.",
                });
            }

            var dispute = new DisputeRecord(
                DisputeId: Guid.CreateVersion7().ToString("n"),
                Period: request.Period,
                FindingId: findingId,
                RaisedBy: request.RaisedBy ?? "unnamed",
                Reason: request.Reason,
                RaisedAt: DateTimeOffset.UtcNow,
                Outcome: null,
                ResolutionReasoning: null,
                ResolvedAt: null);

            store.RaiseDispute(dispute);

            return Results.Ok(RenderDispute(dispute));
        })
        .AllowAnonymous()
        .WithName("NoiseVerdictDispute");

        endpoints.MapPost("/api/noise/disputes/{disputeId}/resolve",
            (string disputeId, DisputeResolutionRequest request, INoiseStore store) =>
        {
            var outcome = ParseDisputeOutcome(request?.Outcome);
            if (outcome is null)
            {
                return Results.BadRequest(new
                {
                    error = "a dispute is answered as upheld or overturned",
                    outcomes = new[] { DisputeOutcomes.Upheld, DisputeOutcomes.Overturned },
                });
            }

            // ★★ REQUIRED IN BOTH DIRECTIONS. An outcome with no reasoning is "the standard says so", which is
            // exactly the argument 01 §5 says CAI does not get to make — and upholding needs a reason as much as
            // overturning does, because the upheld ones are what show this is not a complaints box.
            if (string.IsNullOrWhiteSpace(request!.Reasoning))
            {
                return Results.BadRequest(new
                {
                    error = "a resolution requires its reasoning, whichever way it goes. An outcome without one "
                          + "is 'the standard says so', which is not an argument CAI gets to make.",
                });
            }

            if (store.FindDispute(disputeId) is null)
            {
                return Results.NotFound(new { disputeId, error = "no such dispute" });
            }

            if (!store.ResolveDispute(disputeId, outcome, request.Reasoning, DateTimeOffset.UtcNow))
            {
                // ★ Already answered. Otherwise the outcome is whatever was written last, and "publishes either
                // way" becomes "publishes whichever way we ended up preferring".
                return Results.Conflict(new
                {
                    disputeId,
                    error = "this dispute has already been answered, and an answer is not replaced. Raise a new "
                          + "dispute if there is something new to say.",
                    resolved = RenderDispute(store.FindDispute(disputeId)!),
                });
            }

            return Results.Ok(RenderDispute(store.FindDispute(disputeId)!));
        })
        .AllowAnonymous()
        .WithName("NoiseDisputeResolve");

        // ★★ THE CHECK THAT POINTS AT US. Every other verification asks whether a vendor's run answered the
        // holdout it claims; this one asks whether the standard's own judging REPRODUCES. A rate produced by a
        // process that disagrees with itself is not a measurement however carefully the corpus was drawn, and
        // CAI owns the judging — so it is the check a critic asks for first, and it was absent.
        endpoints.MapGet("/api/noise/rejudge/{period}", (string period, INoiseStore store) =>
        {
            var judged = JudgedFindings(store, period);
            var seed = RejudgeSeed(period);
            var sample = Rejudge.SelectSample(seed, period, judged);
            var second = store.ListRejudge(period);

            if (sample.Count == 0)
            {
                return Results.Ok(new
                {
                    period,
                    sample = Array.Empty<string>(),
                    sampleSeed = seed,
                    sampleSize = Rejudge.DefaultSampleSize,
                    tolerance = Rejudge.Tolerance,
                    rejudged = false,
                    note = "nothing has been judged for this period, so there is no sample to re-judge.",
                });
            }

            var outcome = second.Count == 0
                ? null
                : Rejudge.Compare(
                    sample,
                    Settled(store, period),
                    second.ToDictionary(r => r.FindingId, r => r.Verdict, StringComparer.OrdinalIgnoreCase));

            return Results.Ok(new
            {
                period,

                // ★★ PUBLISHED BEFORE ANYBODY RE-JUDGES IT, and with the seed beside it: a third party
                // re-derives the same sample from the full judged set, so it cannot have been steered toward
                // the findings that happen to agree.
                sample,
                sampleSeed = seed,
                sampleSize = Rejudge.DefaultSampleSize,
                judgedInPeriod = judged.Count,

                tolerance = Rejudge.Tolerance,
                toleranceRationale = Rejudge.ToleranceRationale,

                rejudged = outcome is not null,
                compared = outcome?.Compared,
                disagreements = outcome?.Disagreements,
                disagreementRate = outcome?.DisagreementRate,
                withinTolerance = outcome?.WithinTolerance ?? false,
                unjudged = outcome?.Unjudged ?? [],
                excluded = outcome?.Excluded ?? [],
                unusable = outcome?.Unusable ?? [],
                note = outcome is null
                    ? "no second pass has been recorded for this period, so the judging has not been shown to "
                    + "reproduce. A rate published on it is unverified in the only sense CAI can verify."
                    : null,
            });
        })
        .AllowAnonymous()
        .WithName("NoiseRejudgeStatus");

        endpoints.MapPost("/api/noise/rejudge/{period}", (string period, RejudgeRequest request, INoiseStore store) =>
        {
            if (request?.Verdicts is not { Count: > 0 })
            {
                return Results.BadRequest(new { error = "at least one re-judged verdict is required" });
            }

            var seed = RejudgeSeed(period);
            var sample = Rejudge.SelectSample(seed, period, JudgedFindings(store, period));
            if (sample.Count == 0)
            {
                return Results.BadRequest(new
                {
                    period,
                    error = "nothing has been judged for this period, so there is no sample to re-judge.",
                });
            }

            // ★★ ONLY THE SAMPLE. Without this the second pass re-judges whatever it likes and reports
            // agreement over its own choice — the steerable sample the seed exists to prevent, arriving
            // through the back door.
            var sampled = sample.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var strays = request.Verdicts
                .Select(v => v.FindingId ?? "")
                .Where(id => !sampled.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (strays.Count > 0)
            {
                return Results.BadRequest(new
                {
                    period,
                    error = "these findings are not in this period's re-judge sample: "
                          + string.Join(", ", strays)
                          + ". The sample is drawn from the period's seed; re-judging a set of your own "
                          + "choosing and reporting agreement over it measures the chooser.",
                    sample,
                });
            }

            // ★ The same provenance a first-pass verdict needs. A verdict a reader cannot argue with is not
            // open judging, and here it is also the unauditable half of a reproducibility claim.
            var unrecordable = request.Verdicts
                .Where(v => string.IsNullOrWhiteSpace(v.Verdict)
                         || string.IsNullOrWhiteSpace(v.Model)
                         || string.IsNullOrWhiteSpace(v.ModelVersion)
                         || string.IsNullOrWhiteSpace(v.PromptId)
                         || string.IsNullOrWhiteSpace(v.Reasoning))
                .Select(v => $"{v.FindingId ?? "(no id)"}: a re-judged verdict needs a verdict, model, "
                           + "modelVersion, promptId and reasoning.")
                .ToList();
            if (unrecordable.Count > 0)
            {
                return Results.BadRequest(new { period, unrecordable });
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var v in request.Verdicts.Where(v => !string.IsNullOrWhiteSpace(v.Prompt)))
            {
                store.RegisterPrompt(v.PromptId!, v.Prompt!, now);
            }

            store.RecordRejudge([.. request.Verdicts.Select(v => new RejudgeRecord(
                period, v.FindingId!, v.Verdict!, v.Model!, v.ModelVersion!, v.PromptId!, v.Reasoning!, now))]);

            var outcome = Rejudge.Compare(
                sample,
                Settled(store, period),
                store.ListRejudge(period)
                    .ToDictionary(r => r.FindingId, r => r.Verdict, StringComparer.OrdinalIgnoreCase));

            return Results.Ok(new
            {
                period,
                sampleSize = outcome.SampleSize,
                compared = outcome.Compared,
                disagreements = outcome.Disagreements,
                disagreementRate = outcome.DisagreementRate,
                tolerance = Rejudge.Tolerance,
                withinTolerance = outcome.WithinTolerance,
                unjudged = outcome.Unjudged,
                excluded = outcome.Excluded,
                unusable = outcome.Unusable,
                fold = Rejudge.Fold,
            });
        })
        .AllowAnonymous()
        .WithName("NoiseRejudgeRecord");

        endpoints.MapPost("/api/noise/publication", (PublicationRequest request, INoiseStore store) =>
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

            // ★★ BOTH AVERAGES, from per-cluster tallies. 02 §5: "so no repository can dominate unseen".
            var clusterTallies = (request.ClusterTallies ?? [])
                .Where(t => !string.IsNullOrWhiteSpace(t.ClusterId))
                .Select(t => new ClusterTally(t.ClusterId!, t.Judged ?? 0, t.Noise ?? 0, t.ClaimClass))
                .ToList();
            var clusterAverages = ClusterAverages.Compute(clusterTallies);

            // ★★ THE ROLLING TWELVE-MONTH FIGURE, pooled from the append-only publication store — plus THIS
            // period, which is not stored yet. Leaving the current one out would publish a "rolling figure
            // beside a rate" that excludes that very rate: visibly wrong on the first period and subtly wrong
            // for ever after. 02 §5 lists it as required with every rate, and it existed nowhere.
            var judgedNow = request.ValidAndActionable + request.ValidNotActionable + request.Noise;
            var rolling = RollingFigure.Compute(
                [
                    .. store.PublishedTallies().Where(t =>
                        !string.Equals(t.Period, request.Period, StringComparison.Ordinal)),
                    new PeriodTally(request.Period ?? "", judgedNow, request.Noise),
                ],
                throughPeriod: request.Period ?? "");

            // ★★ READ FROM THE STORE, never from the request. See PublicationRequest.RejudgeUnavailable.
            var rejudgeSample = Rejudge.SelectSample(
                RejudgeSeed(request.Period ?? ""), request.Period ?? "",
                JudgedFindings(store, request.Period ?? ""));
            var recordedRejudge = store.ListRejudge(request.Period ?? "");
            var rejudgeOutcome = rejudgeSample.Count == 0 || recordedRejudge.Count == 0
                ? null
                : Rejudge.Compare(
                    rejudgeSample,
                    Settled(store, request.Period ?? ""),
                    recordedRejudge.ToDictionary(
                        r => r.FindingId, r => r.Verdict, StringComparer.OrdinalIgnoreCase));

            // ★★ TWO ROUTES TO ONE NUMBER IS HOW THEY DRIFT. The headline rate comes from the census counts and
            // the micro average from the tallies; if they disagree, one of them is wrong and publishing both
            // would let the reader pick. Checked here rather than in the contract because it is a relation
            // between two parts of THIS request, not a requirement the method states about a result.
            var judgedFromCensus = request.ValidAndActionable + request.ValidNotActionable + request.Noise;
            var talliedJudged = clusterTallies.Sum(t => t.Judged);
            var talliedNoise = clusterTallies.Sum(t => t.Noise);
            if (clusterTallies.Count > 0
                && (talliedJudged != judgedFromCensus || talliedNoise != request.Noise))
            {
                return Results.BadRequest(new
                {
                    error = "this result does not meet the contract /api/noise/method publishes.",
                    breaches = new[]
                    {
                        new
                        {
                            field = "clusterTallies",
                            error = $"the per-cluster tallies add up to {talliedJudged} judged and "
                                  + $"{talliedNoise} noise, but the census says {judgedFromCensus} and "
                                  + $"{request.Noise}. One of the two is wrong: the pooled rate would then be "
                                  + "computable two ways with two answers, and a reader shown both could pick.",
                        },
                    },
                    methodVersion = MethodVersion,
                });
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
                fixRateWindowDays: request.FixRateWindowDays,
                configuration: request.Configuration,
                period: request.Period,
                rejudge: rejudgeOutcome,
                rejudgeUnavailable: request.RejudgeUnavailable);

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

            // ★★ THE INTERVAL, COMPUTED HERE AND CARRIED WITH THE RATE. #23-4: the number never appears
            // without its interval and its period — "if the surface cannot carry the qualifiers, it does not
            // carry the number". Nothing computed one before, so that constraint was unsatisfiable.
            var interval = PublicationSurface.WilsonIntervalOrNull(request.Noise, judged);

            var payload = new
            {
                period = request.Period,

                // ★★ THE VERSION IN FORCE FOR THIS PERIOD, not the newest. A period judged under 1.0 publishes
                // as judged under 1.0 for ever, which is exactly what stops a later change reaching back and
                // reinterpreting a number somebody disliked.
                methodVersion = MethodVersions.InForceFor(request.Period)?.Version ?? MethodVersion,
                methodVersionRationale = MethodVersions.InForceFor(request.Period)?.Rationale,

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

                // ★★ NEVER the bare rate. A noise rate lives near the ends of the scale, which is where the
                // normal approximation fails outright — 0 of 200 becomes "0 % to 0 %", certainty from a sample
                // that proves nothing of the kind. Wilson stays inside [0,1] and stays honest there.
                noiseRateInterval = interval is { } ci
                    ? new { low = (double?)ci.Low, high = (double?)ci.High, method = "wilson-95" }
                    : new { low = (double?)null, high = (double?)null, method = "wilson-95" },

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

                    // ★★ THE MACRO PER CLASS. A pooled rate across claim classes is a category error the
                    // method already refuses; a pooled macro across them is the same error one level up, so
                    // the pointwise average must be readable without the structural findings moving it.
                    macroRate = ClusterAverages
                        .ComputeFor(clusterTallies, ClaimSpecificity.Wire(c.Class)).MacroRate,

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
                    // ★★ HOW BIG THE PRISTINE SLICE IS. The overfitting gap is computed against the
                    // never-trained stratum, which IS this slice — so its size is what tells a reader whether the
                    // gap rests on three repositories or thirty.
                    reservedRepositories = CorpusManifest.Load().Candidates.Count(c => c.Reserved),
                    reservedNote = "The reserved repositories are never used for development by any participant, "
                                 + "and are in every draw. Declaring one as trained is refused — without a "
                                 + "never-trained endpoint the decay curve measures nothing.",

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

                // ★★ WHETHER THE JUDGING BEHIND THIS NUMBER REPRODUCES, published with the number. It is the
                // only figure here that says anything about the instrument rather than about the tool, and a
                // reader weighing a two-point move needs it more than they need any of the rest.
                rejudge = rejudgeOutcome is { } ro
                    ? new
                    {
                        declared = true,
                        unavailableReason = (string?)null,
                        sampleSize = (int?)ro.SampleSize,
                        compared = (int?)ro.Compared,
                        disagreements = (int?)ro.Disagreements,
                        disagreementRate = ro.DisagreementRate,
                        tolerance = (double?)Rejudge.Tolerance,
                        withinTolerance = ro.WithinTolerance,
                        fold = Rejudge.Fold,
                    }
                    : new
                    {
                        declared = false,
                        unavailableReason = request.RejudgeUnavailable,
                        sampleSize = (int?)null,
                        compared = (int?)null,
                        disagreements = (int?)null,
                        disagreementRate = (double?)null,
                        tolerance = (double?)Rejudge.Tolerance,
                        withinTolerance = false,
                        fold = Rejudge.Fold,
                    },

                // ★★ THE ROLLING FIGURE, with its interval and its SPAN. A single period's interval is wide
                // enough to hide most movements, so this is the only rate here that can support a claim about a
                // trend — and a three-period pool quoted as the annual number is the natural failure, because
                // the two look identical and only `spansTheFullWindow` separates them.
                twelveMonth = new
                {
                    windowMonths = RollingFigure.WindowMonths,
                    periods = rolling.Periods,
                    spansTheFullWindow = rolling.SpansTheFullWindow,
                    firstPeriod = rolling.FirstPeriod,
                    lastPeriod = rolling.LastPeriod,
                    judged = rolling.Judged,
                    noise = rolling.Noise,
                    rate = rolling.Rate,
                    intervalLow = rolling.IntervalLow,
                    intervalHigh = rolling.IntervalHigh,
                    intervalMethod = "wilson-95",
                    note = rolling.Note,
                },

                // ★★ THE SPOT-CHECK, and the contested tail BESIDE it rather than merged. The contested items
                // are hard by construction; the spot-check sample is the pipeline's own claim about itself, and
                // it is the only evidence that the judges agreeing made them right. A combined figure would hide
                // the disagreement rate on auto-accepted findings — and it is the one that would get quoted.
                spotCheck = CrowdSlice(request.Period, CrowdReason.SpotCheck),
                contestedTail = CrowdSlice(request.Period, CrowdReason.Contested),

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

                // ★★ The configuration travels WITH the number. #23-1: deviations publish alongside it, and
                // the publication is the number a reader quotes.
                configuration = request.Configuration is { } cfg ? new
                {
                    rulesetId = cfg.RulesetId,
                    isProductDefault = cfg.IsProductDefault,
                    divergenceExplanation = cfg.DivergenceExplanation,
                    rulesDisabled = cfg.RulesDisabled ?? [],
                    thresholdsAltered = (cfg.ThresholdsAltered ?? []).Select(t => new
                    {
                        ruleId = t.RuleId, shipped = t.Shipped, used = t.Used,
                    }),
                } : null,

                provenance = new
                {
                    toolVersion = request.ToolVersion,
                    holdoutSeed = request.HoldoutSeed,
                    modelSet = request.ModelSet,
                    gitMiningVerified = request.GitMiningVerified,
                },

                clusters = summary.Clusters,

                // ★★ THE SAME RATE, TWICE. 02 §5 requires both so no repository can dominate the number
                // unseen — and the defence cannot be to drop the outlier, because excluding a repository for
                // having an extreme rate is selecting on the outcome.
                clusterAverages = new
                {
                    micro = clusterAverages.MicroRate,
                    macro = clusterAverages.MacroRate,
                    leaveOneOutLow = clusterAverages.LeaveOneOutLow,
                    leaveOneOutHigh = clusterAverages.LeaveOneOutHigh,

                    // ★ Named: "the range is wide" without saying which repository did it is a fact nobody
                    // can act on.
                    mostInfluentialCluster = clusterAverages.MostInfluentialCluster,

                    clustersWithARate = clusterAverages.ClustersWithARate,
                    clustersWithNothingJudged = clusterAverages.ClustersWithNothingJudged,

                    // ★★ Flagged rather than left as arithmetic: publishing two numbers and expecting the
                    // reader to subtract them is how the second one gets ignored.
                    averagesDiverge = clusterAverages.AveragesDiverge,
                    notableDivergence = ClusterAverages.NotableDivergence,
                    note = clusterAverages.Note,
                },

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
            };

            // ★★ STORED, so there is something to transclude. #23-4 has CAI own the published result and
            // watchdog.canine.dev render it at request time rather than keeping a copy — two copies drifting
            // is this codebase's track record, and on this number a caching bug and a suppression are the
            // same event seen from outside. A POST that computed and forgot left nothing to render, so the
            // Watchdog surface could only have restated a figure kennel computed itself: the rejected option.
            //
            // ★ APPEND-ONLY, and only on the accepted path — a refused result must not be fetchable as
            // though it had passed the contract.
            store.RecordPublication(
                request.Period!, JsonSerializer.Serialize(payload), DateTimeOffset.UtcNow);

            return Results.Ok(payload);
        })
        .AllowAnonymous()
        .WithName("NoisePublication");

        // ★★ THE READ SIDE OF #23-4. Watchdog fetches this at request time; it is the only copy.
        endpoints.MapGet("/api/noise/published/{period}", (string period, INoiseStore store) =>
            ServePublished(store, period))
        .AllowAnonymous()
        .WithName("NoisePublishedResult");

        // ★★ AND THE SAME THING WITHOUT A PERIOD. A transcluding surface does not know which period is
        // current: given only the keyed route it would have to derive one — "this month", "last month if this
        // 404s" — which is the same class of guess the standard exists to remove, and it would quietly show a
        // stale period the month a cycle slips. The LATEST is a fact CAI holds, so CAI answers it.
        endpoints.MapGet("/api/noise/published", (INoiseStore store) =>
            // ★ PublishedPeriods() is ordered by period descending, so the answer comes from the period the
            // result measures and never from insertion order — a correction to an older period must not
            // become "the current number".
            ServePublished(store, store.PublishedPeriods().FirstOrDefault()))
        .AllowAnonymous()
        .WithName("NoiseLatestPublishedResult");

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
                    f.Tool ?? "", f.RepoId ?? "", f.FilePath ?? "", f.Line ?? 0, f.Valid ?? false,
                    f.HasFixPairOracle ?? false))
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

                    // ★★ THIS TOOL'S OWN DENOMINATOR. Every tool is scored against a different reference —
                    // its own findings removed — so a single shared union size would be the wrong denominator
                    // for everybody, and the figure could not be checked by the reader it is published for.
                    leaveOneOutReferenceSize = t.LeaveOneOutReferenceSize,
                    pooledRecallUnavailable = t.PooledRecallUnavailable,
                }),

                // ★★ Whether the pool is large enough for the figure to mean what its name says, and the floor
                // itself. Below three tools each leave-one-out reference is essentially one other tool's
                // findings, so "recall" is pairwise agreement — published as refused rather than as a number.
                pooledRecallAvailable = summary.PooledRecallAvailable,
                minimumTools = PooledRecall.MinimumTools,

                // ★★ Said out loud: this is a PSEUDO-oracle. The difference between "our recall is 62 %" and
                // "62 % of what this pool found" is the whole reading.
                pseudoOracle = summary.PseudoOracle,
                scope = summary.Scope,
                excludedWithFixPairOracle = summary.ExcludedWithFixPairOracle,

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

                // ★★ WHAT THE RATERS WOULD DO, beside what they LABELLED it (#13). 02 §4 validates the spec
                // against practitioner behaviour rather than against opinions of the spec's vocabulary — and
                // where the two disagree is the most informative thing this layer produces.
                behaviour = Behaviour(),

                // ★★ Published beside the figures, never as a footnote elsewhere. A reader who cannot see
                // that four fifths of the answers came from one language, or from the vendor, is reading
                // an agreement rate as if it measured truth.
                composition = Composition(),
            });

            object Behaviour()
            {
                var asked = measured.Where(a => a.WouldFix is not null || a.WantInReport is not null).ToList();
                var fix = measured.Where(a => a.WouldFix is not null).ToList();
                var report = measured.Where(a => a.WantInReport is not null).ToList();

                return new
                {
                    answered = asked.Count,

                    // ★★ NOT ASKED is its own count, never folded into "no". A missing answer counted as "would
                    // not fix" would manufacture evidence that practitioners ignore findings nobody asked them
                    // about — and the more raters skipped it, the stronger that false signal would get.
                    notAsked = measured.Count - asked.Count,

                    wouldFix = fix.Count(a => a.WouldFix == true),
                    wantInReport = report.Count(a => a.WantInReport == true),
                    wouldFixRate = fix.Count > 0 ? (double?)fix.Count(a => a.WouldFix == true) / fix.Count : null,
                    wantInReportRate = report.Count > 0
                        ? (double?)report.Count(a => a.WantInReport == true) / report.Count
                        : null,

                    // ★★ WHERE THE LABEL AND THE BEHAVIOUR PART COMPANY. A finding called VALID that nobody would
                    // fix is one the spec counts as a success and the practitioner would ignore; noise somebody
                    // would fix anyway says the taxonomy is cutting in the wrong place. Merging the two figures
                    // would destroy exactly this.
                    validButWouldNotFix = measured.Count(a => !a.Verdict.IsNoise() && a.WouldFix == false),
                    noiseButWouldFix = measured.Count(a => a.Verdict.IsNoise() && a.WouldFix == true),

                    questions = new
                    {
                        wouldFix = BehaviouralQuestions.WouldFix,
                        wantInReport = BehaviouralQuestions.WantInReport,
                    },
                    note = "Where a verdict and the behaviour disagree, the spec and the practitioner have parted "
                         + "company — that gap is what this layer is for. "
                         + BehaviouralQuestions.RelationToTheRate,
                };
            }

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

    /// <summary>
    /// Serve one period's published result, or a stated 404.
    /// </summary>
    /// <remarks>
    /// ★ ONE implementation behind both routes. Two would be two chances for the keyed and the latest answer to
    /// carry different fields, and a consumer that got a thinner body from one of them would render a rate
    /// without its interval — the one thing #23-4 forbids outright.
    /// </remarks>
    private static IResult ServePublished(INoiseStore store, string? period)
    {
        var stored = string.IsNullOrWhiteSpace(period) ? null : store.LatestPublication(period);

        if (stored is not { } published)
        {
            // ★★ NEVER a zero-filled body. "We measured that period and found nothing" and "nothing has been
            // published for it" are different claims, and the first one is false.
            return Results.NotFound(new
            {
                period,
                error = period is { Length: > 0 }
                    ? $"no result has been published for period '{period}'."
                    : "no result has been published yet.",
                published = store.PublishedPeriods(),
            });
        }

        var node = JsonNode.Parse(published.PayloadJson)!.AsObject();
        node["publishedAt"] = JsonValue.Create(published.PublishedAt);

        // ★★ A CORRECTION IS VISIBLE AS ONE. On the single figure where §01 says that being seen to suppress
        // ends the standard, a store that overwrote would make the second publication of a period
        // indistinguishable from the first.
        node["supersededCount"] = JsonValue.Create(published.History.Count - 1);
        node["history"] = new JsonArray(
            published.History.Select(h => (JsonNode?)JsonValue.Create(h)).ToArray());

        // ★ What else exists, so a reader who arrived without a period can walk back through the history.
        node["publishedPeriods"] = new JsonArray(
            store.PublishedPeriods().Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());

        return Results.Json(node);
    }

    /// <summary>The reservation, as a published rule — one object, used by the method, the corpus and each draw.</summary>
    private static object ReservedSliceRule() => new
    {
        repositories = CorpusManifest.Load().Candidates.Count(c => c.Reserved),
        alwaysDrawn = true,
        why = "The recency strata compare 'never trained' against 'trained N cycles ago'. If every repository is "
            + "eventually developed against, the never-trained bucket empties as the standard matures — the "
            + "decay curve loses its endpoint, and the overfitting gap becomes uncomputable exactly when tools "
            + "have had time to overfit.",
        declaringItTrained = "refused. A submission that declares a reserved repository as anything but "
                           + "never-trained is rejected: that declaration IS the reservation being broken, and "
                           + "it is the only moment anybody outside the vendor can see it happen. A repository "
                           + "that has been developed against must LEAVE the reserved slice, which is a change "
                           + "to the signed corpus.",
        listedIn = "/api/noise/corpus and every /api/noise/holdout/{period}, per repository.",
    };

    /// <summary>
    /// Every tool's mark for a period, decided from what the store already holds.
    /// </summary>
    /// <remarks>
    /// ★★ NOTHING IS ASKED OF ANYBODY. The four conditions read the receipt (accepted, and its coverage), the
    /// deadline against the receipt's timestamp, and whether a publication exists — all facts CAI recorded when
    /// they happened. A mark that needed an input would be a mark somebody could argue for.
    /// </remarks>
    private static IReadOnlyList<MarkState> MarksFor(INoiseStore store, string period)
    {
        var deadline = NoiseCorpus.Draws.TryGetValue(period, out var draw) ? draw.SubmissionsCloseAt : null;
        var published = store.LatestPublication(period) is not null;

        return
        [
            .. store.ListSubmissions(period)
                .GroupBy(r => r.Tool, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g =>
                {
                    // ★ The BEST receipt this tool has for the period: a refused run followed by an accepted one
                    // is a tool that got it right, and the register keeps both either way.
                    var best = g.OrderByDescending(r => r.Accepted).ThenBy(r => r.ReceivedAt).First();

                    return ComplianceMark.Evaluate(g.Key, period, new MarkInputs(
                        // ★ Coverage is the holdout check the receipt already reports: an accepted run with no
                        // uncovered repositories ran the published draw.
                        RanAgainstTheHoldout: best.Accepted && best.CoveredRepositories > 0,
                        SubmittedBeforeTheDeadline: deadline is not { } closes || best.ReceivedAt <= closes,
                        RunReproduces: best.Accepted,
                        NumbersPublishedInFull: published));
                }),
        ];
    }

    /// <summary>The two answers a dispute can have.</summary>
    private static class DisputeOutcomes
    {
        public const string Upheld = "upheld";
        public const string Overturned = "overturned";
    }

    /// <summary>The outcome, or null when it is not one of the two.</summary>
    private static string? ParseDisputeOutcome(string? outcome) => outcome?.Trim().ToLowerInvariant() switch
    {
        DisputeOutcomes.Upheld => DisputeOutcomes.Upheld,
        DisputeOutcomes.Overturned => DisputeOutcomes.Overturned,
        _ => null,
    };

    /// <summary>One dispute as it publishes.</summary>
    private static object RenderDispute(DisputeRecord d) => new
    {
        disputeId = d.DisputeId,
        period = d.Period,
        findingId = d.FindingId,
        raisedBy = d.RaisedBy,
        reason = d.Reason,
        raisedAt = d.RaisedAt,

        // ★ "open" is a state, not a missing field — see RenderDisputes.
        state = d.Outcome is null ? "open" : "answered",
        outcome = d.Outcome,
        resolutionReasoning = d.ResolutionReasoning,
        resolvedAt = d.ResolvedAt,
    };

    /// <summary>A period's disputes, with the counts a reader needs before the list.</summary>
    private static object RenderDisputes(INoiseStore store, string period)
    {
        var disputes = store.ListDisputes(period);

        return new
        {
            raised = disputes.Count,
            open = disputes.Count(d => d.Outcome is null),
            upheld = disputes.Count(d => d.Outcome == DisputeOutcomes.Upheld),
            overturned = disputes.Count(d => d.Outcome == DisputeOutcomes.Overturned),

            // ★★ Two things a reader would otherwise assume, said once: the raw verdict survives an overturn,
            // and an overturn does not silently move a published rate.
            note = "The raw verdicts are append-only and nothing here removes one — an overturned verdict is "
                 + "still in this record, with the dispute beside it. An overturned verdict does not change a "
                 + "published rate by itself either: that takes a corrected publication, which the "
                 + "append-only publication record shows as a correction.",

            items = disputes.Select(RenderDispute),
        };
    }

    /// <summary>
    /// A 503 when the shipped corpus does not verify, or null when it does.
    /// </summary>
    /// <remarks>
    /// ★★ FAIL CLOSED, AND SAY WHY. The alternative is an endpoint that serves the pool with the signature field
    /// missing or false — a degradation nobody reading the response would notice, on the one artefact whose whole
    /// job is to be checkable. 503 rather than 500: the corpus is fixable and the fault is ours.
    /// </remarks>
    private static IResult? CorpusUnverifiable()
    {
        var manifest = CorpusManifest.Load();

        // ★ The DECISION is CorpusManifest.RefusalReason — a pure function, so it has a test. Shipping a broken
        // manifest is the only other way to reach this branch, and that breaks every other test in the suite.
        return CorpusManifest.RefusalReason(manifest) is not { } reason
            ? null
            : Results.Json(
                new
                {
                    error = reason,
                    detail = manifest.Problem,
                    manifestVersion = manifest.Version,
                    keyId = manifest.KeyId,
                    howToVerify = CorpusManifest.VerificationInstructions,
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>Which manifest, signed by which key — published with every draw and with the pool.</summary>
    private static object ManifestIdentity()
    {
        var manifest = CorpusManifest.Load();

        return new
        {
            version = manifest.Version,
            keyId = manifest.KeyId,
            algorithm = CorpusManifest.Algorithm,
            signature = manifest.Signature,

            // ★★ THE CUSTODY CLAIM, published with the signature. Who holds the key IS what a signature is worth,
            // so a reader gets it in words rather than a key id to interpret — and it says plainly what this one
            // does NOT prove.
            keyCustody = manifest.KeyCustody,

            files = new
            {
                manifest = CorpusManifest.ManifestFileName,
                signature = CorpusManifest.SignatureFileName,
                publicKey = CorpusManifest.PublicKeyFileName,
            },
        };
    }

    /// <summary>
    /// One crowd slice for a period, as the publication carries it.
    /// </summary>
    /// <remarks>
    /// ★★ THE SAME ARITHMETIC AS <c>/api/noise/crowd/results/{period}</c>, and deliberately the same shape: a
    /// spot-check figure that disagreed with the crowd endpoint's would leave a reader unable to tell which was
    /// the standard's answer. Honeypots leave the count here too — their answer was known before it was asked.
    /// </remarks>
    private static object CrowdSlice(string? period, CrowdReason reason)
    {
        if (period is not { Length: > 0 } || CrowdQueues.Find(period) is not { } round)
        {
            // ★ An absence, never a blank. "No spot-check was run" and "the spot-check found no
            // contradictions" are opposite claims and look identical when one of them is a missing field.
            return new
            {
                run = false,
                queued = (int?)null,
                answered = (int?)null,
                contradicted = (int?)null,
                notComparable = (int?)null,
                note = "no crowd round is registered for this period, so this check was not run. That is an "
                     + "absence and not a clean result.",
            };
        }

        var byFinding = round.Queue.ToDictionary(
            i => i.FindingId, i => i.Reason, StringComparer.OrdinalIgnoreCase);
        var measured = RaterCalibration.ExcludeHoneypots([.. round.Answers], [.. round.Honeypots.Values]);
        var answers = measured
            .Where(a => byFinding.TryGetValue(a.FindingId, out var r) && r == reason)
            .ToList();

        return new
        {
            run = true,
            queued = (int?)round.Queue.Count(i =>
                i.Reason == reason && !round.Honeypots.ContainsKey(i.FindingId)),
            answered = (int?)answers.Count,

            // ★ A contradiction is the whole point of the spot-check: the judges agreed, and a person outside
            // the model family says otherwise.
            contradicted = (int?)answers.Count(a =>
                a.MachineVerdict is { } m && a.Verdict.IsNoise() != m.IsNoise()),

            // ★ Answers with nothing to compare against are counted HERE and never as agreement, or omitting
            // one field would hide every disagreement.
            notComparable = (int?)answers.Count(a => a.MachineVerdict is null),
            note = (string?)null,
        };
    }

    /// <summary>A period's re-judge as it publishes: the sample, the outcome and the raw second pass.</summary>
    private static object RenderRejudge(INoiseStore store, string period)
    {
        var judged = JudgedFindings(store, period);
        var seed = RejudgeSeed(period);
        var sample = Rejudge.SelectSample(seed, period, judged);
        var second = store.ListRejudge(period);

        var outcome = second.Count == 0 || sample.Count == 0
            ? null
            : Rejudge.Compare(
                sample,
                Settled(store, period),
                second.ToDictionary(r => r.FindingId, r => r.Verdict, StringComparer.OrdinalIgnoreCase));

        return new
        {
            sample,
            sampleSeed = seed,
            tolerance = Rejudge.Tolerance,
            fold = Rejudge.Fold,
            compared = outcome?.Compared,
            disagreements = outcome?.Disagreements,
            disagreementRate = outcome?.DisagreementRate,
            withinTolerance = outcome?.WithinTolerance ?? false,
            unjudged = outcome?.Unjudged ?? [],
            excluded = outcome?.Excluded ?? [],
            unusable = outcome?.Unusable ?? [],

            // ★ Raw, with provenance. Named `verdicts` to mirror the first pass's shape.
            verdicts = second.Select(r => new
            {
                findingId = r.FindingId,
                verdict = r.Verdict,
                model = r.Model,
                modelVersion = r.ModelVersion,
                promptId = r.PromptId,
                reasoning = r.Reasoning,
                recordedAt = r.RecordedAt,
            }),

            note = second.Count == 0
                ? "no second pass has been recorded, so the judging has not been shown to reproduce."
                : null,
        };
    }

    /// <summary>The findings a period has a settled verdict for — the population the sample is drawn from.</summary>
    /// <remarks>
    /// ★ Only SETTLED ones. A finding still in the cascade has no first-pass answer to disagree with, so
    /// sampling it would produce an unjudged entry that blocks the tolerance through no fault of the re-judge.
    /// </remarks>
    private static IReadOnlyList<string> JudgedFindings(INoiseStore store, string period) =>
        [.. store.ListResolutions(period)
            .Where(r => !string.IsNullOrWhiteSpace(r.Verdict))
            .Select(r => r.FindingId)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>Each finding's settled verdict, for comparison against a second pass.</summary>
    private static IReadOnlyDictionary<string, string> Settled(INoiseStore store, string period) =>
        store.ListResolutions(period)
            .Where(r => !string.IsNullOrWhiteSpace(r.Verdict))
            .GroupBy(r => r.FindingId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.RecordedAt).First().Verdict!,
                StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The seed the re-judge sample is drawn from.
    /// </summary>
    /// <remarks>
    /// ★★ THE PERIOD'S OWN PUBLISHED HOLDOUT SEED, so the sample is reproducible from a value that was
    /// published before any result existed. A period with no published draw falls back to its own identifier —
    /// which in production cannot happen, because judging without a draw is judging findings from nowhere; it
    /// is the honest answer for a dev or test period rather than a throw that would hide the case.
    /// </remarks>
    private static string RejudgeSeed(string period) =>
        NoiseCorpus.Draws.TryGetValue(period ?? "", out var draw) ? draw.Seed : period ?? "";

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
        // ★★ The period the number measures. Required — see PublicationContract.
        string? Period,
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
        RunConfiguration? Configuration = null,
        string? ToolVersion = null,
        string? HoldoutSeed = null,
        string? ModelSet = null,
        bool? GitMiningVerified = null,
        int? GapsFoundSinceLastPeriod = null,

        // ★★ PER-CLUSTER TALLIES, without which the macro average cannot exist. See ClusterTallyEntry.
        IReadOnlyList<ClusterTallyEntry>? ClusterTallies = null,

        // ★★ A stated reason no second pass was run. NOTE there is deliberately NO field for its OUTCOME: CAI
        // holds that, and a body able to declare its own reproducibility would be publishing the self-measured
        // number the standard exists to replace. Optional and LAST, because inserting a required parameter in
        // the middle of a positional record silently reorders every caller that passes them by position.
        string? RejudgeUnavailable = null);

    /// <summary>One claim class's share of the run, as it arrives on the wire.</summary>
    public sealed record ClaimClassEntry(string? ClaimClass, int Judged, int Noise);

    /// <summary>One recency stratum's share of the run, as it arrives on the wire.</summary>
    public sealed record RecencyEntry(string? Stratum, int Judged, int Noise);

    /// <summary>A count per 100k LoC, or null without a denominator.</summary>
    private static double? Per100k(int count, long? loc) =>
        loc is > 0 ? count * 100_000d / loc.Value : null;

    /// <summary>One tool's finding and its adjudication, as it arrives on the wire.</summary>
    /// <param name="HasFixPairOracle">
    /// ★ Whether a real before/after fix pair establishes this defect. Those findings leave the pool: a commit
    /// is evidence where a pool is consensus, and blending them publishes the blend under the stronger name.
    /// </param>
    /// <summary>One cluster's judged findings as they arrive on the wire.</summary>
    /// <remarks>
    /// ★★ A COUNT OF CLUSTERS CANNOT PRODUCE A CLUSTER-WEIGHTED AVERAGE. The publication carried
    /// <c>clusters: 14</c> and nothing else about them, which is enough for the clustering interval and
    /// structurally insufficient for 02 §5's second average — so the requirement was published in
    /// <c>reportingRule</c> and unimplementable at the same time.
    /// </remarks>
    public sealed record ClusterTallyEntry(
        string? ClusterId, int? Judged, int? Noise, string? ClaimClass = null);

    public sealed record PooledFindingEntry(
        string? Tool, string? RepoId, string? FilePath, int? Line, bool? Valid,
        bool? HasFixPairOracle = null);

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

    /// <summary>A declared intent to submit for a period.</summary>
    public sealed record IntentRequest(string? Period, string? Tool);

    /// <summary>A verdict being contested.</summary>
    public sealed record DisputeRequest(string? Period, string? RaisedBy, string? Reason);

    /// <summary>How a dispute was answered.</summary>
    public sealed record DisputeResolutionRequest(string? Outcome, string? Reasoning);

    /// <summary>One verdict from the independent second pass.</summary>
    public sealed record RejudgeVote(
        string? FindingId, string? Verdict,
        string? Model = null, string? ModelVersion = null,
        string? PromptId = null, string? Prompt = null, string? Reasoning = null);

    /// <summary>A second pass over a period's re-judge sample.</summary>
    public sealed record RejudgeRequest(IReadOnlyList<RejudgeVote>? Verdicts);

    /// <summary>A honeypot as it arrives on the wire.</summary>
    public sealed record HoneypotEntry(string? FindingId, string? Truth, string? Source, string? Evidence);

    /// <summary>Honeypots to plant into a period's queue.</summary>
    public sealed record HoneypotRequest(string? Period, IReadOnlyList<HoneypotEntry>? Honeypots);

    /// <summary>One person's answer to one finding.</summary>
    /// <param name="WouldFix">
    /// ★★ Nullable, and a missing one is NOT ASKED rather than "no" — folding it into "no" would manufacture
    /// evidence that practitioners would not act on findings nobody asked them about.
    /// </param>
    public sealed record CrowdAnswerRequest(
        string? Period, string? RaterId, string? FindingId, string? Verdict, string? MachineVerdict,
        bool? WouldFix = null, bool? WantInReport = null);

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
        string? PromptId = null, string? Prompt = null, string? Reasoning = null,

        // ★★ The panel's shape (#10). The FAMILY is the training tradition, not the product name — four models
        // from one vendor is one family — and the TEMPERATURE must be 0 or the verdict cannot be re-run to the
        // same answer. Both nullable on the wire so a missing one is REFUSED rather than defaulted: an undeclared
        // family that counted as "some other family", or an undeclared temperature that counted as 0, would let
        // every panel pass by omitting the field.
        string? ModelFamily = null, double? Temperature = null);

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
        // ★★ THE COUNTS, published even when they agree. A check whose inputs are not shown cannot be
        // re-derived by the reader it exists for, and this is the one check that says how much of the run
        // reached the standard at all.
        findingCount = new
        {
            reportedByRun = r.ReportedFindingCount,
            submitted = r.SubmittedFindingCount,
            agrees = r.ReportedFindingCount == r.SubmittedFindingCount,
        },

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
