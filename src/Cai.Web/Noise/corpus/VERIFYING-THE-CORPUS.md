# Verifying the noise corpus

The candidate pool and the periods drawn from it are published as a **signed manifest**. This directory holds
everything needed to check it, and nothing else is required — no CAI code runs, and no endpoint is trusted.

| File | What it is |
| --- | --- |
| `noise-corpus-1.0.json` | the manifest: the rules, the draws with their seeds and timestamps, and the pool |
| `noise-corpus-1.0.json.sig` | a detached signature over that file's **exact bytes** |
| `cai-corpus-2026-08.pub.pem` | the public key the signature verifies against (named by the manifest's `keyId`) |

## Check it

```sh
openssl dgst -sha256 \
  -verify cai-corpus-2026-08.pub.pem \
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

## ★★ What this signature is worth — and what it is not

The signing key is **generated on, and never leaves, the CAI production host**, and the manifest is signed by the
**deploy** that ships it. So a verifying signature proves:

> this manifest has not changed since the deploy that produced it.

It does **not** prove that an independent party vouched for it. Who holds a key is what a signature is worth, and
this one is held by the same operator who runs the service. Upgrading to an offline key or a hosted KMS changes
who can produce a signature — it changes nothing about the format, the file names, or the command above.

The manifest states this itself, in `keyCustody`, and `/api/noise/corpus` publishes it beside the signature.

## Re-signing after a change

You do not need to. **The deploy signs the corpus before it builds**, with the key on the server:

```yaml
- name: Sign the noise corpus
  run: tools/sign-corpus.sh
  env:
    CAI_CORPUS_SIGNING_KEY: /home/jimmy/apps/cai-web/registry-data/cai-corpus.key.pem
```

Edit the manifest, commit it, deploy — the signature and the public key are regenerated from the key on the box,
before the tests run. The key is created on first use and then reused, so a fresh host provisions itself.

To sign locally (for a test run against an edited manifest), the same script works with any key:

```sh
tools/sign-corpus.sh ~/.cai-signing/cai-corpus.key.pem
```

The private key is **not in this repository** and must not be. Signing is deliberately **not** an API endpoint:
an endpoint that can sign means whoever can call it can re-sign a tampered corpus, and the thing doing the
verifying becomes the thing being checked.
