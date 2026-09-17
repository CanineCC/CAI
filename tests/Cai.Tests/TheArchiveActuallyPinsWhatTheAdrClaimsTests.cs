using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// ★★ ADR-0004's RULE, EXECUTABLE — so the document cannot claim something the archive does not do.
///
/// <para>ADR-0004 states, as a settled consequence, that "a catalog must pin every input that can move a score."
/// Between 2026-08-22 and 2026-09-16 that was false of every artifact: 0 of 38 published catalogs carried a
/// <c>scoring</c> block, the publisher emitted a hand-rolled shape with no such field, and the producer pinned a
/// scorer build that could not have read one. Nothing detected the gap, because the claim lived only in prose.</para>
///
/// <para>This test is that sentence, run. It is deliberately a RATCHET WITH AN EXPIRING WAIVER — the pattern
/// <c>PublishedRubricParityTests</c> uses in the kennel repo, whose own comment puts it best: "an allowlist that
/// cannot expire is how a guard becomes decoration." The waiver here is a single constant naming the first version
/// required to pin its own constants. While it is null the archive is asserted to be COHERENT — nothing pins — so
/// the day a block is published without anyone updating the constant, this fails and says so.</para>
/// </summary>
public sealed class TheArchiveActuallyPinsWhatTheAdrClaimsTests
{
    /// <summary>
    /// The first rubric version required to carry a <c>scoring</c> block, or null while none does.
    ///
    /// <para>Set on 2026-09-17, when <c>rubric-2026.09.13</c> became the first version to pin the fold's own
    /// constants and the band cutlines. Its values EQUAL <c>ScoringParameters.Default</c>, so no published number and
    /// no published word moved — what changed is that the criteria are now readable off the archive instead of
    /// inferred from a scorer build. From here the rule is enforced FORWARD: every version from this one onward must
    /// pin, and a later one that stops is a rubric whose numbers stop being reproducible from the archive.</para>
    ///
    /// <para>Do not "fix" a failure here by editing this constant to match what the archive happens to contain. The
    /// constant records a DECISION about when the standard started pinning; the archive is evidence about whether the
    /// decision was carried out.</para>
    /// </summary>
    private const string? FirstVersionRequiredToPin = "rubric-2026.09.13";

    /// <summary>Year, month, then the sequence AS A NUMBER — see the note in <c>RubricCatalogStore</c>.</summary>
    private static (int Year, int Month, int Sequence) Sequence(string version)
    {
        var parts = version["rubric-".Length..].Split('.');
        return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    private static RubricCatalogStore Archive()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "rubrics")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir); // an absent archive is a fault, never a skip
        return new RubricCatalogStore(Path.Combine(dir.FullName, "rubrics"));
    }

    private static (IReadOnlyList<string> Pinning, IReadOnlyList<string> NotPinning) Split()
    {
        var store = Archive();
        var versions = store.Versions();
        Assert.NotEmpty(versions); // so this cannot pass by finding nothing

        var pinning = new List<string>();
        var notPinning = new List<string>();
        foreach (var version in versions)
        {
            var raw = store.RawCatalogJson(version);
            if (raw is null)
            {
                continue;
            }

            (RubricCatalog.Parse(raw).Scoring is null ? notPinning : pinning).Add(version);
        }

        return (pinning, notPinning);
    }

    [Fact]
    public void While_no_version_pins_the_folds_constants_the_archive_says_so_consistently()
    {
        if (FirstVersionRequiredToPin is not null)
        {
            return; // the forward rule below governs instead
        }

        var (pinning, _) = Split();

        Assert.True(
            pinning.Count == 0,
            $"{pinning.Count} published rubric version(s) carry a `scoring` block ({string.Join(", ", pinning)}) while "
            + $"{nameof(FirstVersionRequiredToPin)} is still null. ADR-0004 says a catalog pins every input that can "
            + "move a score; the archive has started doing that and the standard has not recorded when. Set the "
            + "constant to the FIRST version that pins, so the rule is enforced forward from there — and check the "
            + "kennel product resolves cutlines per repository before publishing a block whose values differ from "
            + "ScoringParameters.Default.");
    }

    [Fact]
    public void Once_a_version_pins_every_later_version_must_pin_too()
    {
        if (FirstVersionRequiredToPin is null)
        {
            return; // nothing to enforce forward yet; the coherence check above holds the line
        }

        var (_, notPinning) = Split();

        // By SEQUENCE, not by string. This guard shipped with `string.CompareOrdinal` and its first encounter with a
        // real release reported eight versions as "published from rubric-2026.09.13 onward" — .09.2 through .09.9,
        // every one of them OLDER — because '9' > '1'. The same trap it exists to catch, in the catching.
        var floor = Sequence(FirstVersionRequiredToPin);
        var regressed = notPinning.Where(v => Sequence(v).CompareTo(floor) >= 0).ToList();

        Assert.True(
            regressed.Count == 0,
            $"Published from {FirstVersionRequiredToPin} onward, so required to pin the fold's constants, but carrying "
            + $"no `scoring` block: {string.Join(", ", regressed)}. A rubric that stops pinning is a rubric whose "
            + "numbers stop being reproducible from the archive.");
    }

    [Fact]
    public void The_waiver_expires_when_the_archive_catches_up()
    {
        // The half that stops this becoming decoration. If the constant still says "nothing pins" while the archive
        // has moved on, the entry is stale and must be deleted — the same discipline PublishedRubricParityTests
        // applies to its known-absent dimension ids.
        var (pinning, _) = Split();

        if (FirstVersionRequiredToPin is null)
        {
            Assert.True(
                pinning.Count == 0,
                "The recorded gap says no version pins the fold's constants, but the archive contains versions that "
                + $"do: {string.Join(", ", pinning)}. Delete the gap by setting {nameof(FirstVersionRequiredToPin)}.");
            return;
        }

        Assert.Contains(FirstVersionRequiredToPin, pinning);
    }
}
