#!/usr/bin/env bash
# Re-sign the noise corpus manifest after editing it.
#
#   tools/sign-corpus.sh ~/.cai-signing/cai-corpus-dev-2026-08.key.pem
#
# ★★ THE SIGNATURE COVERS THE FILE'S EXACT BYTES. Edit the manifest, run this, commit both files together. A
#    manifest committed without its new signature makes the service fail closed — deliberately, because the
#    alternative is serving a pool nobody can check.
# ★ The private key lives OUTSIDE the repository. Its custody is an open decision; see
#   src/Cai.Web/Noise/corpus/VERIFYING-THE-CORPUS.md.
set -euo pipefail

KEY="${1:-}"
if [ -z "$KEY" ] || [ ! -f "$KEY" ]; then
  echo "usage: $0 <private-key.pem>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DIR="$ROOT/src/Cai.Web/Noise/corpus"
MANIFEST="$DIR/noise-corpus-1.0.json"
SIG="$DIR/noise-corpus-1.0.json.sig"
PUB="$DIR/cai-corpus-dev-2026-08.pub.pem"

openssl dgst -sha256 -sign "$KEY" -out "$SIG" "$MANIFEST"

# ★ VERIFY WHAT WAS JUST WRITTEN, against the PUBLISHED public key rather than the private one. Signing with the
#   wrong key produces a valid signature that nothing else can check — the failure this line exists to catch.
if ! openssl dgst -sha256 -verify "$PUB" -signature "$SIG" "$MANIFEST"; then
  echo "the new signature does not verify against $PUB — wrong key?" >&2
  exit 1
fi

echo "signed: $(basename "$MANIFEST") → $(basename "$SIG")"
