using System.Text.Json;

namespace Cai.Scoring;

/// <summary>
/// Loads + caches the versioned rubric catalogs cai.canine.dev owns — the authoritative, archived definitions of the
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
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>Published directories whose catalog declares a version other than the directory name, newest first,
    /// each with the version it wrongly declares. Empty in a healthy archive; non-empty means a published document
    /// cannot be attested and is being withheld.</summary>
    public IReadOnlyList<(string Directory, string Declares)> UnattestedVersions() =>
        PublishedDirectories()
            .Where(n => !IsAttested(n))
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .Select(n => (n, DeclaredVersion(n) ?? "(unreadable)"))
            .ToList();

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
}
