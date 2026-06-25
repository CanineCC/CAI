---
description: Work the Watchdog audit task list — fix findings via the watchdog MCP, verified by the next scan
---
Use the **watchdog** MCP server to work this repository's audit task list.

Target by IMPACT, not by severity. Clearing a pile of low-severity findings in a dimension already at 9/10 barely moves the grade — the score moves on a handful of high-weight dimensions.

Loop:
1. Call `audit` — the grade, each lens, and the remediation plan ranked by impact ÷ effort. It tells you what actually moves the score.
2. Call `next_task` — the single highest-leverage work item, fully briefed: what the dimension measures, its current /10, the estimated point-gain + effort, **what to do**, and the concrete instances to fix (each with a file:line and a fingerprint). (Or `get_task("D34")` for a specific dimension.) One call gives you everything — no need to chase findings separately.
3. Fix the instances in the code with a minimal, focused change. `resolve_finding` each (a short note on what you changed) — optionally `claim_finding` first if other agents share the repo. Resolution is **provisional**: the next scan is the arbiter, so don't claim a fix you didn't actually make. Point-gains are estimates; only the rescan confirms movement.
4. Repeat until `next_task` returns nothing, then `request_rescan` (if this repo allows it) to verify.

Rules:
- Work the highest-impact task first; keep diffs small and reviewable.
- If a SCORED finding looks wrong, call `dispute_finding` with concrete reasoning instead of forcing a change — a human decides. Never try to make a finding disappear without fixing it.
- If an **advisory** finding (`advisory: true` — LLM-judged, does not affect your score) looks wrong, it can't be disputed; use `flag_advisory` with a note so we can improve the model. (`dispute_finding` will reject it and point you here.)
- Suppressing or accepting a finding is a human decision, never yours.
- If you spot a real problem Watchdog missed, `report_detector_gap` with evidence.

When you're done, summarize what you fixed and anything you couldn't (and why). If this repo allows it, you may call `request_rescan` to verify; otherwise tell me to run a scan.
