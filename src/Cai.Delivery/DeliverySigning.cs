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
/// verdict reproduces from the embedded evidence. A package is trustworthy only when <see cref="SignatureValid"/> is
/// true; <see cref="Reproduced"/> is the independent second check that the number is honest math, not just a signed
/// claim.</summary>
public sealed record DeliveryVerification(
    bool SignatureValid,
    string? Reason,
    bool? Reproduced = null,
    double? ComputedCai = null,
    double? ClaimedCai = null)
{
    /// <summary>True only when the signature is authentic AND (if evidence was folded) the headline reproduced.</summary>
    public bool Trustworthy => SignatureValid && Reproduced != false;
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
    /// <summary>Verify a package (parsed) against a key set. <paramref name="reproduce"/> also re-folds the evidence and
    /// checks it reproduces the verdict headline within <paramref name="tolerance"/>.</summary>
    public static DeliveryVerification Verify(
        DeliveryPackage package, DeliveryPublicKeySet keys, bool reproduce = true, double tolerance = 0.5)
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

        if (!reproduce)
        {
            return new DeliveryVerification(true, null);
        }

        // Independent reproducibility check: fold the embedded evidence and compare to the stated headline.
        double computed;
        try
        {
            computed = CaiScorer.Score(package.Payload.Evidence).Headline;
        }
        catch (Exception e)
        {
            return new DeliveryVerification(true, $"signature valid but evidence could not be scored: {e.Message}",
                Reproduced: false, ClaimedCai: package.Payload.Verdict.Cai);
        }

        var claimed = package.Payload.Verdict.Cai;
        var reproduced = Math.Abs(computed - claimed) <= tolerance;
        return new DeliveryVerification(
            true,
            reproduced ? null : $"signature valid but headline does not reproduce ({computed:0.00} vs claimed {claimed:0.00})",
            reproduced, Math.Round(computed, 2), claimed);
    }
}
