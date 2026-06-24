namespace Cai.Web;

/// <summary>
/// Per-dimension "why it matters" prose for /dimensions. The EN sentences are ported VERBATIM from the surveyor's
/// founder-approved copy (Watchdog.Api/Documents/DimensionConsequences.cs — each sentence is derivable from that
/// dimension's whatItMeasures, no over-claim) — NOT invented. A dimension with no entry shows nothing rather than a
/// fabricated claim.
/// </summary>
public static class CaiDimConsequences
{
    public static string? For(string id) => ById.TryGetValue(id, out var s) ? s : null;

    public static readonly IReadOnlyDictionary<string, string> ById =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Code health
            ["D1"] = "When this is weak, the code is full of tangled, heavily branching methods that are hard to test and risky to change, so even small changes take longer and break things more often.",
            ["D2"] = "When this is weak, the code is hard for a developer to follow, so understanding it before changing it takes longer and mistakes are more likely.",
            ["D3"] = "When this is weak, oversized classes try to do too much at once, making them a bottleneck that several developers cannot safely work on in parallel.",
            ["D4"] = "When this is weak, the same logic is copy-pasted in several places, so a fix or change must be made repeatedly and it is easy to miss a copy and reintroduce a bug.",
            ["D17"] = "When this is weak, the team has left acknowledged debt in the code — TODOs, dead code and suppressed warnings — that signals unfinished work you will eventually have to pay down.",
            ["D18"] = "When this is weak, the solution is not laid out in a conventional structure, so a new developer takes longer to find their way around and onboarding costs more.",
            ["GD1"] = "When this is weak, the code still contains shipped methods that throw \"not implemented\" and placeholder text left in place, meaning features look present but do not actually work.",
            ["IC1"] = "When this is weak, parts of the code are unfinished by their very shape — stubs, methods that ignore their input, async methods that never wait, dead branches — so the delivery is less complete than it appears.",
            ["R1"] = "When this is weak, much of the frontend is untyped JavaScript rather than typed TypeScript, so whole classes of mistakes slip through to runtime instead of being caught while coding.",
            ["R2"] = "When this is weak, individual frontend functions are heavily branching and tangled, so they are hard to test and risky to change.",
            ["R3"] = "When this is weak, several frontend components or modules are oversized, making them harder to understand and a bottleneck for parallel work.",
            ["R7"] = "When this is weak, the frontend carries dead code — files and exports nothing else uses — that adds noise, inflates the build and slows down anyone reading the code.",
            ["R10"] = "When this is weak, identical blocks of frontend code are copy-pasted across the codebase, so the same change has to be made in many places and copies are easily missed.",
            ["X1"] = "When this is weak, asynchronous code is written in deadlock-prone ways (blocking on tasks, async void), which can cause the application to hang or crash under load.",
            ["X2"] = "When this is weak, long-running operations cannot be cancelled cleanly, so the system keeps doing work no one is waiting for and is harder to shut down responsively.",
            ["X3"] = "When this is weak, errors are silently swallowed or rethrown with their origin lost, so real failures go unnoticed and are far harder to diagnose when they surface.",
            ["X4"] = "When this is weak, log messages are built as plain interpolated strings rather than structured templates, so production logs are hard to search and filter when you are trying to find a problem.",
            ["X5"] = "When this is weak, the compiler's null-safety checks are off or undermined by suppression, so null-related crashes that the language could have prevented reach production instead.",
            // Architecture
            ["D5"] = "When this is weak, the parts of the system depend tightly on each other and form circular dependencies, so a change in one place forces changes in several others and the structure resists growth.",
            ["D6"] = "When this is weak, individual classes mix several unrelated responsibilities, making them harder to understand, test and change in isolation.",
            ["D7"] = "When this is weak, the code does not respect its own intended layering, so the structure on paper no longer matches reality and the design erodes over time.",
            ["D22"] = "When this is weak, the system's internal interfaces are inconsistent, so developers cannot rely on familiar patterns and each part has to be learned from scratch.",
            ["D23"] = "When this is weak, domain types leak across module boundaries, so the parts of the system that should be independent become entangled and cannot evolve separately.",
            ["D26"] = "When this is weak, individual projects have become oversized grab-bags rather than focused units, blurring responsibilities and making the codebase harder to reason about.",
            ["D27"] = "When this is weak, following a single call means tracing through many layers of indirection, so understanding even simple behaviour takes a developer much longer.",
            ["AX1"] = "When this is weak, long-lived services hold on to short-lived dependencies — a silent lifetime bug that can cause subtle, hard-to-reproduce errors under concurrent use.",
            ["AX2"] = "When this is weak, shared single-instance services keep mutable state that concurrent users can race on, leading to intermittent corruption that is very hard to track down.",
            ["AX3"] = "When this is weak, the projects depend on each other in cycles, so they cannot be built or deployed independently and the architectural boundaries are eroding.",
            ["AX4"] = "When this is weak, dependencies point the wrong way through the layers, so the core business logic is tied to technical details and becomes harder to test and replace.",
            ["AX5"] = "When this is weak, the codebase has no recognisable, scale-appropriate structure, so it reads as an ad-hoc tangle that is hard to navigate and grow.",
            ["AX6"] = "When this is weak, interfaces have grown fat with unrelated members, forcing callers to depend on methods they do not use and making the contracts harder to change.",
            ["AX7"] = "When this is weak, feature slices reach directly into each other instead of staying independent, so a change to one feature can unexpectedly break another.",
            ["R9"] = "When this is weak, frontend files import each other in cycles, so they can only be understood and changed together — a tightly knotted area that resists refactoring.",
            ["R11"] = "When this is weak, the frontend ignores its own intended layout and reaches across package boundaries with deep imports, so the structure decays and modules become entangled.",
            // Maturity
            ["D15"] = "When this is weak, the riskiest files — those that are both complex and change often — are concentrated and unaddressed, so the same hotspots keep generating defects.",
            ["D16"] = "When this is weak, knowledge of the code is concentrated in too few people, so the project is exposed if a key person becomes unavailable (the \"bus factor\").",
            ["D19"] = "When this is weak, the project's documentation is unclear or incomplete, so new people lean on the original authors instead of the documents to get up to speed.",
            ["D20"] = "When this is weak, the reasons behind key architecture decisions are not recorded, so future maintainers cannot tell why the system was built the way it was.",
            ["D21"] = "When this is weak, names for types, methods and variables are unclear or inconsistent, so the code is harder to read and small misunderstandings turn into bugs.",
            ["D24"] = "When this is weak, comments mostly restate what the code already says instead of explaining why, so they add clutter without helping the next reader.",
            ["D25"] = "When this is weak, the code does not actually follow the architecture decisions the project wrote down, so the documented design and the real system have drifted apart.",
            ["M1"] = "When this is weak, the repository lacks a substantive, current README, so anyone picking up the project has no reliable starting point and onboarding is slower.",
            ["M2"] = "When this is weak, the key decisions and the high-level shape of the system are not written down, so its design lives only in people's heads and is lost when they leave.",
            ["M3"] = "When this is weak, the repository is not organised deliberately — source and tests are not cleanly separated and project naming is inconsistent — so it is harder to find your way around.",
            ["M4"] = "When this is weak, the README no longer matches the code that actually exists, so the documentation actively misleads anyone who relies on it.",
            // Production readiness
            ["D8"] = "When this is weak, little of the code is actually exercised by tests, so most defects are found by your users in production rather than by the test suite.",
            ["D9"] = "When this is weak, the test suite is lopsided — missing the right mix of unit, integration and end-to-end tests — so whole categories of failures go uncaught.",
            ["D10"] = "When this is weak, the tests run the code but do not really check its behaviour, so they can pass while the software is in fact broken — giving false confidence.",
            ["D11"] = "When this is weak, the tests do not pass reliably, so failures get ignored as \"flaky\" and a real regression can hide among the noise.",
            ["D12"] = "When this is weak, the dependencies are out of date, insecure or bloated, so the project carries avoidable risk and is harder to keep current.",
            ["D13"] = "When this is weak, secrets such as keys, tokens or passwords are present in the code, where anyone with access to it can read and misuse them.",
            ["D14"] = "When this is weak, third-party package licenses have not been checked against your policy, so you may be using components on terms your organisation cannot accept.",
            ["P1"] = "When this is weak, there is no automated pipeline building and testing every change, so broken code can reach the main branch and problems are caught late, if at all.",
            ["P2"] = "When this is weak, the running system is hard to diagnose — no structured logging, tracing or health checks — so when something breaks in production you are flying blind.",
            ["P3"] = "When this is weak, security and performance tooling — SAST, secret/dependency scanning, benchmarks — is not wired in, so the project has no automated guard against these risks.",
            ["P4"] = "When this is weak, releases are not automated or safely reversible, so every deployment is a manual, error-prone event and rolling back a bad release is slow and risky.",
            ["P5"] = "When this is weak, there is no planned, codified disaster recovery — no backups, no recovery targets — so a serious failure could mean prolonged downtime or permanent data loss.",
            ["P6"] = "When this is weak, releases are not traceable — no maintained changelog or version stamping — so it is hard to tell which version is running or what changed between releases.",
            ["P7"] = "When this is weak, outbound calls to other services have no retry, timeout or circuit-breaker, so a single slow or failing dependency can cascade and take the whole system down.",
            ["P8"] = "When this is weak, database schema changes are not handled through versioned migrations, so updating an existing database safely becomes a manual, risky operation.",
            ["P9"] = "When this is weak, the tests focus on the trivial web layer instead of the business rules, so the parts that matter most to your operation are the least verified.",
            ["P10"] = "When this is weak, a library exposes a large, undisciplined public surface with no clear versioning, so consumers cannot depend on it without risking breakage on every update.",
            ["P11"] = "When this is weak, the project does not capture behaviour as executable specifications, so the shared, business-readable description of how the system should behave is missing.",
            ["R4"] = "When this is weak, large parts of the frontend are not reachable from any test, so changes there can break behaviour with nothing to catch it.",
            ["R5"] = "When this is weak, the npm dependencies are well out of date, so the project drifts further from current, supported versions and upgrades grow harder over time.",
            ["R6"] = "When this is weak, the frontend has no wired-up test, lint and typecheck scripts, so quality checks are not part of the normal workflow and are easily skipped.",
            ["R8"] = "When this is weak, the npm dependencies are not truthful — unused packages, undeclared imports, dev-only packages shipped as production — so the dependency list cannot be trusted and builds are fragile.",
            // Security & compliance
            ["D28"] = "When this is weak, secrets were committed somewhere in the project's history, so even if removed today they remain readable to anyone who can clone the repository.",
            ["D29"] = "When this is weak, automated static analysis finds likely security bugs in the code, meaning real, exploitable weaknesses are present in the delivery.",
            ["D30"] = "When this is weak, dependencies carry known published vulnerabilities (CVEs), so the delivery ships with publicly documented weaknesses an attacker can look up and exploit.",
            ["D31"] = "When this is weak, the Docker, Terraform or Kubernetes configuration does not follow security best practices, so the infrastructure itself is misconfigured and exposed.",
            ["D32"] = "When this is weak, the code likely handles personal data without proper safeguards — logging or storing it unprotected — exposing you to GDPR and privacy risk.",
            ["D33"] = "When this is weak, JavaScript/npm dependencies carry known published vulnerabilities (CVEs) — the npm ecosystem's biggest risk — so the frontend ships with documented, exploitable weaknesses.",
            ["D36"] = "When this is weak, the build pipeline lacks supply-chain integrity — no provenance, signing or SBOM, or unpinned actions — so a consumer can't verify how the artifact was built or whether it was tampered with.",
            ["D37"] = "When this is weak, there is no published vulnerability-disclosure policy (no SECURITY.md or security.txt with a reporting contact), so a researcher who finds a flaw has no documented way to report it to you.",
            ["C1"] = "When this is weak, sensitive data is not consistently encrypted at rest and in transit with keys properly vaulted, so that data is more exposed if the system is breached.",
            ["C2"] = "When this is weak, access is not authorised by default — endpoints lack proper role and policy enforcement — so data or actions may be reachable by people who should not have them.",
            ["C3"] = "When this is weak, changes to sensitive data are not recorded, so you cannot tell who changed what and when — undermining compliance and incident response.",
            ["C4"] = "When this is weak, data has no defined lifetime — no retention periods or cleanup — so personal and other data accumulates indefinitely, contrary to storage-limitation requirements.",
            ["C5"] = "When this is weak, GDPR data-subject requests are not supported in code — no erasure, export or consent handling — so meeting a lawful request becomes a manual, risky scramble.",
            ["S1"] = "When this is weak, the web application lacks basic protections — transport security, security headers, secure cookies, input validation — leaving it open to common web attacks.",
            // Domain modelling
            ["DM1"] = "When this is weak, the parts of the domain that must stay consistent reference each other directly instead of by identity, so the rules that keep your data correct are easier to break.",
            ["DM2"] = "When this is weak, much of the domain uses raw ids (plain strings, numbers, GUIDs) instead of strongly-typed ones, so it is easy to pass the wrong id and corrupt data without the compiler noticing.",
            ["DM3"] = "When this is weak, cross-context integration events leak internal domain types to their consumers, so independent parts of the system become tightly coupled and harder to change apart.",
            ["DM4"] = "When this is weak, the domain objects are little more than data bags whose rules live in external services, so the business rules that protect your data are scattered and easy to bypass.",
            ["DM5"] = "When this is weak, domain objects expose their internal state through public setters, so other code can change them in ways that skip the rules meant to keep them valid.",
            ["DM6"] = "When this is weak, the core business logic depends directly on technical infrastructure (database, HTTP, framework), so the rules at the heart of your system are hard to test and tied to specific technology.",
            ["DM7"] = "When this is weak, data access is not organised around the natural consistency boundaries, so the rules that protect your most important objects can be bypassed.",
            ["DM8"] = "When this is weak, groups of values that belong together are passed around as loose primitives, so the same validation has to be repeated everywhere and is easy to get wrong.",
            // Event-driven design
            ["ED1"] = "When this is weak, event handlers block on remote calls instead of staying asynchronous, so one slow downstream service can stall the whole flow of events.",
            ["ED2"] = "When this is weak, commands have several competing handlers and fan-out is not modelled with events, so it is unclear who actually owns each decision and behaviour becomes unpredictable.",
            ["ED3"] = "When this is weak, events are not named in the past tense, a small clarity issue that makes the flow of the system slightly harder to read at a glance.",
            ["ED4"] = "When this is weak, saving state and publishing a message are not atomic, so a crash at the wrong moment can leave the system having done one without the other — silently losing or duplicating work.",
            // Event sourcing
            ["ES1"] = "When this is weak, the logic that rebuilds state from events is not deterministic, so replaying the same history can produce different results — undermining the audit trail's trustworthiness.",
            ["ES2"] = "When this is weak, stored events can be modified after the fact, so the historical record is no longer a trustworthy account of what actually happened.",
            ["ES3"] = "When this is weak, personal data is written into the append-only event log with no way to erase it, creating a direct conflict with the GDPR right to be forgotten.",
            // LLM advisory compliance (advisory dimensions)
            ["LA1"] = "When this is weak, the code handles personal data the team hasn't yet confirmed is minimised, retention-limited and protected — a GDPR Art. 25/32 exposure flagged by AI for a human to review.",
            ["LA2"] = "When this is weak, images carry alt text that isn't a meaningful equivalent — filenames or generic words — so screen-reader users don't get the information the image conveys (WCAG 1.1.1), as judged by AI.",
            ["LA3"] = "When this is weak, there is no real coordinated vulnerability-disclosure policy (a security contact and a reporting process), so a researcher who finds a flaw has no safe way to report it (CRA/ISO), as judged by AI.",
            ["LA4"] = "When this is weak, development, test and production may share configuration and secrets rather than being separated, raising the risk that a test setting or credential reaches production (ISO A.8.31), as judged by AI.",
        };
}
