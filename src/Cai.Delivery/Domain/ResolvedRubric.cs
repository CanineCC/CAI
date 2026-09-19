using Cai.Scoring;

namespace Cai.Delivery;

/// <summary>
/// A rubric catalog together with the content digest of the DOCUMENT it came from — the pair every folding path needs,
/// bound so they cannot drift apart.
///
/// <para>Two facts have to travel together to fold a score honestly. The catalog says what the rules ARE (the
/// dimension→category map and, from the <c>scoring</c> block, the OWA decays, the critical gate, the surface floor and
/// the band cutlines). The digest says WHICH published document those rules were read from, and it is what a holder of
/// an older artifact re-computes to prove a published rubric was not edited under its own name.</para>
///
/// <para>They are bound in one type because deriving the digest from a parsed catalog is not sound: the digest is taken
/// over the published bytes, and a parse→serialize round-trip need not reproduce them (an unknown field a newer
/// publisher emitted would be dropped, and the digest would then describe a document nobody published). Resolving from
/// the raw JSON — <see cref="FromPublished"/> — keeps the digest anchored to the real document; building one from an
/// in-memory catalog is <see cref="FromCatalog"/>, and its digest necessarily describes that constructed catalog and
/// not any archived file.</para>
/// </summary>
public sealed record ResolvedRubric
{
    private ResolvedRubric(RubricCatalog catalog, string? contentHash)
    {
        Catalog = catalog;
        ContentHash = contentHash;
    }

    /// <summary>The rules the fold runs under.</summary>
    public RubricCatalog Catalog { get; }

    /// <summary>
    /// The content digest of the PUBLISHED document <see cref="Catalog"/> was read from, as
    /// <c>sha256:&lt;base64url&gt;</c> — or null when there is no published document.
    ///
    /// <para>Null is not a missing value, it is the truthful one. The digest exists so a holder of an artifact can
    /// re-fetch the named version and prove it was not edited under its own name; that check needs something to
    /// fetch. A catalog built in memory (<see cref="FromCatalog"/>) has no such counterpart, so witnessing a digest
    /// of it would assert a check nobody can perform — and would look exactly like a real one.</para>
    /// </summary>
    public string? ContentHash { get; }

    /// <summary>The rubric version this resolves; echoed from the catalog.</summary>
    public string RubricVersion => Catalog.RubricVersion;

    /// <summary>
    /// Resolve from the PUBLISHED catalog document — the authoritative path, and the one every production caller should
    /// use. The digest is taken over the bytes as published, so it matches what any other holder of that version computes.
    /// </summary>
    /// <param name="catalogJson">The catalog document exactly as served by the archive.</param>
    public static ResolvedRubric FromPublished(string catalogJson)
    {
        ArgumentNullException.ThrowIfNull(catalogJson);
        return new ResolvedRubric(RubricCatalog.Parse(catalogJson), RubricDigest.Of(catalogJson));
    }

    /// <summary>
    /// Resolve the published catalog for <paramref name="rubricVersion"/> out of an archive store, from its RAW
    /// document so the digest is the one every other holder of that version computes. Null when the store does not
    /// serve that version — the caller must refuse rather than fold under a substitute.
    /// </summary>
    /// <param name="store">The archive store.</param>
    /// <param name="rubricVersion">The version to resolve.</param>
    public static ResolvedRubric? FromStore(RubricCatalogStore store, string rubricVersion)
    {
        ArgumentNullException.ThrowIfNull(store);
        var raw = store.RawCatalogJson(rubricVersion);
        return raw is null ? null : FromPublished(raw);
    }

    /// <summary>
    /// Resolve from a catalog held in memory — for a producer that BUILT the catalog, for a delivery whose "rubric"
    /// is not a published CAI version at all (an internal contract framework), and for tests.
    ///
    /// <para><see cref="ContentHash"/> is NULL for these: there is no published document to witness, so the payload
    /// omits the field exactly as it did before the field existed. Folding still happens under the catalog given.</para>
    /// </summary>
    /// <param name="catalog">The catalog to fold under.</param>
    public static ResolvedRubric FromCatalog(RubricCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return new ResolvedRubric(catalog, contentHash: null);
    }
}
