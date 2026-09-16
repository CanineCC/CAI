using Cai.Delivery;
using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// ★★ THE REGISTRY'S TWO RUBRIC ENDPOINTS MUST AGREE ABOUT WHAT A VERSION CONTAINS.
///
/// <para><c>/api/rubrics/{v}/digest</c> hashes the RAW archived document (<c>RawCatalogJson</c>).
/// <c>/api/rubrics/{v}/catalog</c> serves <c>RubricCatalog.ToJson()</c> — the parsed model re-serialized. A
/// consumer does the obvious thing: fetch the catalog, fold under it, and digest it to check against the
/// digest endpoint (or against the <c>rubricContentHash</c> in a delivery). If the model does not round-trip,
/// those two answers differ and the consumer's verification fails for a reason nothing in their possession
/// explains.</para>
///
/// <para>That was live until 2026-09-16: <c>CatalogDimension.DeepScan</c> was a non-nullable bool, so
/// <c>ToJson()</c> added <c>"deepScan": false</c> to every dimension of every catalog the endpoint served,
/// while the digest endpoint hashed a document without it. Canonicalization does not rescue this — it
/// normalizes formatting and ordering, not an added field.</para>
///
/// <para>Swept over EVERY published version in the archive, so this cannot pass by picking a lucky one.</para>
/// </summary>
public sealed class RegistryDigestAgreementTests
{
    private static string ArchiveRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "rubrics")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "rubrics");
    }

    [Fact]
    public void Every_published_catalog_digests_the_same_whether_served_raw_or_reserialized()
    {
        var store = new RubricCatalogStore(ArchiveRoot());
        var versions = store.Versions();

        Assert.NotEmpty(versions); // an empty archive would make this vacuous

        var disagreed = new List<string>();
        foreach (var version in versions)
        {
            var raw = store.RawCatalogJson(version);
            if (raw is null) continue;

            var servedByDigestEndpoint = RubricDigest.Of(raw);
            var servedByCatalogEndpoint = RubricDigest.Of(RubricCatalog.Parse(raw).ToJson());

            if (!string.Equals(servedByDigestEndpoint, servedByCatalogEndpoint, StringComparison.Ordinal))
            {
                disagreed.Add(version);
            }
        }

        Assert.True(
            disagreed.Count == 0,
            $"{disagreed.Count} of {versions.Count} published rubric versions digest differently depending on which "
            + $"endpoint served them: {string.Join(", ", disagreed)}. A consumer fetching the catalog and checking it "
            + "against the digest endpoint would be told the archive had been tampered with.");
    }
}
