using System.Globalization;

namespace Cai.Scoring;

/// <summary>
/// ★★ WHICH OF TWO RUBRIC VERSIONS IS NEWER. The standard owns this, because it is a fact about what a
/// version STRING MEANS — and the string is the standard's, carried in every signed delivery.
///
/// <para>A version is <c>rubric-&lt;year&gt;.&lt;month&gt;.&lt;sequence&gt;</c> and the last segment is a SEQUENCE
/// NUMBER, not a day: the archive runs <c>rubric-2026.06.0 … rubric-2026.06.17</c> and
/// <c>rubric-2026.09.1 … rubric-2026.09.13</c>. So an ordinal string sort is NOT a chronology —
/// <c>rubric-2026.09.9</c> sorts above <c>rubric-2026.09.13</c> as text, because '9' &gt; '1'.</para>
///
/// <para><b>That was live.</b> <see cref="RubricCatalogStore.Latest"/> ordered by
/// <c>StringComparer.Ordinal</c>, so from the day <c>rubric-2026.09.10</c> was published the archive
/// served <c>rubric-2026.09.9</c> as its latest — observed in production on 2026-09-17, answering
/// <c>{"latest":"rubric-2026.09.9"}</c> while serving <c>.13</c>. Every consumer resolving "latest" got a
/// stale rubric and nothing reported it, because a stale-but-valid version verifies perfectly well.</para>
///
/// <para><b>Why it lives here rather than in a consumer.</b> The knowledge existed: kennel had a type
/// saying exactly this, with a comment that an ordinal sort is not a chronology. It was on one side of the
/// seam and not the other, and the side that lacked it is the side that SERVES the archive. A rule about
/// the meaning of the standard's own identifiers belongs in the standard, so there is one definition for
/// every consumer to share rather than one per consumer to get right independently.</para>
///
/// <para><b>Padding the sequence would not fix this.</b> Published versions are immutable (ADR-0004) and
/// their names are bound into signed deliveries, customer pins and analyzer image tags, so the existing
/// ones cannot be renamed — and in a MIXED archive every zero-padded name sorts below every unpadded one
/// ('0' &lt; '9'), which is worse than today. Compare numerically; do not encode the ordering into the
/// string.</para>
///
/// <para>Input may come from a registry listing, a pin a customer typed, or a container tag, so an
/// unrecognised version is ORDERED rather than rejected: it sorts below every parseable one — an unknown
/// name must never win "newest" — with ordinal comparison between two unparseable names so the order
/// stays total.</para>
/// </summary>
public static class RubricVersionOrder
{
    /// <summary>The prefix every rubric version carries.</summary>
    private const string Prefix = "rubric-";

    /// <summary>Ascending (oldest first) comparer, for LINQ and sorted collections.</summary>
    public static IComparer<string> Comparer { get; } = System.Collections.Generic.Comparer<string>.Create(Compare);

    /// <summary>
    /// Compare two rubric versions chronologically: negative when <paramref name="left"/> is older, positive
    /// when it is newer, zero when they are the same version.
    /// </summary>
    /// <param name="left">The left-hand version.</param>
    /// <param name="right">The right-hand version.</param>
    public static int Compare(string? left, string? right)
    {
        var l = Parse(left);
        var r = Parse(right);

        if (l is null && r is null)
        {
            // Neither is a version we recognise; ordinal keeps the order total and stable.
            return string.CompareOrdinal(left ?? string.Empty, right ?? string.Empty);
        }

        // An unrecognised version always loses — it must never be able to win "newest".
        if (l is null)
        {
            return -1;
        }

        if (r is null)
        {
            return 1;
        }

        var (ly, lm, ls) = l.Value;
        var (ry, rm, rs) = r.Value;
        return ly != ry ? ly.CompareTo(ry)
            : lm != rm ? lm.CompareTo(rm)
            : ls.CompareTo(rs);
    }

    /// <summary>The newest version, or null when the sequence holds none. Blank entries are skipped.</summary>
    /// <param name="versions">The candidate versions, in any order.</param>
    public static string? Newest(IEnumerable<string?> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        string? newest = null;
        foreach (var candidate in versions)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && (newest is null || Compare(candidate, newest) > 0))
            {
                newest = candidate;
            }
        }

        return newest;
    }

    /// <summary>The versions ordered newest first — what an archive listing serves and a picker offers.
    /// Blank entries are dropped.</summary>
    /// <param name="versions">The candidate versions, in any order.</param>
    public static IReadOnlyList<string> NewestFirst(IEnumerable<string?> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        return [.. versions
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .OrderByDescending(v => v, Comparer)];
    }

    /// <summary>Whether this is a well-formed rubric version.</summary>
    /// <param name="version">The candidate.</param>
    public static bool IsWellFormed(string? version) => Parse(version) is not null;

    /// <summary>The (year, month, sequence) triple, or null when the name is not a rubric version. Every
    /// segment must be a plain non-negative integer that fits an <see cref="int"/> — an out-of-range segment
    /// is unparseable rather than silently wrapping to a negative.</summary>
    private static (int Year, int Month, int Sequence)? Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || !version.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var body = version.AsSpan(Prefix.Length);
        Span<Range> segments = stackalloc Range[4];
        if (body.Split(segments, '.') != 3)
        {
            return null;
        }

        if (!Segment(body[segments[0]], out var year)
            || !Segment(body[segments[1]], out var month)
            || !Segment(body[segments[2]], out var sequence))
        {
            return null;
        }

        return (year, month, sequence);

        static bool Segment(ReadOnlySpan<char> text, out int value)
        {
            value = 0;
            foreach (var c in text)
            {
                // Reject '+'/'-'/whitespace/digit-separators that int.TryParse would otherwise accept.
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }
            }

            return text.Length > 0
                && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }
}
