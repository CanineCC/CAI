# Verifying the noise corpus

The candidate pool and the periods drawn from it are published as a **signed manifest**. This directory holds
everything needed to check it, and nothing else is required — no CAI code runs, and no endpoint is trusted.

| File | What it is |
| --- | --- |
| `noise-corpus-1.0.json` | the manifest: the rules, the draws with their seeds and timestamps, and the pool |
| `noise-corpus-1.0.json.sig` | a detached signature over that file's **exact bytes** |
| `cai-corpus-dev-2026-08.pub.pem` | the public key the signature verifies against |

## Check it

```sh
openssl dgst -sha256 \
  -verify cai-corpus-dev-2026-08.pub.pem \
  -signature noise-corpus-1.0.json.sig \
  noise-corpus-1.0.json
```

It prints `Verified OK`, or `Verification Failure`. Algorithm: ECDSA P-256 with SHA-256.

## Why it is done this way

**The pool is public or the draw is unverifiable.** Re-deriving a holdout needs the seed *and* the pool it was
drawn from. Publishing only the seed proves nothing, because the pool could have been chosen after the fact.

**The signature covers the file's bytes, not a canonical form of its contents.** Reformatting the JSON breaks
verification, deliberately: "the bytes that were signed" is the only definition that cannot be argued with, and a
canonicalising serialiser is one more thing that can differ between the signer and the checker. If you need to
change the manifest, change it and **re-sign it** — see below.

**There is no endpoint that reports whether the signature is valid.** That would be the standard attesting to its
own signature, which is evidence of nothing. `/api/noise/corpus` and `/api/noise/holdout/{period}` publish the
manifest version, the key id, the algorithm and the signature itself, so you can check them here.

**The service fails closed.** If the shipped manifest does not verify, both endpoints answer `503` and serve no
draw at all. A holdout endpoint that quietly degraded to "here is the pool, unsigned" would be worse than one
that stops, because the degradation is invisible in the thing it hands back.

## ★★ The key is a DEVELOPMENT key

`cai-corpus-dev-2026-08` is a development key. The mechanism is complete and the signature verifies, but the
custody of a production signing key — who holds it and who can use it — has not been decided, and that decision
is the whole value of a signature. The key id says `dev` so a reader learns this rather than assuming. Treat a
`dev`-keyed signature as evidence that the manifest has not changed **since it was built**, and not as evidence
about who built it.

## Re-signing after a change

```sh
tools/sign-corpus.sh <path-to-private-key.pem>
```

The private key is **not in this repository** and must not be. The script signs the manifest in place and
verifies the result before it finishes.
