#!/usr/bin/env bash
# Sign the noise corpus manifest. Runs at DEPLOY time, on the server, with a key that never leaves it.
#
#   tools/sign-corpus.sh                      # uses $CAI_CORPUS_SIGNING_KEY, or the default path below
#   tools/sign-corpus.sh /path/to/key.pem     # explicit
#
# ★★ A DEPLOY STEP, DELIBERATELY NOT AN API ENDPOINT. An endpoint that can sign means whoever can call it can
#    re-sign a TAMPERED corpus — and the thing doing the verifying becomes the thing being checked. Here the key
#    is on the box, out of the repository, and no request can reach it.
#
# ★★ IT GENERATES THE KEY IF THERE IS NONE, so provisioning is not a manual step somebody has to remember on a
#    new host. The public half is written into the source tree under the manifest's own keyId, so the artifact
#    ships with the key a third party verifies against.
#
# ★ WHAT THIS SIGNATURE IS WORTH, said plainly: the key is generated on and never leaves the CAI host, so a
#   verifying signature proves the manifest has not changed since the deploy that produced it. It does NOT prove
#   an independent party vouched for it — that needs an offline key or a KMS, and the format does not change when
#   it happens.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DIR="$ROOT/src/Cai.Web/Noise/corpus"
MANIFEST="$DIR/noise-corpus-1.0.json"
SIG="$MANIFEST.sig"

KEY="${1:-${CAI_CORPUS_SIGNING_KEY:-$HOME/.cai-signing/cai-corpus.key.pem}}"

# ★ The public key's filename comes from the manifest's OWN keyId, so rotating the key is one edit in one file
#   and the app finds the right key without a second place to update.
KEY_ID="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["keyId"])' "$MANIFEST")"
PUB="$DIR/$KEY_ID.pub.pem"

if [ ! -f "$KEY" ]; then
  echo "no signing key at $KEY — generating one (it will not leave this host)"
  mkdir -p "$(dirname "$KEY")"
  openssl ecparam -name prime256v1 -genkey -noout -out "$KEY"
  chmod 600 "$KEY"
fi

# ★ Exported every run: a public key that drifted from the private one fails verification at startup, and the
#   app fails CLOSED — so a stale committed pem would take the standard down rather than mislead anyone. Writing
#   it here removes that failure mode entirely.
openssl ec -in "$KEY" -pubout -out "$PUB" 2>/dev/null

openssl dgst -sha256 -sign "$KEY" -out "$SIG" "$MANIFEST"

# ★ VERIFY WHAT WAS JUST WRITTEN, against the PUBLISHED public key rather than the private one. Signing with the
#   wrong key produces a valid signature that nothing else can check — the failure this line exists to catch.
if ! openssl dgst -sha256 -verify "$PUB" -signature "$SIG" "$MANIFEST"; then
  echo "the new signature does not verify against $PUB — wrong key?" >&2
  exit 1
fi

echo "signed $(basename "$MANIFEST") with $KEY_ID"
