using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Cai.Web.Noise;

/// <summary>
/// The id a finding is known by — derived from the finding, never assigned.
/// </summary>
/// <remarks>
/// <para>★★ A SUBMITTER-CHOSEN ID CANNOT MATCH ACROSS TOOLS, and cross-vendor matching is the whole point of the
/// pooled union: two tools reporting one defect have to produce one id or they are being compared on nothing. An
/// id CAI invented per submission would be worse — it would change every time the same defect was reported.</para>
///
/// <para>★★ THE SHA IS PART OF THE KEY. "The same line" means nothing across two revisions — the file may have
/// changed underneath it, which is exactly what pinning a sha exists to settle.</para>
///
/// <para>★ A finding with no file and no line still gets an id. That is the coordinate gap, not a defect in the
/// finding, and without this the repository-level dimensions would drop silently out of everything keyed by id.</para>
/// </remarks>
public static class FindingKey
{
    /// <summary>
    /// The id for one finding, derived from what identifies it.
    /// </summary>
    /// <remarks>
    /// <para>★★ DERIVED, NOT ASSIGNED. A submitter-chosen id cannot be matched across tools — which is what the
    /// pooled union needs — and an id CAI invented per submission would change every time the same defect was
    /// reported. Two tools reporting one defect must produce one id.</para>
    ///
    /// <para>★★ AND THE TITLE COUNTS ONLY WHERE THERE IS NO COORDINATE (#21). Measured over 34 fingerprint sets:
    /// 1,905 of 7,144 rows carry no usable coordinate, and 1,689 of those collided — one session had 160 D3 rows
    /// keying to a single id. A repo-level claim IS its sentence, so its sentence identifies it. Where a
    /// coordinate exists the title is deliberately EXCLUDED: otherwise improving a remediation message would
    /// renumber most of the corpus and break every dispute and crowd queue keyed on the old ids.</para>
    /// </remarks>
    /// <param name="title">
    /// The finding's own statement. Read only when <paramref name="filePath"/> and <paramref name="line"/> are
    /// both absent — see the remarks.
    /// </param>
    public static string For(
        string repoId, string pinnedSha, string? filePath, int? line, string ruleId, string? title = null)
    {
        // ★ "No usable coordinate" is the union's own definition: no file at all, or a file with no line. A file
        // without a line cannot be matched against another vendor's finding in the same file either, so it is
        // the same case and gets the same treatment.
        var hasCoordinate = !string.IsNullOrWhiteSpace(filePath) && line is > 0;
        var discriminator = hasCoordinate ? "" : (title ?? "").Trim();

        var material = string.Create(CultureInfo.InvariantCulture,
            $"{repoId}\n{pinnedSha}\n{filePath ?? ""}\n{line?.ToString(CultureInfo.InvariantCulture) ?? ""}\n{ruleId}\n{discriminator}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..24];
    }
}

public sealed record FindingRecord(
    string FindingId, string Period, string Tool,
    string RepoId, string PinnedSha, string? FilePath, int? Line, string RuleId, string? Title,
    string ClaimClass);

/// <summary>Where a rater goes to look at the code.</summary>
public static class FindingEvidence
{
    /// <summary>
    /// A link to the cited line at the PINNED revision.
    /// </summary>
    /// <remarks>
    /// ★★ NOT AT THE BRANCH. A link to HEAD shows the rater code that may have changed since the run, and they
    /// would be judging the finding against a file that no longer matches it.
    /// <para>★ This is the whole reason the corpus is public repositories only: the rater needs access to nothing,
    /// and CAI needs to store no source.</para>
    /// </remarks>
    public static string? SourceUrl(string repoId, string pinnedSha, string? filePath, int? line)
    {
        if (string.IsNullOrWhiteSpace(repoId) || string.IsNullOrWhiteSpace(pinnedSha))
        {
            return null;
        }

        var at = $"https://github.com/{repoId}/tree/{pinnedSha}";
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return at;
        }

        var file = $"https://github.com/{repoId}/blob/{pinnedSha}/{filePath.TrimStart('/')}";
        return line is { } l ? $"{file}#L{l.ToString(CultureInfo.InvariantCulture)}" : file;
    }
}
