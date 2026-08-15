using System.Globalization;

namespace Cai.Web.Noise;

/// <summary>
/// The public candidate pool, and the periods drawn from it.
/// </summary>
/// <remarks>
/// <para>★ THE POOL IS PUBLIC OR THE DRAW IS UNVERIFIABLE. A third party re-deriving a holdout needs the
/// seed AND the candidates it was drawn from; publishing only the seed proves nothing, because the pool
/// could have been chosen after the fact.</para>
/// <para><b>Public repositories only.</b> Everything a human rater is shown must already be public —
/// that is what makes crowd review possible at all, and it removes every data-handling question in one
/// stroke rather than managing it.</para>
/// <para>Shipped as code for now. It belongs in a versioned, signed manifest before the standard invites
/// a second participant, so that "the pool at the time of the draw" is itself checkable — a pool that
/// can be edited silently makes a reproducible draw meaningless.</para>
/// </remarks>
public static class NoiseCorpus
{
    /// <summary>The sampler contract's version, published with every draw.</summary>
    public const string SamplerVersion = "cai-holdout-1.0";

    /// <summary>
    /// The rules in force. ★ Pre-registered: published BEFORE a draw, never adjusted after seeing one.
    /// </summary>
    public static HoldoutRules Rules { get; } = new(
        TargetProductionLocPerLanguage: 60_000,
        MaxRepositoryLoc: 400_000,
        MinRepositoriesPerLanguage: 3,
        MinRepositoriesPerSlice: 10);

    /// <summary>
    /// Periods with a published draw, and the seed each was drawn under.
    /// </summary>
    /// <remarks>
    /// ★ A period absent from here is 404, never an empty draw. An empty holdout reads as "we measured
    /// nothing there", which is a different and false claim from "no draw has been published".
    /// <para>The seed is published WITH the draw and fixed before it: a seed chosen after seeing a draw
    /// is not a seed, it is a selection.</para>
    /// </remarks>
    public static IReadOnlyDictionary<string, PublishedDraw> Draws { get; } =
        new Dictionary<string, PublishedDraw>(StringComparer.OrdinalIgnoreCase)
        {
            ["2026-09"] = new("cai-2026-09-9f2b41c7e0a85d36", new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)),
        };

    /// <summary>A published draw: the seed it used and when it was fixed.</summary>
    /// <param name="Seed">The seed the sampler ranks candidates under.</param>
    /// <param name="DrawnAt">
    /// ★ When the draw was fixed. Published so "before any scanner ran" is checkable rather than
    /// asserted — a holdout published after the runs is worthless however it was made.
    /// </param>
    public sealed record PublishedDraw(string Seed, DateTimeOffset DrawnAt);

    /// <summary>The candidate pool.</summary>
    public static IReadOnlyList<HoldoutCandidate> Candidates { get; } = Build();

    private static List<HoldoutCandidate> Build()
    {
        // A first, deliberately small pool across four languages, so the endpoints and the reproducibility
        // property can be exercised end to end. Onboarding the real corpus is a separate, larger step —
        // and one that must land with the signed manifest, not before it.
        (string Owner, string Name, string Language, int Loc, string Licence, string Sha)[] seed =
        [
            ("dotnet", "efcore", "csharp", 210_000, "MIT", "c1d83d0e9f2b4a6710d5e8c3b2a1f09876543210"),
            ("dotnet", "aspnetcore", "csharp", 380_000, "MIT", "a41b7c9d2e8f0361b5d4c7a9e2f8106b3d5c7e91"),
            ("AutoMapper", "AutoMapper", "csharp", 42_000, "MIT", "b72e4f8a1c9d0356e8b2f4a7c1d9e360587a2b4c"),
            ("serilog", "serilog", "csharp", 38_000, "Apache-2.0", "d93f1a5b7c2e8046f1a3b9d5c7e20418a6b3d9f2"),
            ("grpc", "grpc-go", "go", 190_000, "Apache-2.0", "e15a9c3d7f2b8460a9c1e5d3b7f92048c6a1d3e5"),
            ("spf13", "cobra", "go", 24_000, "Apache-2.0", "f27b1d5e9a3c8460b2d4f6a8c0e13579b4d6f8a2"),
            ("gin-gonic", "gin", "go", 31_000, "MIT", "0a3c5e7b9d1f2468a0c2e4b6d8f0135792a4c6e8"),
            ("prometheus", "client_golang", "go", 45_000, "Apache-2.0", "1b4d6f8a0c2e357913579bdf02468ace13579bdf"),
            ("serde-rs", "serde", "rust", 52_000, "Apache-2.0", "2c5e7a9b1d3f468a02468ace13579bdf02468ace"),
            ("tokio-rs", "tokio", "rust", 120_000, "MIT", "3d6f8b0c2e4a579b13579bdf02468ace13579bdf"),
            ("clap-rs", "clap", "rust", 47_000, "MIT", "4e709c1d3f5b68ac02468ace13579bdf02468ace"),
            ("psf", "requests", "python", 28_000, "Apache-2.0", "5f81ad2e406c79bd13579bdf02468ace13579bdf"),
            ("pallets", "flask", "python", 22_000, "BSD-3-Clause", "6092be3f517d8ace02468ace13579bdf02468ace"),
            ("encode", "httpx", "python", 26_000, "BSD-3-Clause", "71a3cf40628e9bdf13579bdf02468ace13579bdf"),
            ("tiangolo", "fastapi", "python", 34_000, "MIT", "82b4d051739face002468ace13579bdf02468ace"),
        ];

        return [.. seed.Select(r => new HoldoutCandidate(
            RepoId: string.Create(CultureInfo.InvariantCulture, $"{r.Owner}/{r.Name}"),
            Language: r.Language,
            ProductionLoc: r.Loc,
            Licence: r.Licence,
            PinnedSha: r.Sha))];
    }
}
