using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The published rubric archive is the standard's memory: a consumer that pinned "rubric-2026.08.15" must be able to
/// fetch exactly that definition years later and recompute the same number. These tests cover the two ways that
/// promise breaks — a version the engine emits but the archive never published (drift), and a published document that
/// declares a different version than the directory serving it (mislabelling).
/// </summary>
public sealed class RubricArchiveTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cai.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new DirectoryNotFoundException("could not locate repo root (Cai.slnx)");
        }
    }

    private static string RubricsRoot => Path.Combine(RepoRoot, "rubrics");

    private static RubricCatalogStore Store() => new(RubricsRoot);

    // ---- the real, shipped archive -------------------------------------------------------------------------------

    [Fact]
    public void Every_published_catalog_declares_the_version_it_is_published_under()
    {
        var unattested = Store().UnattestedVersions();

        Assert.True(
            unattested.Count == 0,
            "These published rubric directories contain a catalog declaring a DIFFERENT version, so the archive " +
            "cannot attest what they are and the API withholds them. Regenerate each from the engine commit that " +
            "set RubricVersion.Current to the directory's version — never by relabelling the file, which would " +
            "assert provenance we do not have:\n  " +
            string.Join("\n  ", unattested.Select(u => $"{u.Directory} declares {u.Declares}")));
    }

    [Fact]
    public void The_archive_publishes_the_versions_the_engine_can_emit()
    {
        // Guards the drift this suite was written for: the engine shipped rubric-2026.08.16 and .17 while the public
        // archive stopped at .15, so a freshly signed survey could not be verified against any published rubric.
        var versions = Store().Versions();

        Assert.Contains("rubric-2026.08.16", versions);
        Assert.Contains("rubric-2026.08.17", versions);
    }

    [Fact]
    public void Every_attested_catalog_parses_and_is_non_empty()
    {
        var store = Store();
        var versions = store.Versions();

        Assert.NotEmpty(versions);
        foreach (var v in versions)
        {
            var catalog = store.Get(v);
            Assert.NotNull(catalog);
            Assert.Equal(v, catalog!.RubricVersion);
            Assert.NotEmpty(catalog.Dimensions);
            Assert.NotEmpty(catalog.Lenses);
            Assert.All(catalog.Dimensions, d => Assert.False(string.IsNullOrWhiteSpace(d.Id)));
            Assert.All(catalog.Lenses, l => Assert.False(string.IsNullOrWhiteSpace(l.Key)));
        }
    }

    [Fact]
    public void Latest_is_the_newest_attested_version()
    {
        var store = Store();

        Assert.Equal(store.Versions().First(), store.Latest());
    }

    // ---- the attestation invariant, on a controlled archive ------------------------------------------------------

    [Fact]
    public void A_mislabelled_catalog_is_withheld_and_reported()
    {
        using var tmp = new TempArchive();
        tmp.Write("rubric-2026.01.01", declaring: "rubric-2026.01.01");
        tmp.Write("rubric-2026.02.02", declaring: "rubric-2026.03.03");  // mislabelled

        var store = new RubricCatalogStore(tmp.Root);

        Assert.Equal(["rubric-2026.01.01"], store.Versions());
        Assert.Null(store.Get("rubric-2026.02.02"));
        var (dir, declares) = Assert.Single(store.UnattestedVersions());
        Assert.Equal("rubric-2026.02.02", dir);
        Assert.Equal("rubric-2026.03.03", declares);
    }

    [Fact]
    public void A_mislabelled_catalog_is_not_reachable_under_the_version_it_declares_either()
    {
        // The document claims to be .03.03 — but nothing is published at that path, and serving the .02.02 directory's
        // file under .03.03 would fabricate a publication that never happened.
        using var tmp = new TempArchive();
        tmp.Write("rubric-2026.02.02", declaring: "rubric-2026.03.03");

        var store = new RubricCatalogStore(tmp.Root);

        Assert.Null(store.Get("rubric-2026.03.03"));
        Assert.Empty(store.Versions());
        Assert.Null(store.Latest());
    }

    [Fact]
    public void An_unparseable_catalog_is_withheld_rather_than_thrown()
    {
        using var tmp = new TempArchive();
        tmp.Write("rubric-2026.01.01", declaring: "rubric-2026.01.01");
        Directory.CreateDirectory(Path.Combine(tmp.Root, "rubric-2026.09.09"));
        File.WriteAllText(Path.Combine(tmp.Root, "rubric-2026.09.09", "rubric-catalog.json"), "{ not json");

        var store = new RubricCatalogStore(tmp.Root);

        Assert.Equal(["rubric-2026.01.01"], store.Versions());
        Assert.Null(store.Get("rubric-2026.09.09"));
        Assert.Contains(store.UnattestedVersions(), u => u.Directory == "rubric-2026.09.09");
    }

    [Fact]
    public void An_empty_or_missing_archive_is_empty_not_an_error()
    {
        using var tmp = new TempArchive();

        var store = new RubricCatalogStore(tmp.Root);
        Assert.Empty(store.Versions());
        Assert.Null(store.Latest());

        var absent = new RubricCatalogStore(Path.Combine(tmp.Root, "does-not-exist"));
        Assert.Empty(absent.Versions());
        Assert.Null(absent.Latest());
        Assert.Empty(absent.UnattestedVersions());
    }

    private sealed class TempArchive : IDisposable
    {
        public TempArchive()
        {
            Root = Path.Combine(Path.GetTempPath(), "cai-rubric-archive-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Write(string directory, string declaring)
        {
            var dir = Path.Combine(Root, directory);
            Directory.CreateDirectory(dir);
            var catalog = new RubricCatalog
            {
                RubricVersion = declaring,
                Lenses = [new CatalogLens("code_health", "Code Health")],
                Dimensions = [new CatalogDimension { Id = "D1", Name = "Complexity", Lens = "code_health", Evaluator = "tool" }],
            };
            File.WriteAllText(Path.Combine(dir, "rubric-catalog.json"), catalog.ToJson());
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A temp dir we could not remove must never fail a test run.
            }
        }
    }
}
