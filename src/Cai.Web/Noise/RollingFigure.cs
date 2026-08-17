using System.Globalization;

namespace Cai.Web.Noise;

/// <summary>One period's judged findings, as the rolling figure needs them.</summary>
/// <remarks>
/// ★ Periods are <c>yyyy-MM</c> so they compare and sort lexically — the same reason the method versions use that
/// format. A period appearing more than once is a CORRECTION (publications are append-only), and the later row
/// supersedes rather than averaging with the earlier one.
/// </remarks>
public sealed record PeriodTally(string Period, int Judged, int Noise);

/// <summary>The pooled rate over a rolling window, and how much of the window it actually covers.</summary>
/// <param name="Periods">How many distinct periods went in.</param>
/// <param name="SpansTheFullWindow">
/// ★★ Whether it is really a twelve-month figure. A three-period pool is a real and useful number and is NOT the
/// annual one; presenting one as the other is the natural failure here, because it looks identical and is
/// available from month one.
/// </param>
/// <param name="Rate">Pooled noise over pooled judged, or null over an empty window.</param>
/// <param name="IntervalLow">Wilson 95 % low. ★ Over twelve periods this is tight, which is the point.</param>
/// <param name="IntervalHigh">Wilson 95 % high.</param>
public sealed record RollingSummary(
    int Periods,
    bool SpansTheFullWindow,
    string? FirstPeriod,
    string? LastPeriod,
    int Judged,
    int Noise,
    double? Rate,
    double? IntervalLow,
    double? IntervalHigh,
    string? Note);

/// <summary>
/// The rolling twelve-month figure 02 §5 requires beside every rate.
/// </summary>
/// <remarks>
/// <para>★★ A SINGLE PERIOD'S INTERVAL IS WIDE ENOUGH TO HIDE MOST MOVEMENTS. On 1,800 judged findings a
/// two-point change sits inside the Wilson interval, and the minimum detectable difference computed over
/// repositories is wider still — so month-to-month comparison is mostly noise about noise. Pooling the window is
/// what makes a trend legible.</para>
///
/// <para>★★ ROLLING, not cumulative. Pooling everything ever published would make the figure insensitive to
/// exactly the change a reader is looking for, and a tool that improved a year ago would carry its old rate for
/// ever.</para>
/// </remarks>
public static class RollingFigure
{
    /// <summary>The window, in months.</summary>
    public const int WindowMonths = 12;

    /// <summary>
    /// The pooled figure for the window ending at <paramref name="throughPeriod"/>.
    /// </summary>
    /// <param name="tallies">
    /// Every period available. ★ May contain the same period twice — a correction — and the LATEST occurrence
    /// wins: pooling both would double that period's weight and let a correction quietly re-weight the year.
    /// </param>
    /// <param name="throughPeriod">The window's last period, inclusive.</param>
    public static RollingSummary Compute(IReadOnlyList<PeriodTally> tallies, string throughPeriod)
    {
        ArgumentNullException.ThrowIfNull(tallies);

        // ★★ TWELVE CALENDAR MONTHS, not "the last twelve periods published". A publisher that skips months —
        // or publishes quarterly — would otherwise pool three YEARS under a label that says twelve months, which
        // is exactly the mislabelling this figure is supposed to make impossible. The window is
        // [throughPeriod − 11 months, throughPeriod].
        if (Month(throughPeriod) is not { } last)
        {
            return new RollingSummary(
                0, false, null, null, 0, 0, null, null, null,
                $"'{throughPeriod}' is not a yyyy-MM period, so a twelve-month window cannot be placed around "
              + "it. Periods are yyyy-MM precisely so they can be compared and windowed.");
        }

        var first = last.AddMonths(-(WindowMonths - 1));

        // ★★ NOTHING AFTER THE PERIOD ASKED FOR either. Otherwise a correction published later reaches backwards
        // into an older rolling figure, and a reader re-deriving last quarter's number gets a different answer
        // than the one that was published.
        var window = tallies
            .Where(t => Month(t.Period) is { } m && m >= first && m <= last)
            .GroupBy(t => t.Period, StringComparer.Ordinal)

            // ★ A period appearing twice is a CORRECTION — publications are append-only — and the later row
            // supersedes rather than averaging with the earlier one.
            .Select(g => g.Last())
            .OrderBy(t => t.Period, StringComparer.Ordinal)
            .ToList();

        if (window.Count == 0)
        {
            return new RollingSummary(
                0, false, null, null, 0, 0, null, null, null,
                "no periods have been published, so there is no rolling figure. This is an absence, not a rate "
              + "of zero.");
        }

        var judged = window.Sum(t => t.Judged);
        var noise = window.Sum(t => t.Noise);
        var rate = judged > 0 ? (double?)noise / judged : null;
        var interval = PublicationSurface.WilsonIntervalOrNull(noise, judged);
        var full = window.Count >= WindowMonths;

        return new RollingSummary(
            Periods: window.Count,
            SpansTheFullWindow: full,
            FirstPeriod: window[0].Period,
            LastPeriod: window[^1].Period,
            Judged: judged,
            Noise: noise,
            Rate: rate,
            IntervalLow: interval?.Low,
            IntervalHigh: interval?.High,
            Note: full ? null : ShortWindowNote(window.Count));
    }

    /// <summary>A period as a date, or null when it is not <c>yyyy-MM</c>.</summary>
    private static DateOnly? Month(string? period) =>
        DateOnly.TryParseExact(
            (period ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

    /// <summary>Says the window is short, in words a reader can act on.</summary>
    /// <remarks>★ InvariantCulture: this repository's hosts run da-DK, and a number formatted for the ambient
    /// culture has already shipped once this session as "16,7 %".</remarks>
    private static string ShortWindowNote(int periods)
    {
        var n = periods.ToString(CultureInfo.InvariantCulture);
        var window = WindowMonths.ToString(CultureInfo.InvariantCulture);

        return $"this pools {n} of {window} periods, so it is not yet a twelve-month figure. It is a real "
             + $"pooled rate over {n} period(s) and must not be quoted as the annual one — the two look "
             + "identical and only this line distinguishes them.";
    }
}
