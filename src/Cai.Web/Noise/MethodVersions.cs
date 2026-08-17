namespace Cai.Web.Noise;

/// <summary>One version of the method, with when it was announced and which period it first governed.</summary>
/// <param name="Version">The version string a result cites.</param>
/// <param name="AnnouncedAt">
/// When the change was published. ★★ The load-bearing field: it must predate the draw of
/// <paramref name="EffectiveFromPeriod"/>, or the change is being applied to a holdout that already existed.
/// </param>
/// <param name="EffectiveFromPeriod">
/// The first period this version governs, <c>yyyy-MM</c> — the next holdout drawn after the announcement,
/// never one already open.
/// </param>
/// <param name="Rationale">
/// Why the method changed. ★ Required, and dated by <paramref name="AnnouncedAt"/>: a history of version
/// numbers with no reasons is a change log that explains nothing, and the reason is the part a reader needs in
/// order to judge whether a change was self-serving.
/// </param>
public sealed record MethodVersionRecord(
    string Version, DateTimeOffset AnnouncedAt, string EffectiveFromPeriod, string Rationale);

/// <summary>
/// The change-control rule: who can change the method, and when.
/// </summary>
/// <remarks>
/// <para>★★ WITH NO GOVERNANCE BODY IN PHASE 1, THE HONEST ANSWER WAS "WATCHDOG, UNILATERALLY, AT ANY TIME".
/// §01 already required versioning — "a standard that changes silently is worse than none" — but a version
/// number records that a change happened and constrains nothing about <em>when</em>. The failure case is
/// specific, and it is the one every reader will watch for: publish a number, dislike it, change the method for
/// the period it came from. Versioning documents that perfectly and prevents nothing.</para>
///
/// <para>★★ THE RULE (#23-2): <b>a method version takes effect from the next holdout drawn, never the current
/// one.</b> A change published after a holdout is drawn cannot apply to that holdout. It is §01's own
/// neutrality principle — "draw before results exist, prove the ordering" — applied to the method instead of
/// the corpus, and it removes the discretion without requiring a single meeting.</para>
///
/// <para>★ Shipped as code, like the corpus, and guarded by a test that fails the moment a version appears with
/// a date that suits us. A rule we publish and then break in our own history is worse than no rule.</para>
/// </remarks>
public static class MethodVersions
{
    /// <summary>The published history, oldest first.</summary>
    /// <remarks>
    /// ★ One entry today. The first version is effective from the first period whose holdout was drawn, and it
    /// was announced before that draw — which is the property <see cref="Validate"/> checks and
    /// <c>MethodVersionApiTests</c> asserts about this list specifically.
    /// </remarks>
    public static readonly IReadOnlyList<MethodVersionRecord> History =
    [
        new(
            Version: "noise-1.0-draft",
            AnnouncedAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveFromPeriod: "2026-09",
            Rationale:
                "First published method: the six-verdict set, the cascade, the crowd layer, the holdout rules, "
              + "and what a published result must carry. Announced before the 2026-09 holdout was drawn, so it "
              + "governs that period from its start rather than being applied to it afterwards."),
    ];

    /// <summary>The rule, in the words it publishes in.</summary>
    public const string Rule =
        "A method version takes effect from the NEXT holdout drawn, never one already open. A change "
      + "announced after a holdout has been drawn cannot apply to that holdout — the period is judged by the "
      + "version in force when its draw was published. Every version carries a dated rationale.";

    /// <summary>
    /// The version governing <paramref name="period"/>, or null when no version was yet in force.
    /// </summary>
    /// <remarks>
    /// ★★ NULL RATHER THAN THE EARLIEST. Claiming a version governed a period that predates it is the same
    /// retroactive application the rule forbids, pointing the other way. Periods are <c>yyyy-MM</c> so they
    /// compare lexically — which is the reason for that format.
    /// </remarks>
    public static MethodVersionRecord? InForceFor(
        string? period, IReadOnlyList<MethodVersionRecord>? history = null)
    {
        if (string.IsNullOrWhiteSpace(period))
        {
            return null;
        }

        return (history ?? History)
            .Where(v => string.Compare(v.EffectiveFromPeriod, period, StringComparison.Ordinal) <= 0)
            .OrderByDescending(v => v.EffectiveFromPeriod, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Everything wrong with a declared history — empty when it obeys the rule it publishes.
    /// </summary>
    /// <param name="history">The versions to check.</param>
    /// <param name="draws">The published draws, so an announcement can be compared against a draw date.</param>
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<MethodVersionRecord> history,
        IReadOnlyDictionary<string, NoiseCorpus.PublishedDraw> draws)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(draws);

        var problems = new List<string>();

        foreach (var version in history)
        {
            if (string.IsNullOrWhiteSpace(version.Rationale))
            {
                problems.Add($"version '{version.Version}' has no rationale, so nobody can judge the change.");
            }

            // ★★ THE CHECK THE RULE REDUCES TO. If the effective period's holdout was already drawn when the
            // change was announced, the change is reaching backwards into a measurement that had begun — which
            // is precisely "publish a number, dislike it, change the method for that period".
            if (draws.TryGetValue(version.EffectiveFromPeriod, out var draw)
                && version.AnnouncedAt >= draw.DrawnAt)
            {
                problems.Add(
                    $"version '{version.Version}' is effective from {version.EffectiveFromPeriod}, whose "
                  + $"holdout was already drawn at {draw.DrawnAt:O} when the version was announced at "
                  + $"{version.AnnouncedAt:O}. A change announced after a draw cannot apply to that draw.");
            }
        }

        var duplicates = history
            .GroupBy(v => v.Version, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var duplicate in duplicates)
        {
            problems.Add($"version '{duplicate}' appears more than once, so a result citing it is ambiguous.");
        }

        return problems;
    }
}
