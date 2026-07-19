using System.Globalization;
using Cai.Delivery;
using Cai.Scoring;
using Microsoft.Extensions.Logging;

// The CAI reference CLI. Two halves of the standard:
//   scoring   — `cai score` / `cai verify` fold an evidence bundle to the open, reproducible 0–100 CAI.
//   delivery  — `cai keygen` / `cai sign` / `cai verify-delivery` mint and check the signed, shareable CAI-delivery
//               package (Ed25519). `verify-delivery` is the buyer-side offline trust check.
// Exit codes: 0 ok, 1 verify/verify-delivery mismatch, 2 usage/IO error.

// Structured, diagnosable logging — all to stderr (so stdout stays the clean, machine-readable result), quiet by
// default and lifted to Information when CAI_LOG is set. Lets a CI step trace what the scorer did without parsing stdout.
using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)
    .SetMinimumLevel(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CAI_LOG"))
        ? LogLevel.Warning
        : LogLevel.Information));
var log = loggerFactory.CreateLogger("cai");

var commands = new[] { "score", "verify", "keygen", "sign", "verify-delivery" };
if (args.Length < 1 || !commands.Contains(args[0]))
{
    Console.Error.WriteLine(
        """
        cai — the Codebase Assurance Index reference tools

        Scoring (open, reproducible fold):
          cai score  <evidence.json>                    compute the CAI from an evidence bundle
          cai verify <evidence.json> [--expect N]       reproduce the published headline (exit 1 on mismatch)

        Delivery (signed, shareable evidence artifact — Ed25519):
          cai keygen <keyId> [--out keypair.json]       generate a cai signing key pair (private — keep secret)
          cai sign   <evidence.json> --key keypair.json --repo NAME [--commit X] [--producer NAME]
                     [--scanner S] [--id ID] [--issued-at TS] [--out package.json]
                                                         recompute + sign a CAI-delivery package
          cai verify-delivery <package.json> --keys keys.json [--no-reproduce]
                                                         verify signature (+ reproduce headline); exit 1 on failure

        See cai.canine.dev/spec (scoring) and cai.canine.dev/spec/delivery (package + registry).
        """);
    return 2;
}

try
{
    return args[0] switch
    {
        "score" => await RunScore(args, log).ConfigureAwait(false),
        "verify" => await RunVerify(args, log).ConfigureAwait(false),
        "keygen" => RunKeygen(args, log),
        "sign" => await RunSign(args, log).ConfigureAwait(false),
        "verify-delivery" => await RunVerifyDelivery(args, log).ConfigureAwait(false),
        _ => 2,
    };
}
catch (Exception e)
{
    log.LogDebug(e, "Unhandled error running {Command}", args[0]);
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}

// ── scoring ─────────────────────────────────────────────────────────────────────────────────────────────────────
async Task<int> RunScore(string[] a, ILogger l)
{
    if (await ReadBundle(a, l).ConfigureAwait(false) is not { } bundle)
    {
        return 2;
    }

    var s = CaiScorer.Score(bundle);
    l.LogInformation("Scored {Path}: CAI {Headline:0.0} ({Band}) over {LensCount} lens(es)",
        a[1], s.Headline, s.Band.Label(), s.Lenses.Count);
    Console.WriteLine($"CAI {s.Headline:0.0} ({s.Band.Label()})  ·  rubric {Rubric(bundle)}");
    Console.WriteLine();
    Console.WriteLine($"  {"lens",-22}{"score",7}{"band",-12}{"dims",6}{"weight",9}{"contrib",10}");
    foreach (var lens in s.Lenses.OrderByDescending(x => x.Contribution))
    {
        var band = lens.Band.Label() + (lens.CriticalGated ? "*" : "");
        Console.WriteLine($"  {lens.Lens,-22}{lens.Score,7:0.0}  {band,-12}{lens.ItemCount,4}{lens.Weight,9:0.000}{lens.Contribution,10:0.00}");
    }

    if (s.Lenses.Any(x => x.CriticalGated))
    {
        Console.WriteLine("\n  * band capped at Fair — a dimension below 4.0/10 critical-gates the lens.");
    }

    return 0;
}

async Task<int> RunVerify(string[] a, ILogger l)
{
    if (await ReadBundle(a, l).ConfigureAwait(false) is not { } bundle)
    {
        return 2;
    }

    var expect = ParseDouble(a, "--expect");
    var toVerify = expect is { } e ? bundle with { HeadlineScore = e } : bundle;
    if (toVerify.HeadlineScore is null)
    {
        Console.Error.WriteLine("error: bundle has no headlineScore and no --expect N given; nothing to verify against.");
        return 2;
    }

    var v = CaiScorer.Verify(toVerify);
    l.LogInformation("Verified {Path}: reproduced={Reproduced} computed={Computed:0.00} claimed={Claimed:0.00}",
        a[1], v.Reproduced, v.Computed, v.Claimed);
    if (v.Reproduced)
    {
        Console.WriteLine($"✓ reproduced: CAI {v.Computed:0} ({Bands.For(v.Computed).Label()}) under rubric {Rubric(bundle)} (claimed {v.Claimed:0}, Δ{v.Delta:0.00})");
        return 0;
    }

    Console.WriteLine($"✗ MISMATCH: evidence folds to {v.Computed:0.00} but the bundle claims {v.Claimed:0.00} (Δ{v.Delta:0.00} > {v.Tolerance:0.00})");
    return 1;
}

// ── delivery ────────────────────────────────────────────────────────────────────────────────────────────────────
int RunKeygen(string[] a, ILogger l)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("error: keygen needs a keyId, e.g. `cai keygen cai-ed25519-2026-07`.");
        return 2;
    }

    var keyId = a[1];
    var pair = DeliveryKeyPair.Generate(keyId);
    var outPath = ParseString(a, "--out");
    if (outPath is not null)
    {
        File.WriteAllText(outPath, pair.ToJson());
        Console.Error.WriteLine($"wrote signing key pair (SECRET — do not commit) to {outPath}");
    }
    else
    {
        Console.Error.WriteLine("# signing key pair (SECRET — do not commit); pass --out to save it:");
        Console.Error.WriteLine(pair.ToJson());
    }

    // stdout carries the PUBLISHABLE public key set — safe to redirect to keys.json.
    var set = new DeliveryPublicKeySet { Keys = [pair.ToPublicKey()] };
    Console.WriteLine(set.ToJson());
    l.LogInformation("Generated Ed25519 key {KeyId}", keyId);
    return 0;
}

async Task<int> RunSign(string[] a, ILogger l)
{
    if (await ReadBundle(a, l).ConfigureAwait(false) is not { } bundle)
    {
        return 2;
    }

    var keyPath = ParseString(a, "--key");
    var repo = ParseString(a, "--repo");
    if (keyPath is null || repo is null)
    {
        Console.Error.WriteLine("error: sign needs --key <keypair.json> and --repo <name>.");
        return 2;
    }

    var pair = DeliveryKeyPair.Parse(await File.ReadAllTextAsync(keyPath).ConfigureAwait(false));
    var request = new DeliveryBuildRequest
    {
        DeliveryId = ParseString(a, "--id") ?? $"cd_{repo.Replace('/', '_')}_{bundle.Commit ?? "head"}",
        // The CLI stamps a wall-clock time when --issued-at is absent; pass --issued-at for a reproducible artifact.
        // InvariantCulture is load-bearing, not stylistic: ':' in a custom format string is the CULTURE's time
        // separator, so on a machine with (say) a Danish locale this silently emitted "2026-07-19T09.28.09Z" — an
        // invalid RFC 3339 timestamp, baked into a signed artifact where it cannot be corrected after the fact.
        IssuedAt = ParseString(a, "--issued-at")
                   ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        Subject = new DeliverySubject { Repository = repo, Commit = ParseString(a, "--commit"), Host = ParseString(a, "--host") },
        Producer = new DeliveryProducer
        {
            Name = ParseString(a, "--producer") ?? "watchdog.canine.dev",
            Scanner = ParseString(a, "--scanner"),
            ScannerVersion = ParseString(a, "--scanner-version"),
        },
    };

    var payload = DeliveryBuilder.Build(bundle, request);
    using var signer = new DeliverySigner(pair);
    var package = signer.SignPackage(payload);
    var json = package.ToJson();

    var outPath = ParseString(a, "--out");
    if (outPath is not null)
    {
        await File.WriteAllTextAsync(outPath, json).ConfigureAwait(false);
        Console.Error.WriteLine($"wrote signed CAI-delivery to {outPath}  ·  CAI {package.Payload.Verdict.Cai:0.0} ({package.Payload.Verdict.Band})  ·  key {package.Signature.KeyId}");
    }
    else
    {
        Console.WriteLine(json);
    }

    l.LogInformation("Signed delivery {Id} for {Repo}: CAI {Cai:0.0}", package.Payload.DeliveryId, repo, package.Payload.Verdict.Cai);
    return 0;
}

async Task<int> RunVerifyDelivery(string[] a, ILogger l)
{
    if (a.Length < 2 || !File.Exists(a[1]))
    {
        Console.Error.WriteLine($"error: no such package file: {(a.Length < 2 ? "(none)" : a[1])}");
        return 2;
    }

    var keysPath = ParseString(a, "--keys");
    if (keysPath is null)
    {
        Console.Error.WriteLine("error: verify-delivery needs --keys <keys.json> (cai's published public key set).");
        return 2;
    }

    var package = DeliveryPackage.Parse(await File.ReadAllTextAsync(a[1]).ConfigureAwait(false));
    var keys = DeliveryPublicKeySet.Parse(await File.ReadAllTextAsync(keysPath).ConfigureAwait(false));
    var reproduce = !a.Contains("--no-reproduce");

    var r = DeliveryVerifier.Verify(package, keys, reproduce);
    l.LogInformation("Verified delivery {Id}: signatureValid={Valid} reproduced={Reproduced}",
        package.Payload.DeliveryId, r.SignatureValid, r.Reproduced);

    if (!r.SignatureValid)
    {
        Console.WriteLine($"✗ SIGNATURE INVALID: {r.Reason}");
        return 1;
    }

    var p = package.Payload;
    Console.WriteLine($"✓ signature verified — signed by {p.Issuer.Name} (key {p.Issuer.KeyId}), Ed25519");
    Console.WriteLine($"  subject   {p.Subject.Repository}{(p.Subject.Commit is { } c ? $" @ {c}" : "")}");
    Console.WriteLine($"  verdict   CAI {p.Verdict.Cai:0.0} ({p.Verdict.Band})  ·  rubric {p.RubricVersion}  ·  issued {p.IssuedAt}");
    if (r.Reproduced is { } rep)
    {
        Console.WriteLine(rep
            ? $"✓ headline reproduces from embedded evidence ({r.ComputedCai:0.0} = claimed {r.ClaimedCai:0.0})"
            : $"✗ headline does NOT reproduce: {r.Reason}");
        if (!rep)
        {
            return 1;
        }
    }

    return 0;
}

// ── shared helpers ──────────────────────────────────────────────────────────────────────────────────────────────
async Task<EvidenceBundle?> ReadBundle(string[] a, ILogger l)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("error: this command needs an <evidence.json> path.");
        return null;
    }

    if (!File.Exists(a[1]))
    {
        Console.Error.WriteLine($"error: no such file: {a[1]}");
        return null;
    }

    try
    {
        var bundle = EvidenceBundle.Parse(await File.ReadAllTextAsync(a[1]).ConfigureAwait(false));
        l.LogInformation("Parsed evidence bundle from {Path}: {DimensionCount} dimension(s), rubric {RubricVersion}",
            a[1], bundle.Dimensions.Count, Rubric(bundle));
        return bundle;
    }
    catch (Exception e)
    {
        l.LogDebug(e, "Failed to parse evidence bundle from {Path}", a[1]);
        Console.Error.WriteLine($"error: could not parse evidence bundle: {e.Message}");
        return null;
    }
}

static string Rubric(EvidenceBundle b) => string.IsNullOrWhiteSpace(b.RubricVersion) ? "(unstated)" : b.RubricVersion;

static string? ParseString(string[] args, string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static double? ParseDouble(string[] args, string flag) =>
    ParseString(args, flag) is { } s && double.TryParse(s, out var n) ? n : null;
