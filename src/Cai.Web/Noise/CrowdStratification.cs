namespace Cai.Web.Noise;

/// <summary>A rater's relationship to the tool being measured.</summary>
/// <remarks>
/// ★★ The one declaration that matters most, and the one nobody would volunteer unprompted. A vendor
/// rating its own tool's findings is the precise conflict the standard exists to remove.
/// </remarks>
public enum RaterAffiliation
{
    /// <summary>No relationship to the vendor whose tool produced the findings.</summary>
    Independent,

    /// <summary>Employed by that vendor.</summary>
    VendorEmployed,

    /// <summary>Paid by that vendor without being employed — a contractor, a sponsored maintainer.</summary>
    VendorContracted,

    /// <summary>
    /// Earning the vendor's product by answering — a contributor cohort, a free tier granted for a daily
    /// question.
    /// </summary>
    /// <remarks>
    /// ★★ ITS OWN BUCKET, and the reason this enum has four members rather than three. Someone granted a
    /// paid tier in exchange for answering is compensated in kind rather than in cash, which changes the
    /// accounting and not the incentive — so counting them as independent would let a vendor manufacture
    /// its own independent bucket, the most valuable number on the page and the cheapest to fake. They are
    /// equally not the vendor's staff, and folding them in there would overstate the conflict as surely as
    /// ignoring it understates it.
    /// </remarks>
    CompensatedInProduct,
}

/// <summary>What a rater declared about themselves.</summary>
public sealed record RaterStratum(string RaterId, string PrimaryLanguage, RaterAffiliation Affiliation);

/// <summary>The composition of the answers a round actually collected.</summary>
/// <param name="Undeclared">
/// ★ Answers from raters who declared nothing. Their own bucket, never folded into Independent: counting
/// an undeclared affiliation as independence lets the most interesting bias in the pool hide in a default.
/// </param>
/// <param name="Compensated">
/// ★★ Answers from raters earning the vendor's product by giving them. Neither independent nor staff —
/// see <see cref="RaterAffiliation.CompensatedInProduct"/>.
/// </param>
public sealed record CrowdComposition(
    int Answers, int Independent, int VendorAffiliated, int Compensated, int Undeclared,
    string? LargestLanguage, double? LargestLanguageShare, bool Dominated,
    IReadOnlyDictionary<string, int> ByLanguage);

/// <summary>
/// Who answered, published so a reader can see the shared bias that agreement statistics cannot.
/// </summary>
/// <remarks>
/// <para>★★ κ measures whether raters agree, never whether what they agree on is true. Ten people who all
/// work in one language, or all work for the vendor, will agree at a rate that reads as reliability and is
/// nothing of the kind. Composition is the only defence, and it is cheap.</para>
/// <para>★ Nothing here excludes anyone — see <see cref="Retain"/>.</para>
/// </remarks>
public static class CrowdStratification
{
    /// <summary>Above this share of answers, one language's conventions are speaking for the crowd.</summary>
    public const double DominanceThreshold = 0.6;

    public static CrowdComposition Summarise(
        IReadOnlyCollection<CrowdAnswer> answers, IReadOnlyCollection<RaterStratum> strata)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(strata);

        var byRater = strata
            .GroupBy(s => s.RaterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // ★ Weighted by ANSWERS, not by head-count. One person answering forty questions shapes a round
        // far more than ten people answering one each, and a head-count would hide that entirely.
        var declared = answers
            .Select(a => byRater.TryGetValue(a.RaterId, out var s) ? s : null)
            .ToList();

        Dictionary<string, int> byLanguage = new(StringComparer.OrdinalIgnoreCase);
        foreach (var stratum in declared.Where(s => s is not null))
        {
            var language = stratum!.PrimaryLanguage;
            byLanguage[language] = byLanguage.GetValueOrDefault(language) + 1;
        }

        var largest = byLanguage.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).FirstOrDefault();
        var declaredCount = declared.Count(s => s is not null);
        double? share = declaredCount > 0 ? (double)largest.Value / declaredCount : null;

        return new CrowdComposition(
            Answers: answers.Count,
            Independent: declared.Count(s => s?.Affiliation == RaterAffiliation.Independent),
            VendorAffiliated: declared.Count(s =>
                s?.Affiliation is RaterAffiliation.VendorEmployed or RaterAffiliation.VendorContracted),
            Compensated: declared.Count(s => s?.Affiliation == RaterAffiliation.CompensatedInProduct),
            Undeclared: declared.Count(s => s is null),
            LargestLanguage: declaredCount > 0 ? largest.Key : null,
            LargestLanguageShare: share,
            Dominated: share > DominanceThreshold,
            ByLanguage: byLanguage);
    }

    /// <summary>
    /// The answers that count — which is ALL of them, whoever gave them.
    /// </summary>
    /// <remarks>
    /// ★★ Written down rather than implied. Dropping raters by who they are selects on a variable
    /// correlated with the outcome, and the surviving number is cleaner and means less. Publishing the
    /// composition costs nothing and leaves the reader to discount as they see fit.
    /// </remarks>
    public static IReadOnlyList<CrowdAnswer> Retain(
        IReadOnlyCollection<CrowdAnswer> answers, IReadOnlyCollection<RaterStratum> strata)
    {
        _ = strata;
        return [.. answers];
    }
}
