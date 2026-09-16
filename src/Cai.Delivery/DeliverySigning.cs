using Cai.Scoring;
using NSec.Cryptography;

namespace Cai.Delivery;

/// <summary>Shared Ed25519 constants + the raw sign/verify primitives over canonical bytes.</summary>
public static class DeliverySigning
{
    /// <summary>The one signature algorithm the standard uses — Ed25519 (RFC 8032 PureEdDSA).</summary>
    public const string Algorithm = "Ed25519";

    internal static byte[] Sign(Key key, ReadOnlySpan<byte> data) => SignatureAlgorithm.Ed25519.Sign(key, data);

    internal static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        var pub = NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, publicKey, KeyBlobFormat.RawPublicKey);
        return SignatureAlgorithm.Ed25519.Verify(pub, data, signature);
    }
}

/// <summary>
/// Mints signatures with a cai signing key. This is the ONLY thing that turns a payload into a signed delivery — in
/// production it lives inside cai's registry push handler, so a signed package can only originate from cai. It signs the
/// payload's canonical form, never the pretty-printed file, so re-serialization never invalidates a signature.
/// </summary>
public sealed class DeliverySigner : IDisposable
{
    private readonly Key _key;
    private readonly string _keyId;

    /// <summary>Load a signer from a cai key pair (its private seed).</summary>
    public DeliverySigner(DeliveryKeyPair keyPair)
    {
        ArgumentNullException.ThrowIfNull(keyPair);
        _keyId = keyPair.KeyId;
        _key = Key.Import(SignatureAlgorithm.Ed25519, Base64Url.Decode(keyPair.PrivateKey), KeyBlobFormat.RawPrivateKey);
    }

    /// <summary>The key id this signer stamps into the signature (and the issuer).</summary>
    public string KeyId => _keyId;

    /// <summary>Sign a payload — canonicalize it, sign the bytes, and return the detached signature.</summary>
    public DeliverySignature Sign(DeliveryPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var canonical = CanonicalJson.Canonicalize(payload);
        return new DeliverySignature
        {
            Alg = DeliverySigning.Algorithm,
            KeyId = _keyId,
            Canon = CanonicalJson.Method,
            Value = Base64Url.Encode(DeliverySigning.Sign(_key, canonical)),
        };
    }

    /// <summary>Wrap a payload into a fully signed package. The payload's <see cref="DeliveryIssuer.KeyId"/> is set to
    /// this signer's key id so the issuer and signature agree.</summary>
    public DeliveryPackage SignPackage(DeliveryPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var stamped = payload with { Issuer = payload.Issuer with { KeyId = _keyId } };
        return new DeliveryPackage { Payload = stamped, Signature = Sign(stamped) };
    }

    /// <summary>Dispose the underlying signing key.</summary>
    public void Dispose() => _key.Dispose();
}

/// <summary>The outcome of verifying a delivery: whether the signature is authentic, and — when checked — whether the
/// verdict reproduces from the embedded evidence. <see cref="SignatureValid"/> establishes authenticity;
/// <see cref="Reproduced"/> is the independent second check that the number is honest math, not just a signed
/// claim.</summary>
public sealed record DeliveryVerification(
    bool SignatureValid,
    string? Reason,
    bool? Reproduced = null,
    double? ComputedCai = null,
    double? ClaimedCai = null)
{
    /// <summary>
    /// True only when the signature is authentic AND the headline actually reproduced.
    /// </summary>
    /// <remarks>
    /// Named for the two facts it conjoins, NOT "Trustworthy", which it was called until 2026-08-07. Whitepaper W3
    /// spends a section on why a signature plus reproducing arithmetic is not the same as a codebase being good, or
    /// safe, or fit to buy — it establishes that this document is ours, unedited, and internally consistent. An
    /// unqualified trust label invites exactly the over-reading the paper warns against, and the /verify page's own
    /// copy already said "Authentic and reproducing" while the field underneath said "trustworthy".
    /// <para>The conjunction is on <c>Reproduced == true</c>, not <c>!= false</c>. Until 2026-09-16 an ABSENT
    /// reproduction — a signature-only check, which folds nothing — read as a passing one, so a verification that had
    /// not reproduced anything still answered true to the question "did this reproduce?". An unmeasured property
    /// defaults to the benign answer only if you let it.</para>
    /// </remarks>
    public bool AuthenticAndReproducing => SignatureValid && Reproduced == true;
}

/// <summary>
/// Verifies a delivery package a consumer received — the buyer-side trust check. It does two independent things: (1)
/// confirms cai's Ed25519 signature over the payload's canonical form using the published public key (authenticity — the
/// artifact is cai's and unedited), and (2) optionally re-folds the embedded evidence through <see cref="CaiScorer"/> and
/// confirms it reproduces the stated headline (reproducibility — the number is honest, not merely signed). Runs fully
/// offline against a pinned key set.
/// </summary>
public static class DeliveryVerifier
{
    /// <summary>
    /// Check AUTHENTICITY alone: that this package is the issuer's, unedited. It folds nothing, so it says nothing
    /// about whether the headline is the number the evidence produces — <see cref="DeliveryVerification.Reproduced"/>
    /// stays null and <see cref="DeliveryVerification.AuthenticAndReproducing"/> is false. Use
    /// <see cref="Verify(DeliveryPackage, DeliveryPublicKeySet, ResolvedRubric, double)"/> for the full check.
    /// </summary>
    public static DeliveryVerification VerifySignature(DeliveryPackage package, DeliveryPublicKeySet keys)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(keys);

        var major = DeliverySchema.MajorOf(package.Payload.SchemaVersion);
        if (major is null)
        {
            return new DeliveryVerification(false, $"unparseable schemaVersion '{package.Payload.SchemaVersion}'");
        }

        if (major != DeliverySchema.SupportedMajor)
        {
            return new DeliveryVerification(false,
                $"unsupported package MAJOR {major} (this build implements {DeliverySchema.SupportedMajor})");
        }

        var sig = package.Signature;
        if (!string.Equals(sig.Alg, DeliverySigning.Algorithm, StringComparison.Ordinal))
        {
            return new DeliveryVerification(false, $"unsupported signature alg '{sig.Alg}'");
        }

        if (!string.Equals(sig.Canon, CanonicalJson.Method, StringComparison.Ordinal))
        {
            return new DeliveryVerification(false, $"unsupported canonicalization '{sig.Canon}'");
        }

        if (!string.Equals(sig.KeyId, package.Payload.Issuer.KeyId, StringComparison.Ordinal))
        {
            return new DeliveryVerification(false, "signature keyId does not match issuer keyId");
        }

        var pub = keys.Resolve(sig.KeyId);
        if (pub is null)
        {
            return new DeliveryVerification(false, $"no public key for keyId '{sig.KeyId}'");
        }

        bool authentic;
        try
        {
            authentic = DeliverySigning.Verify(
                Base64Url.Decode(pub.PublicKey), CanonicalJson.Canonicalize(package.Payload), Base64Url.Decode(sig.Value));
        }
        catch (FormatException e)
        {
            return new DeliveryVerification(false, $"malformed signature or key bytes: {e.Message}");
        }

        if (!authentic)
        {
            return new DeliveryVerification(false, "signature does not verify (tampered payload or wrong key)");
        }

        return new DeliveryVerification(true, null);
    }

    /// <summary>
    /// The full check: the package is the issuer's AND its headline reproduces from the embedded evidence, folded under
    /// the rubric the package NAMES.
    ///
    /// <para><paramref name="rubric"/> is required, and that is the point. A re-fold is only evidence of anything if it
    /// runs the criteria the artifact was computed under; a verifier that substitutes its own build's constants
    /// confirms nothing and can contradict the issuer the moment a rubric version pins its own. The package witnesses
    /// which document those criteria came from (<see cref="DeliveryPayload.RubricContentHash"/>), so a rubric that is
    /// not that document is refused rather than quietly folded under.</para>
    /// </summary>
    /// <param name="package">The parsed package.</param>
    /// <param name="keys">The issuer key set.</param>
    /// <param name="rubric">The resolved catalog for <see cref="DeliveryPayload.RubricVersion"/>.</param>
    /// <param name="tolerance">Permitted absolute difference between the recomputed and claimed headline.</param>
    public static DeliveryVerification Verify(
        DeliveryPackage package, DeliveryPublicKeySet keys, ResolvedRubric rubric, double tolerance = 0.5)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(rubric);

        var authentic = VerifySignature(package, keys);
        if (!authentic.SignatureValid)
        {
            return authentic;
        }

        var claimed = package.Payload.Verdict.Cai;

        // The rubric handed in must BE the one the artifact names. Version first: it is present on every package,
        // including those minted before the digest field existed.
        if (!string.Equals(package.Payload.RubricVersion, rubric.RubricVersion, StringComparison.Ordinal))
        {
            return new DeliveryVerification(true,
                $"signature valid but the supplied rubric is '{rubric.RubricVersion}' and the package names "
                + $"'{package.Payload.RubricVersion}'",
                Reproduced: false, ClaimedCai: claimed);
        }

        // Then content: a package that witnesses a digest must be verified against THAT document, not merely against
        // something sharing its version string — which is exactly the substitution the digest exists to detect.
        if (package.Payload.RubricContentHash is { } witnessed
            && !string.Equals(witnessed, rubric.ContentHash, StringComparison.Ordinal))
        {
            return new DeliveryVerification(true,
                "signature valid but the supplied rubric is not the document this package was folded under "
                + $"(package witnesses {witnessed}, supplied catalog digests to {rubric.ContentHash})",
                Reproduced: false, ClaimedCai: claimed);
        }

        // Independent reproducibility check: fold the embedded evidence UNDER THE NAMED RUBRIC and compare.
        double computed;
        try
        {
            computed = CaiScorer.Score(package.Payload.Evidence, rubric.Catalog).Headline;
        }
        catch (Exception e)
        {
            return new DeliveryVerification(true, $"signature valid but evidence could not be scored: {e.Message}",
                Reproduced: false, ClaimedCai: claimed);
        }

        var reproduced = Math.Abs(computed - claimed) <= tolerance;
        return new DeliveryVerification(
            true,
            reproduced ? null : $"signature valid but headline does not reproduce ({computed:0.00} vs claimed {claimed:0.00})",
            reproduced, Math.Round(computed, 2), claimed);
    }
}
