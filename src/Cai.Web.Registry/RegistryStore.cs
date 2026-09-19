using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Cai.Web.Registry;

/// <summary>A stored delivery: the immutable signed artifact verbatim, plus the indexed columns the registry filters
/// and authorizes on. <see cref="PackageJson"/> is byte-for-byte what the producer published (the signature covers the
/// canonical form, so formatting is irrelevant — but serving the exact artifact is the honest thing to do).</summary>
public sealed record DeliveryRecord(
    string DeliveryId,
    string OwnerOrgId,
    string Repository,
    string? Commit,
    string? Host,
    string Producer,
    string RubricVersion,
    double Cai,
    string Band,
    string IssuedAt,
    string KeyId,
    string CanonicalSha256,
    string SignatureValue,
    string PackageJson,
    string PublishedAt,
    string? Scanner = null,
    string? ScannerVersion = null);

/// <summary>CAI's provenance + quality assessment of a scanner build, keyed by (scanner, version). The scanner NAME +
/// VERSION come from the signed <c>DeliveryProducer</c>, but the quality score is CAI's own opinion — it CANNOT ride in
/// the producer-signed payload, so it lives here. <see cref="QualityScore"/> stays null until the calc lands (deferred).</summary>
public sealed record ScannerQualityRecord(
    string Scanner,
    string Version,
    double? QualityScore,
    string? AssessedAt);

/// <summary>A stored access grant — the seller (<see cref="OwnerOrgId"/>) granting a buyer read access to some of the
/// seller's deliveries. Exactly one of <see cref="GranteeOrgId"/>/<see cref="GranteeEmail"/> is set; email grants stay
/// <c>pending</c> (they confer no access until a claim flow exists — deferred). Revocation keeps the row
/// (<c>status=revoked</c> + <see cref="RevokedAt"/>) — grants are an audit trail, not editable state.</summary>
public sealed record GrantRecord(
    string GrantId,
    string OwnerOrgId,
    string? GranteeOrgId,
    string? GranteeEmail,
    string Scope,
    IReadOnlyList<string> ScopeRefs,
    string Status,
    string? Purpose,
    string CreatedAt,
    string? ExpiresAt,
    string? RevokedAt);

/// <summary>The result of an insert that hit an existing delivery id.</summary>
public enum PublishOutcome
{
    /// <summary>Stored fresh.</summary>
    Created,

    /// <summary>The exact same artifact (same canonical payload hash + signature + owner) was already stored —
    /// idempotent re-push.</summary>
    AlreadyStored,

    /// <summary>A DIFFERENT artifact already holds this delivery id — immutability violation, rejected.</summary>
    Conflict,
}

/// <summary>
/// The registry's persistence seam. Deliveries are write-once (immutability is enforced by the store's primary key,
/// not by handler discipline); grants are append + revoke. The v1 implementation is SQLite (see
/// <see cref="SqliteRegistryStore"/>); a Postgres implementation slots in behind this interface without touching the
/// endpoints — this interface IS the Postgres seam.
/// </summary>
public interface IRegistryStore
{
    /// <summary>Insert a delivery, enforcing id immutability. Never overwrites.</summary>
    PublishOutcome InsertDelivery(DeliveryRecord record);

    /// <summary>The delivery with this id, or null.</summary>
    DeliveryRecord? GetDelivery(string deliveryId);

    /// <summary>All deliveries owned by an org, newest first.</summary>
    IReadOnlyList<DeliveryRecord> ListOwned(string orgId);

    /// <summary>The deliveries with these ids (missing ids are skipped).</summary>
    IReadOnlyList<DeliveryRecord> GetDeliveries(IReadOnlyCollection<string> deliveryIds);

    /// <summary>Deliveries owned by <paramref name="ownerOrgId"/> for any of <paramref name="repositories"/>.</summary>
    IReadOnlyList<DeliveryRecord> ListByOwnerAndRepositories(string ownerOrgId, IReadOnlyCollection<string> repositories);

    /// <summary>Insert a grant.</summary>
    void InsertGrant(GrantRecord record);

    /// <summary>The grant with this id, or null.</summary>
    GrantRecord? GetGrant(string grantId);

    /// <summary>Grants issued by an org (direction=outgoing), newest first.</summary>
    IReadOnlyList<GrantRecord> ListGrantsByOwner(string ownerOrgId);

    /// <summary>Grants naming an org as grantee (direction=incoming), newest first.</summary>
    IReadOnlyList<GrantRecord> ListGrantsByGrantee(string granteeOrgId);

    /// <summary>Mark a grant revoked (idempotent — revoking a revoked grant is a no-op).</summary>
    void RevokeGrant(string grantId, string revokedAt);

    /// <summary>CAI's quality assessment for a scanner build (scanner + version), or null when none is recorded.</summary>
    ScannerQualityRecord? GetScannerQuality(string scanner, string version);

    /// <summary>Insert or replace CAI's quality assessment for a scanner build, keyed (scanner, version).</summary>
    void UpsertScannerQuality(ScannerQualityRecord record);

    /// <summary>True when the store is reachable (the /health probe).</summary>
    bool IsHealthy();
}

/// <summary>
/// SQLite-backed registry store. Chosen for closed-loop v1 because the registry spec is storage-agnostic (it
/// explicitly defers "full registry storage") and SQLite is the simplest store that gives real durability, real
/// constraints (the delivery PK is what enforces immutability under concurrency) and zero ops dependency on the
/// existing cai deploy. WAL mode; one connection per operation. Postgres later = implement
/// <see cref="IRegistryStore"/> against Npgsql and swap the registration.
/// </summary>
public sealed class SqliteRegistryStore : IRegistryStore
{
    private readonly string _connectionString;

    /// <summary>Open (and initialize) the store at <see cref="RegistryOptions.DbPath"/>.</summary>
    public SqliteRegistryStore(IOptions<RegistryOptions> options, IHostEnvironment env, ILogger<SqliteRegistryStore> logger)
    {
        var path = options.Value.DbPath;
        var resolved = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(env.ContentRootPath, path));
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = resolved, ForeignKeys = true }.ToString();
        Initialize();
        logger.LogInformation("Registry store (SQLite) at {Path}", resolved);
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS deliveries (
                delivery_id      TEXT PRIMARY KEY,
                owner_org_id     TEXT NOT NULL,
                repository       TEXT NOT NULL,
                commit_sha       TEXT NULL,
                host             TEXT NULL,
                producer         TEXT NOT NULL,
                rubric_version   TEXT NOT NULL,
                cai              REAL NOT NULL,
                band             TEXT NOT NULL,
                issued_at        TEXT NOT NULL,
                key_id           TEXT NOT NULL,
                canonical_sha256 TEXT NOT NULL,
                signature_value  TEXT NOT NULL,
                package_json     TEXT NOT NULL,
                published_at     TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_deliveries_owner      ON deliveries(owner_org_id);
            CREATE INDEX IF NOT EXISTS ix_deliveries_repository ON deliveries(repository);

            CREATE TABLE IF NOT EXISTS grants (
                grant_id        TEXT PRIMARY KEY,
                owner_org_id    TEXT NOT NULL,
                grantee_org_id  TEXT NULL,
                grantee_email   TEXT NULL,
                scope           TEXT NOT NULL,
                scope_refs_json TEXT NOT NULL,
                status          TEXT NOT NULL,
                purpose         TEXT NULL,
                created_at      TEXT NOT NULL,
                expires_at      TEXT NULL,
                revoked_at      TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_grants_owner   ON grants(owner_org_id);
            CREATE INDEX IF NOT EXISTS ix_grants_grantee ON grants(grantee_org_id);

            CREATE TABLE IF NOT EXISTS scanner_quality (
                scanner       TEXT NOT NULL,
                version       TEXT NOT NULL,
                quality_score REAL NULL,
                assessed_at   TEXT NULL,
                PRIMARY KEY (scanner, version)
            );
            """;
        cmd.ExecuteNonQuery();

        // Scanner provenance columns on deliveries — additive on an EXISTING table (rows predate them), so ALTER…ADD
        // guarded against the "duplicate column" error that a second startup would raise (SQLite has no ADD COLUMN IF
        // NOT EXISTS). Existing rows keep NULL scanner/version; that is honest — those deliveries were stored before
        // provenance was surfaced.
        AddColumnIfMissing(conn, "deliveries", "scanner", "TEXT NULL");
        AddColumnIfMissing(conn, "deliveries", "scanner_version", "TEXT NULL");
    }

    /// <summary>Idempotent <c>ALTER TABLE … ADD COLUMN</c>: adds the column unless it already exists (checked via
    /// <c>PRAGMA table_info</c>), so re-running <see cref="Initialize"/> on an already-migrated DB is a no-op.</summary>
    private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string columnDef)
    {
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table})";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), column, StringComparison.Ordinal))
                {
                    return; // already present
                }
            }
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDef}";
        alter.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public PublishOutcome InsertDelivery(DeliveryRecord r)
    {
        using var conn = Open();
        using var insert = conn.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO deliveries (delivery_id, owner_org_id, repository, commit_sha, host, producer, rubric_version,
                                    cai, band, issued_at, key_id, canonical_sha256, signature_value, package_json, published_at,
                                    scanner, scanner_version)
            VALUES (@id, @owner, @repo, @commit, @host, @producer, @rubric, @cai, @band, @issued, @key, @hash, @sig, @json, @published,
                    @scanner, @scannerVersion)
            """;
        insert.Parameters.AddWithValue("@id", r.DeliveryId);
        insert.Parameters.AddWithValue("@owner", r.OwnerOrgId);
        insert.Parameters.AddWithValue("@repo", r.Repository);
        insert.Parameters.AddWithValue("@commit", (object?)r.Commit ?? DBNull.Value);
        insert.Parameters.AddWithValue("@host", (object?)r.Host ?? DBNull.Value);
        insert.Parameters.AddWithValue("@producer", r.Producer);
        insert.Parameters.AddWithValue("@rubric", r.RubricVersion);
        insert.Parameters.AddWithValue("@cai", r.Cai);
        insert.Parameters.AddWithValue("@band", r.Band);
        insert.Parameters.AddWithValue("@issued", r.IssuedAt);
        insert.Parameters.AddWithValue("@key", r.KeyId);
        insert.Parameters.AddWithValue("@hash", r.CanonicalSha256);
        insert.Parameters.AddWithValue("@sig", r.SignatureValue);
        insert.Parameters.AddWithValue("@json", r.PackageJson);
        insert.Parameters.AddWithValue("@published", r.PublishedAt);
        insert.Parameters.AddWithValue("@scanner", (object?)r.Scanner ?? DBNull.Value);
        insert.Parameters.AddWithValue("@scannerVersion", (object?)r.ScannerVersion ?? DBNull.Value);

        try
        {
            insert.ExecuteNonQuery();
            return PublishOutcome.Created;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) // SQLITE_CONSTRAINT — the PK enforced immutability
        {
            var existing = GetDelivery(r.DeliveryId)
                ?? throw new InvalidOperationException($"constraint hit but delivery '{r.DeliveryId}' not found");
            var identical = existing.CanonicalSha256 == r.CanonicalSha256
                            && existing.SignatureValue == r.SignatureValue
                            && existing.OwnerOrgId == r.OwnerOrgId;
            return identical ? PublishOutcome.AlreadyStored : PublishOutcome.Conflict;
        }
    }

    /// <inheritdoc />
    public DeliveryRecord? GetDelivery(string deliveryId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM deliveries WHERE delivery_id = @id";
        cmd.Parameters.AddWithValue("@id", deliveryId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadDelivery(reader) : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<DeliveryRecord> ListOwned(string orgId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM deliveries WHERE owner_org_id = @org ORDER BY issued_at DESC, delivery_id";
        cmd.Parameters.AddWithValue("@org", orgId);
        return ReadDeliveries(cmd);
    }

    /// <inheritdoc />
    public IReadOnlyList<DeliveryRecord> GetDeliveries(IReadOnlyCollection<string> deliveryIds)
    {
        if (deliveryIds.Count == 0)
        {
            return [];
        }

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM deliveries WHERE delivery_id IN ({Placeholders(cmd, "@d", deliveryIds)}) ORDER BY issued_at DESC, delivery_id";
        return ReadDeliveries(cmd);
    }

    /// <inheritdoc />
    public IReadOnlyList<DeliveryRecord> ListByOwnerAndRepositories(string ownerOrgId, IReadOnlyCollection<string> repositories)
    {
        if (repositories.Count == 0)
        {
            return [];
        }

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM deliveries WHERE owner_org_id = @org AND repository IN ({Placeholders(cmd, "@r", repositories)}) ORDER BY issued_at DESC, delivery_id";
        cmd.Parameters.AddWithValue("@org", ownerOrgId);
        return ReadDeliveries(cmd);
    }

    /// <inheritdoc />
    public void InsertGrant(GrantRecord g)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO grants (grant_id, owner_org_id, grantee_org_id, grantee_email, scope, scope_refs_json,
                                status, purpose, created_at, expires_at, revoked_at)
            VALUES (@id, @owner, @gorg, @gmail, @scope, @refs, @status, @purpose, @created, @expires, @revoked)
            """;
        cmd.Parameters.AddWithValue("@id", g.GrantId);
        cmd.Parameters.AddWithValue("@owner", g.OwnerOrgId);
        cmd.Parameters.AddWithValue("@gorg", (object?)g.GranteeOrgId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gmail", (object?)g.GranteeEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@scope", g.Scope);
        cmd.Parameters.AddWithValue("@refs", JsonSerializer.Serialize(g.ScopeRefs));
        cmd.Parameters.AddWithValue("@status", g.Status);
        cmd.Parameters.AddWithValue("@purpose", (object?)g.Purpose ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", g.CreatedAt);
        cmd.Parameters.AddWithValue("@expires", (object?)g.ExpiresAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@revoked", (object?)g.RevokedAt ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public GrantRecord? GetGrant(string grantId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM grants WHERE grant_id = @id";
        cmd.Parameters.AddWithValue("@id", grantId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadGrant(reader) : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<GrantRecord> ListGrantsByOwner(string ownerOrgId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM grants WHERE owner_org_id = @org ORDER BY created_at DESC, grant_id";
        cmd.Parameters.AddWithValue("@org", ownerOrgId);
        return ReadGrants(cmd);
    }

    /// <inheritdoc />
    public IReadOnlyList<GrantRecord> ListGrantsByGrantee(string granteeOrgId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM grants WHERE grantee_org_id = @org ORDER BY created_at DESC, grant_id";
        cmd.Parameters.AddWithValue("@org", granteeOrgId);
        return ReadGrants(cmd);
    }

    /// <inheritdoc />
    public void RevokeGrant(string grantId, string revokedAt)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE grants SET status = 'revoked', revoked_at = @at WHERE grant_id = @id AND status <> 'revoked'";
        cmd.Parameters.AddWithValue("@at", revokedAt);
        cmd.Parameters.AddWithValue("@id", grantId);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public ScannerQualityRecord? GetScannerQuality(string scanner, string version)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT scanner, version, quality_score, assessed_at FROM scanner_quality WHERE scanner = @scanner AND version = @version";
        cmd.Parameters.AddWithValue("@scanner", scanner);
        cmd.Parameters.AddWithValue("@version", version);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var scoreOrdinal = reader.GetOrdinal("quality_score");
        return new ScannerQualityRecord(
            Scanner: reader.GetString(reader.GetOrdinal("scanner")),
            Version: reader.GetString(reader.GetOrdinal("version")),
            QualityScore: reader.IsDBNull(scoreOrdinal) ? null : reader.GetDouble(scoreOrdinal),
            AssessedAt: NullableString(reader, "assessed_at"));
    }

    /// <inheritdoc />
    public void UpsertScannerQuality(ScannerQualityRecord record)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO scanner_quality (scanner, version, quality_score, assessed_at)
            VALUES (@scanner, @version, @score, @assessed)
            ON CONFLICT (scanner, version) DO UPDATE SET quality_score = @score, assessed_at = @assessed
            """;
        cmd.Parameters.AddWithValue("@scanner", record.Scanner);
        cmd.Parameters.AddWithValue("@version", record.Version);
        cmd.Parameters.AddWithValue("@score", (object?)record.QualityScore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@assessed", (object?)record.AssessedAt ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public bool IsHealthy()
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM deliveries";
            cmd.ExecuteScalar();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static string Placeholders(SqliteCommand cmd, string prefix, IReadOnlyCollection<string> values)
    {
        var names = new List<string>(values.Count);
        var i = 0;
        foreach (var v in values)
        {
            var name = $"{prefix}{i++}";
            names.Add(name);
            cmd.Parameters.AddWithValue(name, v);
        }

        return string.Join(", ", names);
    }

    private static IReadOnlyList<DeliveryRecord> ReadDeliveries(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var rows = new List<DeliveryRecord>();
        while (reader.Read())
        {
            rows.Add(ReadDelivery(reader));
        }

        return rows;
    }

    private static DeliveryRecord ReadDelivery(SqliteDataReader r) => new(
        DeliveryId: r.GetString(r.GetOrdinal("delivery_id")),
        OwnerOrgId: r.GetString(r.GetOrdinal("owner_org_id")),
        Repository: r.GetString(r.GetOrdinal("repository")),
        Commit: NullableString(r, "commit_sha"),
        Host: NullableString(r, "host"),
        Producer: r.GetString(r.GetOrdinal("producer")),
        RubricVersion: r.GetString(r.GetOrdinal("rubric_version")),
        Cai: r.GetDouble(r.GetOrdinal("cai")),
        Band: r.GetString(r.GetOrdinal("band")),
        IssuedAt: r.GetString(r.GetOrdinal("issued_at")),
        KeyId: r.GetString(r.GetOrdinal("key_id")),
        CanonicalSha256: r.GetString(r.GetOrdinal("canonical_sha256")),
        SignatureValue: r.GetString(r.GetOrdinal("signature_value")),
        PackageJson: r.GetString(r.GetOrdinal("package_json")),
        PublishedAt: r.GetString(r.GetOrdinal("published_at")),
        Scanner: NullableString(r, "scanner"),
        ScannerVersion: NullableString(r, "scanner_version"));

    private static IReadOnlyList<GrantRecord> ReadGrants(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var rows = new List<GrantRecord>();
        while (reader.Read())
        {
            rows.Add(ReadGrant(reader));
        }

        return rows;
    }

    private static GrantRecord ReadGrant(SqliteDataReader r) => new(
        GrantId: r.GetString(r.GetOrdinal("grant_id")),
        OwnerOrgId: r.GetString(r.GetOrdinal("owner_org_id")),
        GranteeOrgId: NullableString(r, "grantee_org_id"),
        GranteeEmail: NullableString(r, "grantee_email"),
        Scope: r.GetString(r.GetOrdinal("scope")),
        ScopeRefs: JsonSerializer.Deserialize<List<string>>(r.GetString(r.GetOrdinal("scope_refs_json"))) ?? [],
        Status: r.GetString(r.GetOrdinal("status")),
        Purpose: NullableString(r, "purpose"),
        CreatedAt: r.GetString(r.GetOrdinal("created_at")),
        ExpiresAt: NullableString(r, "expires_at"),
        RevokedAt: NullableString(r, "revoked_at"));

    private static string? NullableString(SqliteDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }
}
