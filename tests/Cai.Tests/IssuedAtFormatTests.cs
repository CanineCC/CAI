using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The <c>issuedAt</c> stamp on a delivery package must be RFC 3339. It is inside the signed payload, so a malformed
/// one cannot be corrected after issue without invalidating the signature — the artifact is simply wrong forever.
/// <para>This is a regression test for a real defect: the CLI formatted the stamp with a custom format string and no
/// culture, and <c>':'</c> in a custom format string is the CURRENT CULTURE's time separator. On a machine with a
/// Danish locale (the build box) that produced <c>2026-07-19T09.28.09Z</c> — dots instead of colons — which parses
/// under no RFC 3339 reader. It was only noticed because a live end-to-end verification printed the timestamp back.</para>
/// </summary>
public sealed class IssuedAtFormatTests
{
    private const string Format = "yyyy-MM-ddTHH:mm:ssZ";

    // A locale whose time separator is NOT ':' — the exact condition that produced the defect.
    private static readonly CultureInfo Hostile = CultureInfo.GetCultureInfo("da-DK");

    [Fact]
    public void Invariant_formatting_produces_rfc3339_even_under_a_hostile_locale()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = Hostile;
            var stamp = new DateTimeOffset(2026, 7, 19, 9, 28, 9, TimeSpan.Zero)
                .ToString(Format, CultureInfo.InvariantCulture);

            Assert.Equal("2026-07-19T09:28:09Z", stamp);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", stamp);
            Assert.True(DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_defect_reproduces_without_invariant_culture()
    {
        // Pins WHY the fix is needed. If a future .NET makes ':' literal in custom format strings this test starts
        // failing, which is the signal to revisit the comment rather than silently keep a now-pointless argument.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = Hostile;
            var stamp = new DateTimeOffset(2026, 7, 19, 9, 28, 9, TimeSpan.Zero).ToString(Format);

            Assert.DoesNotContain(":", stamp, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_shipped_sample_package_carries_a_wellformed_stamp()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Cai.slnx")))
        {
            root = root.Parent;
        }

        var samplePath = Path.Combine(root!.FullName, "examples", "cai-delivery.sample.json");
        var sample = Cai.Delivery.DeliveryPackage.Parse(File.ReadAllText(samplePath));

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$", sample.Payload.IssuedAt);
    }
}
