using System.Text.Json;

namespace Cai.Scoring;

/// <summary>
/// Loads + caches the versioned rubric catalogs codeassuranceindex.info owns — the authoritative, archived definitions of the
/// standard. Reads <c>{root}/&lt;rubricVersion&gt;/rubric-catalog.json</c>. Catalogs are immutable once published, so
/// each version is parsed once and cached. This is the source the API + UI serve and that the Watchdog surveyor calls
/// instead of carrying its own copy.
/// <para><b>Attestation invariant:</b> a catalog is served only when the <c>rubricVersion</c> it declares matches the
/// directory it is published under. A mismatch means the archive cannot attest which version of the standard the
/// document actually is — and a consumer pinning that version would verify against the wrong definition. Such a
/// catalog is withheld from <see cref="Versions"/> and <see cref="Get"/> rather than served with a caveat, and is
/// reported by <see cref="UnattestedVersions"/> so the gap is visible to operators instead of silent.</para>
/// </summary>
public sealed class RubricCatalogStore
{
    private const string CatalogFileName = "rubric-catalog.json";

    private readonly string _root;
    private readonly Dictionary<string, RubricCatalog> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _attestation = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Create a store rooted at <paramref name="root"/> — the directory holding one
    /// <c>&lt;rubricVersion&gt;/rubric-catalog.json</c> subfolder per published version.</summary>
    public RubricCatalogStore(string root) => _root = root;

    /// <summary>The rubric versions present AND attested, newest first (lexical sort works for the date-stamped
    /// names). Directories whose catalog declares a different version are excluded — see the type remarks.</summary>
    public IReadOnlyList<string> Versions() =>
        PublishedDirectories()
            .Where(IsAttested)
            .OrderByDescending(Sequence)
            .ToList();

    /// <summary>Published directories whose catalog declares a version other than the directory name, newest first,
    /// each with the version it wrongly declares. Empty in a healthy archive; non-empty means a published document
    /// cannot be attested and is being withheld.</summary>
    public IReadOnlyList<(string Directory, string Declares)> UnattestedVersions() =>
        PublishedDirectories()
            .Where(n => !IsAttested(n))
            .OrderByDescending(Sequence)
            .Select(n => (n, DeclaredVersion(n) ?? "(unreadable)"))
            .ToList();

    /// <summary>
    /// A version's sort key: year, month, then the SEQUENCE AS A NUMBER.
    ///
    /// <para>★ The last segment counts rubric changes within a month, so it passes 9. Ordered as text,
    /// <c>rubric-2026.09.9</c> sorts ABOVE <c>rubric-2026.09.13</c> because '9' > '1' — which made
    /// <see cref="Latest"/>, the first element of this ordering, return the wrong version from the day
    /// <c>rubric-2026.09.10</c> was published. Every consumer resolving "latest" got a stale rubric, and nothing
    /// reported it, because a stale-but-valid version verifies perfectly well.</para>
    ///
    /// <para>An unparseable name sorts LAST rather than throwing: this ordering is used to serve an archive, and one
    /// malformed directory must not take the whole listing down. It is already excluded from
    /// <see cref="Versions"/> by attestation.</para>
    /// </summary>
    private static (int Year, int Month, int Sequence) Sequence(string version)
    {
        var parts = version.StartsWith("rubric-", StringComparison.Ordinal)
            ? version["rubric-".Length..].Split('.')
            : [];

        return parts.Length == 3
               && int.TryParse(parts[0], out var year)
               && int.TryParse(parts[1], out var month)
               && int.TryParse(parts[2], out var sequence)
            ? (year, month, sequence)
            : (int.MinValue, int.MinValue, int.MinValue);
    }

    private IEnumerable<string> PublishedDirectories()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        return Directory.GetDirectories(_root)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n) && File.Exists(Path.Combine(_root, n!, CatalogFileName)))
            .Select(n => n!);
    }

    private bool IsAttested(string version) =>
        string.Equals(DeclaredVersion(version), version, StringComparison.Ordinal);

    /// <summary>The <c>rubricVersion</c> the on-disk catalog for <paramref name="version"/> declares, or null when it
    /// is missing or unreadable. Cached — catalogs are immutable once published.</summary>
    private string? DeclaredVersion(string version)
    {
        lock (_gate)
        {
            if (_attestation.TryGetValue(version, out var known))
            {
                return known;
            }
        }

        var path = Path.Combine(_root, version, CatalogFileName);
        string? declared = null;
        if (File.Exists(path))
        {
            try
            {
                declared = RubricCatalog.Parse(File.ReadAllText(path)).RubricVersion;
            }
            catch (JsonException)
            {
                // A malformed catalog is unattestable for the same reason a mislabelled one is.
                declared = null;
            }
        }

        lock (_gate)
        {
            _attestation[version] = declared;
        }

        return declared;
    }

    /// <summary>The newest published rubric version, or null when none are present.</summary>
    public string? Latest() => Versions().FirstOrDefault();

    /// <summary>The catalog for a version, or null when that version isn't published or cannot be attested (the
    /// document declares a different version than the one requested — see the type remarks). Cached.</summary>
    public RubricCatalog? Get(string rubricVersion)
    {
        if (string.IsNullOrWhiteSpace(rubricVersion))
        {
            return null;
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(rubricVersion, out var hit))
            {
                return hit;
            }
        }

        var path = Path.Combine(_root, rubricVersion, CatalogFileName);
        if (!File.Exists(path) || !IsAttested(rubricVersion))
        {
            return null;
        }

        var catalog = RubricCatalog.Parse(File.ReadAllText(path));
        lock (_gate)
        {
            _cache[rubricVersion] = catalog;
        }

        return catalog;
    }

    /// <summary>
    /// The catalog document for a version exactly as published, or null under the same conditions as
    /// <see cref="Get"/> (absent, or unattested because it declares a different version than the directory it sits in).
    ///
    /// <para>Returned as raw text rather than a parsed <see cref="RubricCatalog"/> because the caller that needs this
    /// is computing a CONTENT DIGEST, and a digest must be taken over what was actually published — a re-serialization
    /// of the parsed model would silently drop any field the model does not carry, so two materially different
    /// documents could digest identically. Canonicalization belongs downstream of this method, not inside it.</para>
    /// </summary>
    public string? RawCatalogJson(string rubricVersion)
    {
        if (string.IsNullOrWhiteSpace(rubricVersion))
        {
            return null;
        }

        var path = Path.Combine(_root, rubricVersion, CatalogFileName);
        return File.Exists(path) && IsAttested(rubricVersion) ? File.ReadAllText(path) : null;
    }
}
