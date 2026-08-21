#!/usr/bin/env python3
"""Turn the published corpus into the PREPROD corpus, in place, before the preprod build.

    python3 deploy/preprod-corpus.py [path-to-manifest]

★★ WHY PREPROD NEEDS ITS OWN. Preprod already gets its own database, env, artifact root and port. It was
   still being built from PRODUCTION's corpus and signed with production's key, so its register accumulated
   submissions against the real 2026-09 holdout at the real pinned shas — rows distinguishable from
   production's only by which database they happened to land in. A test tier whose test data is the live data
   is not separated from production, it is co-located with it.

★★ BUILD-TIME SUBSTITUTION, NOT A RUNTIME LEVER, and that distinction is the entire safety argument. A config
   option letting a deployment point at some other corpus would put a switch in the PRODUCTION binary whose
   only purpose is defeating its own signature — the one artefact whose whole value is that it cannot be
   swapped. This rewrites the file before compiling, in the preprod workflow's own checkout, for the preprod
   artifact. deploy.yml never runs it; production never sees it. Exactly like swapping the database name.

★ DERIVED FROM THE REAL POOL, never a checked-in second copy. A parallel corpus committed beside the first
  drifts the moment the pool changes, and then preprod rehearses against a holdout that no longer resembles
  what production draws. Transforming the real one keeps the pool faithful and changes only what a test tier
  actually needs changed — which is three things.
"""
import json
import pathlib
import sys

MANIFEST = pathlib.Path(sys.argv[1] if len(sys.argv) > 1
                        else "src/Cai.Web/Noise/corpus/noise-corpus-1.0.json")

PREPROD_KEY_ID = "cai-corpus-preprod"

# ★ Far enough out that it is never reached by accident, and obviously a sentinel rather than a real date.
NEVER = "2099-01-01T00:00:00+00:00"


def transform(manifest: dict) -> dict:
    # 1. ITS OWN KEY ID. The signature chain becomes preprod's end to end, so a preprod artifact can never
    #    verify as a production one — and, more usefully, a production artifact can never verify as preprod's.
    #    Both directions are asserted in the deploy workflows.
    manifest["keyId"] = PREPROD_KEY_ID

    # 2. SAY SO IN THE DOCUMENT ITSELF. Anyone reading a preprod draw or a preprod register sees what it is in
    #    the first sentence, rather than inferring it from a hostname they may not have looked at.
    manifest["note"] = (
        "PREPROD CORPUS — a transformed copy of the published pool, signed with a key that exists only on "
        "the preprod tier. The repositories and shas are the real ones so the shape is faithful, but NOTHING "
        "here is a published draw, and no result measured over it means anything outside preprod. "
    ) + manifest.get("note", "")

    # 3. OPEN THE EMBARGO ON EVERY EXISTING DRAW, by publishing each at the moment it was drawn.
    #    ★★ THIS IS THE CHANGE THAT MAKES PREPROD USEFUL. The open register, the disputes on it, a second
    #    participant's row, the compliance marks and the twelve-month figure only exist once a period has
    #    published. On production's dates none of that is reachable until the calendar allows it, so a tier
    #    carrying production's dates can rehearse roughly half the standard and no more.
    for draw in manifest["draws"]:
        draw["publishesAt"] = draw["drawnAt"]

    # 4. AND KEEP ONE PERIOD EMBARGOED. A tier where nothing is ever withheld cannot test withholding, and
    #    the embargo is one of the four things 03 commits to as making the conflict of interest survivable.
    seed = manifest["draws"][0]["seed"]
    if not any(d["period"] == "2026-12" for d in manifest["draws"]):
        manifest["draws"].append({
            "period": "2026-12",
            "seed": seed,
            "drawnAt": "2026-11-15T00:00:00+00:00",
            "publishesAt": NEVER,
            "submissionsCloseAt": "2026-12-31T00:00:00+00:00",
        })

    return manifest


def main() -> int:
    if not MANIFEST.is_file():
        print(f"::error::no corpus manifest at {MANIFEST}", file=sys.stderr)
        return 1

    manifest = json.loads(MANIFEST.read_text())

    # ★ REFUSE TO RUN TWICE, or on something already transformed. Re-running would be harmless today, but a
    #   script that silently accepts its own output is one that will one day be pointed at production's tree
    #   by a copy-pasted command and report success.
    if manifest.get("keyId") == PREPROD_KEY_ID:
        print(f"{MANIFEST} is already the preprod corpus — nothing to do")
        return 0

    manifest = transform(manifest)
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n")

    periods = [(d["period"], d["publishesAt"]) for d in manifest["draws"]]
    print(f"preprod corpus written: keyId={manifest['keyId']}")
    for period, publishes in periods:
        state = "EMBARGOED" if publishes == NEVER else "published"
        print(f"  {period}  {state}  (publishesAt {publishes})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
