using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// ★★ A RUBRIC VERSION'S LAST SEGMENT IS A SEQUENCE NUMBER, AND AN ORDINAL SORT IS NOT A CHRONOLOGY.
///
/// <para><c>rubric-2026.09.9</c> sorts ABOVE <c>rubric-2026.09.13</c> as a string, because '9' > '1'. So
/// <see cref="RubricCatalogStore.Versions"/>, which ordered by <c>StringComparer.Ordinal</c>, reported the archive
/// newest-first incorrectly — and <see cref="RubricCatalogStore.Latest"/>, which is simply the first of that list,
/// returned the WRONG VERSION.</para>
///
/// <para>This was live. It activated the day <c>rubric-2026.09.10</c> was published: from that moment
/// <c>/api/rubrics/latest/catalog</c> and <c>/api/rubrics/latest/digest</c> served <c>rubric-2026.09.9</c>, and any
/// consumer resolving "latest" — including a producer asking what to measure under — got a stale rubric while
/// everything reported success. The failure is silent by construction: a stale-but-valid version verifies fine.</para>
/// </summary>
public sealed class RubricVersionOrderingTests
{
    private static RubricCatalogStore Archive()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "rubrics")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return new RubricCatalogStore(Path.Combine(dir.FullName, "rubrics"));
    }

    private static (int Year, int Month, int Sequence) Parse(string version)
    {
        var parts = version["rubric-".Length..].Split('.');
        return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    [Fact]
    public void Latest_is_the_newest_by_SEQUENCE_not_by_string_order()
    {
        var store = Archive();
        var versions = store.Versions();
        Assert.NotEmpty(versions);

        var newest = versions.OrderBy(Parse).Last();

        Assert.Equal(newest, store.Latest());
    }

    [Fact]
    public void Versions_are_returned_newest_first_by_sequence()
    {
        var versions = Archive().Versions();

        Assert.Equal(versions.OrderByDescending(Parse).ToList(), versions);
    }

    [Fact]
    public void A_double_digit_sequence_sorts_above_a_single_digit_one()
    {
        // The specific trap, pinned directly so the guard cannot regress to a string compare and still pass on an
        // archive that happens to contain no double-digit sequence.
        var ordered = new[] { "rubric-2026.09.9", "rubric-2026.09.13", "rubric-2026.09.10" }
            .OrderByDescending(Parse)
            .ToList();

        Assert.Equal(["rubric-2026.09.13", "rubric-2026.09.10", "rubric-2026.09.9"], ordered);
    }
}
