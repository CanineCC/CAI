using System.Security.Cryptography;

namespace Cai.Delivery;

/// <summary>
/// The content digest of a published rubric catalog — the binding between a rubric's NAME and its CONTENT.
///
/// <para><b>Why this exists.</b> <c>RubricCatalogStore</c> enforces a *naming* invariant: a catalog is served only
/// when the <c>rubricVersion</c> it declares matches the directory it is published under. That detects a catalog
/// filed under the wrong name. It cannot detect an edit to a catalog that keeps its own name — the declared version
/// still matches the directory, so the check passes. Name consistency is not content immutability, and a frozen-rubric
/// promise that rests on the naming check alone is weaker than it sounds (ADR-0004 states the retention policy; this
/// type is the mechanism that makes a breach of it detectable).</para>
///
/// <para><b>What it gives you.</b> A digest over the catalog's CANONICAL bytes (RFC 8785 / JCS via
/// <see cref="CanonicalJson"/>), so it is stable under reformatting, key reordering and insignificant whitespace, and
/// changes if and only if the catalog's semantic content changes. Published per version by the rubric API, it lets any
/// reader pin what a rubric contained at a point in time and compare later. Carried inside a signed delivery payload,
/// it makes every issued report a witness: the signature covers the digest, so a subsequent edit to that rubric version
/// is provable by anyone still holding the report — and issued copies cannot be recalled (ADR-0010).</para>
///
/// <para>The digest is a detection mechanism, not a prevention one. It does not stop a publisher editing a catalog; it
/// makes the edit evident to a third party who recorded the earlier digest, which is the strongest property available
/// to a system whose publisher is also the party being trusted.</para>
/// </summary>
public static class RubricDigest
{
    /// <summary>The digest algorithm label carried alongside the value, so the wire format can change algorithm later
    /// without ambiguity about how an older value was produced.</summary>
    public const string Algorithm = "sha256";

    /// <summary>The canonical content digest of a rubric catalog document, as <c>sha256:&lt;base64url&gt;</c>.
    /// Throws <see cref="System.Text.Json.JsonException"/> on malformed JSON — an unparseable catalog has no
    /// well-defined content and must not be assigned a digest.</summary>
    public static string Of(string catalogJson)
    {
        ArgumentNullException.ThrowIfNull(catalogJson);
        var canonical = CanonicalJson.Canonicalize(catalogJson);
        return $"{Algorithm}:{Base64Url.Encode(SHA256.HashData(canonical))}";
    }

    /// <summary>True when <paramref name="catalogJson"/> still digests to <paramref name="expected"/>. Returns false on
    /// a null or empty expectation rather than throwing, so a caller holding a pre-digest artifact degrades to "cannot
    /// check" instead of "failed".</summary>
    public static bool Matches(string catalogJson, string? expected) =>
        !string.IsNullOrWhiteSpace(expected)
        && string.Equals(Of(catalogJson), expected, StringComparison.Ordinal);
}
