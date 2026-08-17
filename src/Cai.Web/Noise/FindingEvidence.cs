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
    /// <summary>The id for one finding's coordinates.</summary>
    public static string For(string repoId, string pinnedSha, string? filePath, int? line, string ruleId)
    {
        var material = string.Create(
            CultureInfo.InvariantCulture,
            $"{repoId}\n{pinnedSha}\n{filePath ?? ""}\n{line?.ToString(CultureInfo.InvariantCulture) ?? ""}\n{ruleId}");

        // ★ 24 hex characters: long enough that a collision is not a practical concern over any corpus this
        // standard will hold, short enough to appear in a URL a rater is looking at.
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..24];
    }
}

/// <summary>One finding as a rater is shown it.</summary>
/// <remarks>
/// ★★ THERE IS DELIBERATELY NO TOOL ON THIS RECORD'S PUBLIC SHAPE. A rater told which vendor produced a finding is
/// being asked a different question — and on a standard its owner competes in, "this one is Watchdog's" is the most
/// corrupting thing the page could leak. The tool is stored (the register needs it) and never served here.
/// </remarks>
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
