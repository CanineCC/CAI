using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cai.Web.Noise;

/// <summary>The corpus as published: the pool, the draws, and whether the signature over them checks out.</summary>
/// <param name="SignatureValid">
/// ★★ Whether the shipped signature verifies over the shipped bytes. FALSE means the pool and the signature have
/// drifted, which is the only thing signing was for.
/// </param>
/// <param name="Problem">Why it did not verify, when it did not.</param>
public sealed record CorpusManifestDocument(
    string Version,
    string SamplerVersion,
    string KeyId,
    string Signature,
    bool SignatureValid,
    string? Problem,
    HoldoutRules Rules,
    IReadOnlyDictionary<string, NoiseCorpus.PublishedDraw> Draws,
    IReadOnlyList<HoldoutCandidate> Candidates);

/// <summary>
/// The signed, versioned corpus manifest — 01 §2's "timestamped and signed, before any scanner runs".
/// </summary>
/// <remarks>
/// <para>★★ THE POOL WAS A C# ARRAY. A pool shipped as code can be edited in a commit that also edits the tests,
/// and the reproducible draw then proves only that the sampler is deterministic — not that the pool it ran over is
/// the pool that was published. <c>NoiseCorpus</c> said so about itself: this had to land "before the standard
/// invites a second participant".</para>
///
/// <para>★★ THE SIGNATURE IS OVER THE FILE'S EXACT BYTES, so the formatting is part of the contract. A
/// reformatter breaks verification, and that is the intended behaviour: "the bytes that were signed" is the only
/// definition that cannot be argued with, and a canonicalising serialiser is one more thing that can differ
/// between the signer and the checker.</para>
///
/// <para>★★ AND VERIFICATION IS SOMETHING A THIRD PARTY RUNS. There is deliberately no endpoint reporting
/// "signature: valid" — that would be the standard attesting to its own signature, which is evidence of nothing.
/// The manifest, the detached signature and the public key are published as files, and
/// <see cref="VerificationInstructions"/> is the <c>openssl</c> command over them.</para>
/// </remarks>
public static class CorpusManifest
{
    /// <summary>The manifest file, as published.</summary>
    public const string ManifestFileName = "noise-corpus-1.0.json";

    /// <summary>The detached signature over <see cref="ManifestFileName"/>'s exact bytes.</summary>
    public const string SignatureFileName = "noise-corpus-1.0.json.sig";

    /// <summary>The public key the signature verifies against.</summary>
    public const string PublicKeyFileName = "cai-corpus-dev-2026-08.pub.pem";

    /// <summary>The signature algorithm, published so a checker knows what to run.</summary>
    public const string Algorithm = "ecdsa-p256-sha256";

    /// <summary>
    /// How a third party checks the corpus for themselves.
    /// </summary>
    /// <remarks>
    /// ★ Published beside the files rather than in a document somebody has to be handed. "Verifiable" means
    /// somebody can run it, and the command is the whole claim.
    /// </remarks>
    public static readonly string VerificationInstructions =
        $"""
        The corpus is published as three files: the manifest ({ManifestFileName}), a detached signature over its
        exact bytes ({SignatureFileName}), and the public key it verifies against ({PublicKeyFileName}).

        Check it yourself — no CAI code involved, no endpoint to trust:

            openssl dgst -sha256 -verify {PublicKeyFileName} -signature {SignatureFileName} {ManifestFileName}

        It prints "Verified OK" or "Verification Failure". Algorithm: {Algorithm}.

        The signature covers the file's BYTES. Reformatting the JSON breaks verification, deliberately: "the
        bytes that were signed" is the only definition that cannot be argued with.
        """;

    /// <summary>
    /// Why a manifest may not be served, or null when it may.
    /// </summary>
    /// <remarks>
    /// ★★ A PURE FUNCTION SO THE FAIL-CLOSED PATH HAS A TEST. The only other way to exercise it is to ship a
    /// broken manifest, which breaks every other test in the suite — so the decision would have been the one
    /// piece of this item nobody had ever seen run.
    /// </remarks>
    public static string? RefusalReason(CorpusManifestDocument manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.SignatureValid
            ? null
            : "the published corpus manifest does not verify against its signature, so no draw can be served "
            + "from it. A holdout from a pool nobody can check is not a holdout.";
    }

    private static readonly Lazy<CorpusManifestDocument> Cached = new(LoadCore);

    /// <summary>The manifest, parsed and verified once.</summary>
    public static CorpusManifestDocument Load() => Cached.Value;

    /// <summary>The manifest's shipped bytes, exactly as signed.</summary>
    public static byte[] ReadShippedBytes() => ReadResource(ManifestFileName);

    /// <summary>The shipped detached signature.</summary>
    public static byte[] ReadShippedSignature() => ReadResource(SignatureFileName);

    /// <summary>
    /// Verify a signature over some bytes with the shipped public key.
    /// </summary>
    /// <remarks>
    /// ★ Exposed so a test can tamper the bytes and watch it fail. A verification path with no failing test is a
    /// verification path nobody has seen work.
    /// </remarks>
    public static bool Verify(byte[] manifestBytes, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);
        ArgumentNullException.ThrowIfNull(signature);

        using var key = ECDsa.Create();
        key.ImportFromPem(System.Text.Encoding.UTF8.GetString(ReadResource(PublicKeyFileName)));

        return key.VerifyData(
            manifestBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    private static CorpusManifestDocument LoadCore()
    {
        var bytes = ReadShippedBytes();
        var signature = ReadShippedSignature();

        bool valid;
        string? problem = null;
        try
        {
            valid = Verify(bytes, signature);
            if (!valid)
            {
                problem =
                    $"the signature in {SignatureFileName} does not verify over {ManifestFileName}. Either the "
                  + "manifest was edited without re-signing it, or it was signed by a different key. A draw "
                  + "from an unverifiable pool is not a draw.";
            }
        }
        catch (CryptographicException ex)
        {
            valid = false;
            problem = $"the corpus signature could not be checked: {ex.Message}";
        }

        var root = JsonDocument.Parse(bytes).RootElement;

        var rules = root.GetProperty("rules");
        var candidates = root.GetProperty("candidates").EnumerateArray()
            .Select(c => new HoldoutCandidate(
                RepoId: c.GetProperty("repoId").GetString()!,
                Language: c.GetProperty("language").GetString()!,
                ProductionLoc: c.GetProperty("productionLoc").GetInt32(),
                Licence: c.GetProperty("licence").GetString()!,
                PinnedSha: c.GetProperty("pinnedSha").GetString()!,

                // ★★ Part of what the SIGNATURE covers. A reservation recorded only in code could be quietly
                // un-reserved in the commit that needed it un-reserved.
                Reserved: c.TryGetProperty("reserved", out var reserved) && reserved.GetBoolean()))
            .ToList();

        var draws = root.GetProperty("draws").EnumerateArray()
            .ToDictionary(
                d => d.GetProperty("period").GetString()!,
                d => new NoiseCorpus.PublishedDraw(
                    d.GetProperty("seed").GetString()!,
                    d.GetProperty("drawnAt").GetDateTimeOffset()),
                StringComparer.OrdinalIgnoreCase);

        return new CorpusManifestDocument(
            Version: root.GetProperty("manifestVersion").GetString()!,
            SamplerVersion: root.GetProperty("samplerVersion").GetString()!,
            KeyId: root.GetProperty("keyId").GetString()!,

            // ★ Base64 of the DER signature, so it can travel in JSON beside the draw it covers.
            Signature: Convert.ToBase64String(signature),
            SignatureValid: valid,
            Problem: problem,
            Rules: new HoldoutRules(
                TargetProductionLocPerLanguage: rules.GetProperty("targetProductionLocPerLanguage").GetInt32(),
                MaxRepositoryLoc: rules.GetProperty("maxRepositoryLoc").GetInt32(),
                MinRepositoriesPerLanguage: rules.GetProperty("minRepositoriesPerLanguage").GetInt32(),
                MinRepositoriesPerSlice: rules.GetProperty("minRepositoriesPerSlice").GetInt32()),
            Draws: draws,
            Candidates: candidates);
    }

    private static byte[] ReadResource(string fileName)
    {
        var assembly = typeof(CorpusManifest).Assembly;
        var name = $"Cai.Web.Noise.corpus.{fileName}";

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"the corpus resource '{name}' is not embedded. Available: "
              + string.Join(", ", assembly.GetManifestResourceNames()));

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
