using Cai.Scoring;
using Microsoft.Extensions.Logging;

// The CAI reference CLI: `cai score <evidence.json>` and `cai verify <evidence.json> [--expect N]`.
// Reads an evidence bundle, folds the lenses worst-first into the 0–100 CAI, and bands it — the open, reproducible
// half of the standard. Exit codes: 0 ok, 1 verify mismatch, 2 usage/IO error.

// Structured, diagnosable logging — all to stderr (so stdout stays the clean, machine-readable result), quiet by
// default and lifted to Information when CAI_LOG is set. Lets a CI step trace what the scorer did without parsing stdout.
using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)
    .SetMinimumLevel(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CAI_LOG"))
        ? LogLevel.Warning
        : LogLevel.Information));
var log = loggerFactory.CreateLogger("cai");

if (args.Length < 2 || args[0] is not ("score" or "verify"))
{
    Console.Error.WriteLine(
        """
        cai — the Codebase Assurance Index reference scorer

        Usage:
          cai score   <evidence.json>                 compute the CAI from an evidence bundle
          cai verify  <evidence.json> [--expect N]    reproduce the published headline (exit 1 on mismatch)

        An evidence bundle is the open record a score is computed from (see cai.canine.dev/spec).
        """);
    return 2;
}

var command = args[0];
var path = args[1];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"error: no such file: {path}");
    return 2;
}

EvidenceBundle bundle;
try
{
    bundle = EvidenceBundle.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
    log.LogInformation("Parsed evidence bundle from {Path}: {DimensionCount} dimension(s), rubric {RubricVersion}",
        path, bundle.Dimensions.Count, Rubric(bundle));
}
catch (Exception e)
{
    log.LogDebug(e, "Failed to parse evidence bundle from {Path}", path);
    Console.Error.WriteLine($"error: could not parse evidence bundle: {e.Message}");
    return 2;
}

try
{
    if (command == "score")
    {
        var s = CaiScorer.Score(bundle);
        log.LogInformation("Scored {Path}: CAI {Headline:0.0} ({Band}) over {LensCount} lens(es)",
            path, s.Headline, s.Band.Label(), s.Lenses.Count);
        Console.WriteLine($"CAI {s.Headline:0.0} ({s.Band.Label()})  ·  rubric {Rubric(bundle)}");
        Console.WriteLine();
        Console.WriteLine($"  {"lens",-22}{"score",7}{"band",-12}{"dims",6}{"weight",9}{"contrib",10}");
        foreach (var l in s.Lenses.OrderByDescending(l => l.Contribution))
        {
            var band = l.Band.Label() + (l.CriticalGated ? "*" : "");
            Console.WriteLine($"  {l.Lens,-22}{l.Score,7:0.0}  {band,-12}{l.ItemCount,4}{l.Weight,9:0.000}{l.Contribution,10:0.00}");
        }

        if (s.Lenses.Any(l => l.CriticalGated))
        {
            Console.WriteLine("\n  * band capped at Fair — a dimension below 4.0/10 critical-gates the lens.");
        }

        return 0;
    }

    // verify
    var expect = ParseExpect(args);
    var toVerify = expect is { } e2 ? bundle with { HeadlineScore = e2 } : bundle;
    if (toVerify.HeadlineScore is null)
    {
        Console.Error.WriteLine("error: bundle has no headlineScore and no --expect N given; nothing to verify against.");
        return 2;
    }

    var v = CaiScorer.Verify(toVerify);
    log.LogInformation("Verified {Path}: reproduced={Reproduced} computed={Computed:0.00} claimed={Claimed:0.00} delta={Delta:0.00}",
        path, v.Reproduced, v.Computed, v.Claimed, v.Delta);
    if (v.Reproduced)
    {
        Console.WriteLine($"✓ reproduced: CAI {v.Computed:0} ({Bands.For(v.Computed).Label()}) under rubric {Rubric(bundle)} (claimed {v.Claimed:0}, Δ{v.Delta:0.00})");
        return 0;
    }

    Console.WriteLine($"✗ MISMATCH: evidence folds to {v.Computed:0.00} but the bundle claims {v.Claimed:0.00} (Δ{v.Delta:0.00} > {v.Tolerance:0.00})");
    return 1;
}
catch (Exception e)
{
    log.LogDebug(e, "Unhandled error running {Command} on {Path}", command, path);
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}

static string Rubric(EvidenceBundle b) => string.IsNullOrWhiteSpace(b.RubricVersion) ? "(unstated)" : b.RubricVersion;

static double? ParseExpect(string[] args)
{
    var i = Array.IndexOf(args, "--expect");
    return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out var n) ? n : null;
}
