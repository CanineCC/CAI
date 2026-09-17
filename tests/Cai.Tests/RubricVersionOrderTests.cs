using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The standard's definition of which rubric version is newer — the rule every consumer shares instead of
/// each deriving it and one of them getting it wrong.
/// </summary>
public sealed class RubricVersionOrderTests
{
    [Theory]
    [InlineData("rubric-2026.09.13", "rubric-2026.09.9")]   // ★ the trap: '9' > '1' as text
    [InlineData("rubric-2026.09.10", "rubric-2026.09.9")]
    [InlineData("rubric-2026.06.17", "rubric-2026.06.2")]
    [InlineData("rubric-2026.10.1", "rubric-2026.09.13")]   // month wins over sequence
    [InlineData("rubric-2027.01.0", "rubric-2026.12.99")]   // year wins over month
    public void The_newer_version_compares_greater(string newer, string older)
    {
        Assert.True(RubricVersionOrder.Compare(newer, older) > 0, $"{newer} should be newer than {older}");
        Assert.True(RubricVersionOrder.Compare(older, newer) < 0, $"{older} should be older than {newer}");
        Assert.Equal(newer, RubricVersionOrder.Newest([older, newer]));
        Assert.Equal(newer, RubricVersionOrder.Newest([newer, older]));
    }

    [Fact]
    public void Identical_versions_compare_equal()
    {
        Assert.Equal(0, RubricVersionOrder.Compare("rubric-2026.09.13", "rubric-2026.09.13"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("r3")]                      // the fiction a picker once offered
    [InlineData("rubric-2026.09")]          // too few segments
    [InlineData("rubric-2026.09.1.2")]      // too many
    [InlineData("rubric-2026.09.+1")]       // int.TryParse would accept this; the standard must not
    [InlineData("rubric-2026.09. 1")]
    [InlineData("rubric-2026.09.-1")]
    [InlineData("rubric-latest")]
    public void An_unrecognised_name_is_not_well_formed(string? candidate)
    {
        Assert.False(RubricVersionOrder.IsWellFormed(candidate));
    }

    [Fact]
    public void An_unrecognised_name_can_never_win_newest()
    {
        // The property that matters operationally: a stray tag in a registry listing, or a pin someone typed,
        // must not be able to present itself as the newest rubric.
        Assert.Equal("rubric-2026.09.13", RubricVersionOrder.Newest(["rubric-2026.09.13", "zzz-not-a-rubric"]));
        Assert.True(RubricVersionOrder.Compare("zzz-not-a-rubric", "rubric-2026.06.0") < 0);
    }

    [Fact]
    public void The_order_stays_total_between_two_unrecognised_names()
    {
        // Otherwise a sort over a listing containing two stray tags is undefined behaviour rather than an order.
        Assert.True(RubricVersionOrder.Compare("aaa", "bbb") < 0);
        Assert.True(RubricVersionOrder.Compare("bbb", "aaa") > 0);
        Assert.Equal(0, RubricVersionOrder.Compare("aaa", "aaa"));
    }

    [Fact]
    public void NewestFirst_orders_a_real_archive_listing_correctly_and_drops_blanks()
    {
        var listing = new[]
        {
            "rubric-2026.09.9", "rubric-2026.09.13", "  ", "rubric-2026.08.29",
            "rubric-2026.09.10", null, "rubric-2026.09.2",
        };

        Assert.Equal(
            ["rubric-2026.09.13", "rubric-2026.09.10", "rubric-2026.09.9", "rubric-2026.09.2", "rubric-2026.08.29"],
            RubricVersionOrder.NewestFirst(listing));
    }

    [Fact]
    public void Zero_padding_would_not_have_helped_and_this_records_why()
    {
        // A mixed archive is what padding would actually produce, because published versions are immutable and
        // cannot be renamed. Ordered as TEXT every padded name sorts BELOW every unpadded one ('0' < '9'); the
        // standard's comparison gets both right, which is why the fix is numeric and not cosmetic.
        var mixed = new[] { "rubric-2026.09.9", "rubric-2026.09.0014" };

        Assert.Equal("rubric-2026.09.0014", RubricVersionOrder.Newest(mixed));
        Assert.Equal("rubric-2026.09.9", mixed.OrderByDescending(v => v, StringComparer.Ordinal).First());
    }
}
