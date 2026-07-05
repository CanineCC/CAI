# Challenge & review — how to dispute a CAI

CAI earns authority only if it can be argued with. A standard nobody can contest is a reputation, not a measurement.
This document is the map: **what** you can dispute, **where** it goes, and **who** decides. It exists because the honest
answer depends on *which* part of the number you think is wrong — the arithmetic, the reference scorer, the measurement,
or the rubric itself — and those four have four different, deliberately separate review paths.

The one invariant behind all of them: **same evidence + same rubric version ⇒ the same score** — deterministically,
byte-for-byte ([ADR-0002](adr/0002-deterministic-reproducible-scoring.md)). That is what makes CAI *falsifiable*, and a
falsifiable claim is one you can win an argument against with proof rather than opinion.

## At a glance

| You think… | That's a dispute about… | Where it goes | Who decides |
|---|---|---|---|
| "The number doesn't follow from the evidence." | the **arithmetic** of a published score | reproduce it yourself — [`/verify`](https://cai.canine.dev/verify) / `cai verify` | nobody — it's mechanical & falsifiable |
| "The open scorer disagrees with the published spec." | a **reference-scorer bug** | an issue on [CanineCC/CAI](https://github.com/CanineCC/CAI/issues) | the standard's maintainers, in the open |
| "This finding / deduction mis-measures my code." | a **measurement** in one signed survey | the **issuer** of that survey (e.g. [watchdog.canine.dev](https://watchdog.canine.dev)) | the named human who attested it |
| "This dimension / weight / formula is unfair." | the **rubric or methodology** | an issue or PR on [CanineCC/CAI](https://github.com/CanineCC/CAI/issues) | the standard's maintainers → a *future* rubric version |

## 1. "The number doesn't follow from the evidence." — reproduce it

This is the strongest challenge there is, because it needs no permission and no adjudicator. Every published survey
states its **rubric version** and links its **evidence bundle** (the measured dimensions and their published OWA
weights). Fold that evidence through the open scorer and you get either the same number or proof of a discrepancy:

```
cai verify survey-evidence.json --expect 72.2      # exit 0 = reproduced, exit 1 = mismatch
```

or paste the bundle into the [calculator / verifier](https://cai.canine.dev/calculator) — when the bundle carries a
`headlineScore`, it shows the reproduction verdict (✓ / ✗) inline. A mismatch is **falsifiable proof** the published
number doesn't follow from its evidence: no committee has to agree with you. If the survey is also a signed
[CAI-delivery package](spec/cai-delivery-package.md), the same file lets you check *who* attested it (Ed25519) as well
as *whether the math holds* — authenticity and honesty are two independent checks
([ADR-0010](adr/0010-signed-cai-delivery-package-and-registry.md)).

If a number won't reproduce, that is not a matter of opinion — it is either tampering (the signature fails) or a scorer
bug (§2). Take it to the issuer, or, if the open scorer itself is at fault, to §2.

## 2. "The open scorer disagrees with the spec." — that's a bug

Reproducibility is a **contract**, not a hope: the reference scorer in [`src/Cai.Scoring`](../src/Cai.Scoring) must
produce exactly the number the [published algorithm](https://cai.canine.dev/spec) describes. If you can fold an evidence
bundle and get a headline the spec doesn't predict — a lens roll-up, an OWA weight, a critical-gate band, a rounding
edge that disagrees with the written method — **that is a defect in the standard**, and we want it.

Open an issue on [CanineCC/CAI](https://github.com/CanineCC/CAI/issues) with the **minimal evidence bundle that
reproduces it**, the rubric version, the number you got, and the number the spec implies. Because the fold is
deterministic, a reproduction case is complete on its own — anyone can confirm it with `cai score`. Fixes to the scorer
that change any published number are handled the same disciplined way as any rubric change (§4): the old versions stay
frozen and served, so a fix never silently rewrites history.

## 3. "This finding mis-measures my code." — take it to the issuer

This is the dispute most people mean, and it is deliberately **not** the standard's to settle. CAI's math is open and
mechanical; the **measurement** — "was this dimension scored correctly for *this* repo?", "is this flagged CVE actually
reachable?", "this is dead scaffolding, not a God-class" — is the surveyor's judgment call, attested by a **named
human**. Disputing a measurement is therefore a conversation with **the issuer of that survey**, not with cai.

- For surveys issued by **[watchdog.canine.dev](https://watchdog.canine.dev)**, dispute a scored finding through the
  surveyor's own review path (its agent tooling exposes a `dispute_finding` action; a human adjudicates — a finding is
  never made to disappear without a fix or a documented reason). Advisory, model-judged reads don't affect the number
  and are flagged for model improvement rather than disputed.
- The **free/paid firewall** ([ADR-0003](adr/0003-free-paid-firewall.md)) is why this split exists: the deterministic
  number is open and independent; the survey — the deductions and what to do about them — is the surveyor's product, and
  the surveyor stands behind it. What you *cannot* do is make a scored, reproducible deduction vanish by objecting to it:
  the standard guarantees a suppressed or "accepted" finding changes what a **report says**, never the **CAI**. To move
  the number, fix the code — or challenge the rubric (§4).

The standard's job here is to make measurement disputes *possible* by nailing down the rules the issuer must follow:
a dimension measured at confidence 0 is **absent, never a raw 0** ("not assessed" must never read as "failed"); advisory
(LLM) dimensions are band-only and **never** touch the deterministic number; every critical-gated band must name the
contributor that capped it ("gated by C1"), never hide behind an anonymous flag.

## 4. "This dimension / weight / formula is unfair." — challenge the rubric

If your quarrel is with the **method itself** — a dimension that shouldn't count, an OWA decay that punishes too hard, a
formula that's volume-biased, a lens that's mis-weighted — then you're challenging the rubric, and that is a first-class,
welcome contribution to an *open* standard. Open an issue or a PR on
[CanineCC/CAI](https://github.com/CanineCC/CAI/issues) making the case, ideally with a reproduction bundle showing the
skew.

Two properties of the standard make this safe to do and safe to accept:

- **Rubrics are frozen and versioned** ([ADR-0004](adr/0004-versioned-frozen-rubrics.md)). An accepted change lands in a
  **new** rubric version; it never mutates a rubric that scores already cite. Every past version stays published and
  served at [`/api/rubrics`](https://cai.canine.dev/api/rubrics), so every past number remains reproducible to the exact
  definitions it was computed under. Your challenge can improve the standard **without** retroactively rewriting anyone's
  score.
- **The change is public.** Rubric evolution happens in the open — in issues, PRs, the [CHANGELOG](../CHANGELOG.md), and,
  for anything structural, an [ADR](adr/README.md). A report can cite "rubric-2026.08.15" and a reader can see exactly
  what that version does, and what changed since.

## Who decides, and how fast

- **Reproduction (§1)** is decided by **nobody** — it's arithmetic. If it doesn't reproduce, you're right, and the proof
  travels with the bundle.
- **Scorer bugs (§2)** and **rubric challenges (§4)** are adjudicated by the **standard's maintainers in the open**, on
  the public issue tracker, with the outcome recorded in the CHANGELOG (and an ADR when it's a decision, not just a fix).
  There is no private channel and no appeal to authority: the argument is won in public with a reproduction case.
- **Measurement disputes (§3)** are decided by the **issuer of the survey** — the named human who attested it — under
  that issuer's published review path.

We don't promise a turnaround SLA on an open-source issue tracker, but every dispute above is designed so the *evidence*
is self-contained: a reproduction bundle is complete without us, which is the whole point of a falsifiable standard.

## Reporting a security issue

A vulnerability in the site or the reference implementation is not a scoring dispute — please report it privately via
GitHub Security Advisories on [CanineCC/CAI](https://github.com/CanineCC/CAI/security/advisories/new) rather than a
public issue, so it can be fixed before disclosure.
