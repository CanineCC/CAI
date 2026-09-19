# Examples

Sample inputs for the CLI, the calculator and the delivery spec.

| File | What it is |
|------|------------|
| `evidence.sample.json` | A CAI evidence bundle — the input to `cai score` / `cai verify`. |
| `cai-delivery.sample.json` | A signature-valid [CAI-delivery package](../docs/spec/cai-delivery-package.md). |
| `cai-delivery.keys.json` | The **public** key set that sample verifies against, offline. |

```sh
# From this directory. --rubrics points at the published archive so the headline is re-folded, not taken on trust.
dotnet run --project ../src/Cai.Cli -- verify-delivery cai-delivery.sample.json \
    --keys cai-delivery.keys.json --rubrics ../rubrics
# ✓ signature verified — signed by cai.canine.dev (key cai-ed25519-sample), Ed25519
# ✓ headline reproduces from embedded evidence (70,3 = claimed 70,3)
```

## About the sample signing key

The sample is signed under key id **`cai-ed25519-sample`**, which production does not and must not trust. The
three delivery files are emitted together by [`tools/resign-sample`](../tools/resign-sample), which mints a
fresh keypair on every run — so regenerating the example is how you reproduce it, and the private half is of no
use to anyone: it can mint nothing the registry accepts.

That private half used to be published here as `cai-delivery.sample-key.json`. It is not any more. A committed
private key reads as a leak to every scanner and every reader, whichever it actually is, and verifying the
sample only ever needed the public half above. The tool now writes the seed outside the repository and prints
where it put it.

Rotating a **real** signing key is a different procedure, and it lives in
[`deploy/registry/DEPLOY.md`](../deploy/registry/DEPLOY.md).
