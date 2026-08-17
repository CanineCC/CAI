using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The corpus as a signed manifest: the draw rests on something better than our word.
/// </summary>
/// <remarks>
/// <para>★★ 01 §2 REQUIRES THE DRAW "TIMESTAMPED AND SIGNED, BEFORE ANY SCANNER RUNS", and the corpus was a C#
/// array. A pool shipped as code can be edited in a commit that also edits the tests, and the reproducible draw
/// then proves only that the sampler is deterministic — not that the pool it ran over is the pool that was
/// published. <c>NoiseCorpus</c> said this about itself: it must land "before the standard invites a second
/// participant".</para>
///
/// <para>★★ AND IT FAILS CLOSED. An unverifiable manifest serves NO draws rather than serving them unsigned: a
/// holdout endpoint that quietly degrades to "here is the pool, unsigned" is worse than one that stops, because
/// the degradation is invisible in the thing it hands back.</para>
///
/// <para>★ Verification is <c>openssl</c> over the published bytes and the published public key — a runnable step
/// a third party performs themselves. An endpoint that reported "signature: valid" would be the standard
/// attesting to its own signature, which is not evidence of anything.</para>
/// </remarks>
public sealed class SignedCorpusTests
{
    [Fact]
    public void STAR_The_Shipped_Manifest_Verifies_Against_The_Shipped_Key()
    {
        // ★★ THE GUARD THAT MATTERS. It fails the moment somebody edits the corpus without re-signing it — which
        // is the only way the pool can drift from the published one.
        var manifest = CorpusManifest.Load();

        Assert.True(manifest.SignatureValid, manifest.Problem ?? "the signature did not verify");
        Assert.False(string.IsNullOrWhiteSpace(manifest.Version));
        Assert.False(string.IsNullOrWhiteSpace(manifest.KeyId));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Signature));
    }

    [Fact]
    public void STAR_A_TAMPERED_Manifest_Fails_Verification()
    {
        // ★★ THE TEST THE ITEM EXISTS FOR. One byte of the pool changed — a repository swapped for a friendlier
        // one — and the signature must stop matching. Without this the manifest is decoration.
        var original = CorpusManifest.ReadShippedBytes();
        var tampered = Encoding.UTF8.GetString(original)
            .Replace("\"dotnet/aspnetcore\"", "\"a-much-quieter/repository\"", StringComparison.Ordinal);

        Assert.NotEqual(Encoding.UTF8.GetString(original), tampered);

        var verified = CorpusManifest.Verify(
            Encoding.UTF8.GetBytes(tampered), CorpusManifest.ReadShippedSignature());

        Assert.False(verified);
    }

    [Fact]
    public void STAR_A_Manifest_Signed_By_ANOTHER_Key_Fails()
    {
        // ★★ Otherwise "signed" means "somebody signed it", and anybody with a keypair can publish a draw. The
        // key id is published so a reader knows WHICH key they are trusting.
        using var impostor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = CorpusManifest.ReadShippedBytes();
        var forged = impostor.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        Assert.False(CorpusManifest.Verify(bytes, forged));
    }

    [Fact]
    public void STAR_The_CODE_And_The_MANIFEST_Agree_About_The_Pool()
    {
        // ★★ THE DUPLICATION THIS ITEM COULD HAVE INTRODUCED. Shipping a manifest beside a hard-coded array would
        // give the standard two pools and one signature: the endpoints would serve the array and the signature
        // would attest to the file. NoiseCorpus now reads the manifest, and this asserts it — a re-derivation from
        // the same source, so it fails if the two ever diverge again.
        var manifest = CorpusManifest.Load();

        Assert.Equal(manifest.Candidates.Count, NoiseCorpus.Candidates.Count);
        Assert.Equal(
            manifest.Candidates.Select(c => c.RepoId).Order(StringComparer.Ordinal),
            NoiseCorpus.Candidates.Select(c => c.RepoId).Order(StringComparer.Ordinal));

        Assert.Equal(manifest.Draws.Count, NoiseCorpus.Draws.Count);
        foreach (var (period, draw) in manifest.Draws)
        {
            Assert.Equal(draw.Seed, NoiseCorpus.Draws[period].Seed);
            Assert.Equal(draw.DrawnAt, NoiseCorpus.Draws[period].DrawnAt);
        }

        Assert.Equal(manifest.SamplerVersion, NoiseCorpus.SamplerVersion);
        Assert.Equal(manifest.Rules, NoiseCorpus.Rules);
    }

    [Fact]
    public void STAR_The_Manifest_Says_What_The_SIGNATURE_IS_WORTH()
    {
        // ★★ WHO HOLDS THE KEY IS THE VALUE OF A SIGNATURE. The key is generated on and never leaves the CAI
        // host, and the deploy signs the manifest it ships — so a verifying signature proves this manifest has
        // not changed since that deploy, and NOT that an independent party vouched for it. Claiming the second
        // would be exactly the overreach the standard exists to prevent, so the custody says both halves.
        var custody = CorpusManifest.Load().KeyCustody;

        Assert.Contains("never leaves", custody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does NOT prove", custody, StringComparison.Ordinal);
    }

    [Fact]
    public void STAR_An_Unverifiable_Manifest_Is_REFUSED_Rather_Than_Served_Unsigned()
    {
        // ★★ THE FAIL-CLOSED DECISION, tested as a function because the only other way to reach it is to ship a
        // broken manifest — which breaks every other test in the suite, so it would have been the one piece of
        // this item nobody had ever seen run. A holdout endpoint that degrades to "here is the pool, unsigned" is
        // worse than one that stops: the degradation is invisible in the thing it hands back.
        var shipped = CorpusManifest.Load();
        var broken = shipped with { SignatureValid = false, Problem = "the manifest was edited" };

        Assert.Null(CorpusManifest.RefusalReason(shipped));
        Assert.NotNull(CorpusManifest.RefusalReason(broken));
        Assert.Contains("is not a holdout", CorpusManifest.RefusalReason(broken)!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Verification_Steps_Are_Documented_Where_The_Files_Are()
    {
        // ★ "Verifiable" means somebody can run it. The command, the file names and the algorithm live beside the
        // manifest rather than in a document a reader has to be handed separately.
        var instructions = CorpusManifest.VerificationInstructions;

        Assert.Contains("openssl", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(CorpusManifest.ManifestFileName, instructions, StringComparison.Ordinal);
        Assert.Contains(CorpusManifest.SignatureFileName, instructions, StringComparison.Ordinal);
        Assert.Contains(CorpusManifest.PublicKeyFileName, instructions, StringComparison.Ordinal);
    }
}

/// <summary>
/// What the draw and the pool publish about the manifest they came from.
/// </summary>
/// <remarks>
/// ★★ 01 §2 asks for the draw "timestamped AND signed". The timestamp was published and the signature was our
/// word — so a reader had the ordering claim and no way to check the pool it was made over.
/// </remarks>
public sealed class SignedCorpusApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task STAR_The_Holdout_Publishes_Which_Manifest_And_Key_It_Came_From()
    {
        using var client = fx.Client();
        var body = JsonDocument.Parse(await client.GetStringAsync("/api/noise/holdout/2026-09", Ct)).RootElement;

        var manifest = body.GetProperty("manifest");
        Assert.Equal(CorpusManifest.Load().Version, manifest.GetProperty("version").GetString());
        Assert.Equal(CorpusManifest.Load().KeyId, manifest.GetProperty("keyId").GetString());
        Assert.Equal(CorpusManifest.Algorithm, manifest.GetProperty("algorithm").GetString());
        Assert.False(string.IsNullOrWhiteSpace(manifest.GetProperty("signature").GetString()));

        // ★★ And the custody claim travels with it, rather than leaving a reader to interpret a key id.
        Assert.Contains("never leaves", manifest.GetProperty("keyCustody").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Corpus_Publishes_The_RUNNABLE_Verification_Step()
    {
        // ★★ NOT an endpoint reporting "signature: valid" — that is the standard attesting to its own signature.
        // The three file names and the openssl command, so a third party checks it without running our code.
        using var client = fx.Client();
        var body = JsonDocument.Parse(await client.GetStringAsync("/api/noise/corpus", Ct)).RootElement;

        var how = body.GetProperty("howToVerify").GetString()!;
        Assert.Contains("openssl dgst -sha256 -verify", how, StringComparison.Ordinal);
        Assert.Contains(CorpusManifest.ManifestFileName, how, StringComparison.Ordinal);

        var files = body.GetProperty("manifest").GetProperty("files");
        Assert.Equal(CorpusManifest.SignatureFileName, files.GetProperty("signature").GetString());
        Assert.Equal(CorpusManifest.PublicKeyFileName, files.GetProperty("publicKey").GetString());
    }

    [Fact]
    public async Task The_Pool_Served_Is_The_Pool_In_The_Manifest()
    {
        using var client = fx.Client();
        var body = JsonDocument.Parse(await client.GetStringAsync("/api/noise/corpus", Ct)).RootElement;

        Assert.Equal(CorpusManifest.Load().Candidates.Count, body.GetProperty("count").GetInt32());
    }
}
