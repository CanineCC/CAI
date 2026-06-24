namespace Cai.Scoring;

/// <summary>
/// Loads + caches the versioned rubric catalogs cai.canine.dev owns — the authoritative, archived definitions of the
/// standard. Reads <c>{root}/&lt;rubricVersion&gt;/rubric-catalog.json</c>. Catalogs are immutable once published, so
/// each version is parsed once and cached. This is the source the API + UI serve and that the Watchdog surveyor calls
/// instead of carrying its own copy.
/// </summary>
public sealed class RubricCatalogStore
{
    private readonly string _root;
    private readonly Dictionary<string, RubricCatalog> _cache = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public RubricCatalogStore(string root) => _root = root;

    /// <summary>The rubric versions present, newest first (lexical sort works for the date-stamped names).</summary>
    public IReadOnlyList<string> Versions()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        return Directory.GetDirectories(_root)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n) && File.Exists(Path.Combine(_root, n!, "rubric-catalog.json")))
            .Select(n => n!)
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The newest published rubric version, or null when none are present.</summary>
    public string? Latest() => Versions().FirstOrDefault();

    /// <summary>The catalog for a version, or null when that version isn't published. Cached.</summary>
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

        var path = Path.Combine(_root, rubricVersion, "rubric-catalog.json");
        if (!File.Exists(path))
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
