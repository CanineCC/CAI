namespace Cai.Web.Noise;

/// <summary>
/// The embargo: no participant sees another's result before the period publishes.
/// </summary>
/// <remarks>
/// <para>★★ 03 COMMITS TO IT AS ONE OF THE FOUR THINGS THAT MAKE OUR CONFLICT OF INTEREST SURVIVABLE, and the
/// record served everything to everyone immediately. Watchdog owns the standard and competes in it: early sight of
/// a rival's result is the single most valuable thing that position could be worth, and "we would not look" is
/// exactly the assurance nobody should have to accept.</para>
///
/// <para>★★ THE LIFT DATE LIVES IN THE SIGNED MANIFEST, beside the draw. An embargo whose date can be edited is a
/// promise to lift it when convenient.</para>
///
/// <para>★★ AND IT FAILS CLOSED. A period with no publication date is embargoed, not open: reading "no date" as
/// "publish immediately" would make every incomplete manifest entry a silent leak.</para>
/// </remarks>
public static class Embargo
{
    /// <summary>Whether the embargo still stands at <paramref name="now"/>.</summary>
    /// <remarks>
    /// ★ At the date it has LIFTED — the boundary goes the permissive way, because an embargo outlasting its
    /// published date by a tick is a different date from the published one, and the discrepancy would only ever be
    /// noticed by somebody it inconvenienced.
    /// </remarks>
    public static bool IsInForce(DateTimeOffset? publishesAt, DateTimeOffset now) =>
        publishesAt is not { } lifts || now < lifts;

    /// <summary>
    /// Whether <paramref name="caller"/> may read material belonging to <paramref name="owner"/>.
    /// </summary>
    /// <param name="caller">
    /// The authenticated principal's name, or null when the caller is anonymous. ★★ Anonymous sees nothing under
    /// embargo: "only their own" needs an identity to be anybody's own, and treating anonymous as a participant
    /// would let anyone read everything by presenting nothing.
    /// </param>
    /// <param name="owner">The tool the material belongs to.</param>
    /// <remarks>
    /// ★★ NO EXEMPTIONS. There is deliberately no caller name with a different answer — Watchdog owns the standard
    /// and is bound by this exactly as any other participant, or the embargo is a courtesy rather than a rule.
    /// </remarks>
    public static bool MayRead(string? caller, string owner, DateTimeOffset? publishesAt, DateTimeOffset now) =>
        !IsInForce(publishesAt, now)
        || (!string.IsNullOrWhiteSpace(caller)
            && string.Equals(caller, owner, StringComparison.OrdinalIgnoreCase));

    /// <summary>What the record says while it is withholding.</summary>
    public static string Note(DateTimeOffset? publishesAt) =>
        publishesAt is { } lifts
            ? $"This period is under embargo until {lifts:O}. Until then a participant sees only its own "
            + "submissions, and everything else — the judging, the disputes and every other participant's "
            + "material — is withheld from everyone including Watchdog. 03 commits to this as one of the four "
            + "things that make the standard's conflict of interest survivable."
            : "This period has no publication date, so it is embargoed: a missing date means nobody has said when "
            + "it publishes, and reading that as 'publish immediately' would make an incomplete record a leak.";
}
