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
/// <para>★★ NO LONGER SHIPPED AS CODE. Everything here is READ FROM THE SIGNED MANIFEST — see
/// <see cref="CorpusManifest"/>. A pool shipped as a C# array can be edited in a commit that also edits the
/// tests, and the reproducible draw then proves only that the sampler is deterministic, not that the pool it ran
/// over is the pool that was published. This class is now the typed view of the manifest, and there is exactly
/// one pool.</para>
/// </remarks>
public static class NoiseCorpus
{
    /// <summary>The sampler contract's version, published with every draw.</summary>
    /// <remarks>★ From the manifest, so it cannot disagree with the version the signature covers.</remarks>
    public static string SamplerVersion => CorpusManifest.Load().SamplerVersion;

    /// <summary>
    /// The rules in force. ★ Pre-registered: published BEFORE a draw, never adjusted after seeing one.
    /// </summary>
    public static HoldoutRules Rules => CorpusManifest.Load().Rules;

    /// <summary>
    /// Periods with a published draw, and the seed each was drawn under.
    /// </summary>
    /// <remarks>
    /// ★ A period absent from here is 404, never an empty draw. An empty holdout reads as "we measured
    /// nothing there", which is a different and false claim from "no draw has been published".
    /// <para>The seed is published WITH the draw and fixed before it: a seed chosen after seeing a draw
    /// is not a seed, it is a selection.</para>
    /// </remarks>
    public static IReadOnlyDictionary<string, PublishedDraw> Draws => CorpusManifest.Load().Draws;

    /// <summary>A published draw: the seed it used and when it was fixed.</summary>
    /// <param name="Seed">The seed the sampler ranks candidates under.</param>
    /// <param name="DrawnAt">
    /// ★ When the draw was fixed. Published so "before any scanner ran" is checkable rather than
    /// asserted — a holdout published after the runs is worthless however it was made.
    /// </param>
    public sealed record PublishedDraw(string Seed, DateTimeOffset DrawnAt);

    /// <summary>The candidate pool, from the signed manifest.</summary>
    public static IReadOnlyList<HoldoutCandidate> Candidates => CorpusManifest.Load().Candidates;
}
