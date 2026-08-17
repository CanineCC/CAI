using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The standard must not disagree with itself across two of its own doors.
/// </summary>
/// <remarks>
/// <para>★★ The claim classes were written down TWICE — a hand-typed string set guarding submissions, and the
/// enum the publication side enforces. A class added to one and not the other lets a finding be submitted
/// under a label no published rate can account for, and nothing anywhere says so. The recency strata were
/// worse: submissions required the declaration to be PRESENT and never checked its values, so a vendor could
/// be accepted with "quite-fresh" and then find the publication endpoint refusing a vocabulary the submission
/// endpoint had waved through.</para>
///
/// <para>★ This is the drift a specification is most vulnerable to, because both halves look right in
/// isolation and the disagreement only appears to a participant, at the worst moment.</para>
/// </remarks>
public sealed class VocabularyDriftTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void STAR_The_Submission_Gate_Uses_The_Same_Claim_Classes_As_The_Publication_Gate()
    {
        var published = Enum.GetValues<ClaimClass>().Select(ClaimSpecificity.Wire).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(published.Count, NoiseSubmissions.ClaimClasses.Count);
        Assert.All(published, c => Assert.Contains(c, NoiseSubmissions.ClaimClasses));
    }

    [Fact]
    public void STAR_Every_Claim_Class_Round_Trips_Through_Its_Wire_Value()
    {
        // ★ A class whose wire spelling does not parse back is a class a submitter cannot send.
        foreach (var value in Enum.GetValues<ClaimClass>())
        {
            Assert.Equal(value, ClaimSpecificity.ParseOrNull(ClaimSpecificity.Wire(value)));
        }
    }

    [Fact]
    public void STAR_Every_Recency_Stratum_Round_Trips_Too()
    {
        foreach (var value in Enum.GetValues<RecencyStratum>())
        {
            Assert.Equal(value, RecencyStrata.ParseOrNull(RecencyStrata.Wire(value)));
        }
    }

    [Fact]
    public async Task STAR_The_Method_Endpoint_Publishes_Exactly_The_Vocabularies_That_Are_Enforced()
    {
        // ★★ /method is what a participant reads before building anything. A vocabulary published there that
        // the gates do not accept — or a gate that accepts something /method never mentions — is the standard
        // lying to the only person who reads it carefully.
        using var client = fx.Client();
        var text = await client.GetStringAsync("/api/noise/method", Ct);
        var json = JsonDocument.Parse(text).RootElement;

        var advertisedClasses = json.GetProperty("claimClasses").EnumerateArray()
            .Select(c => c.GetProperty("claimClass").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            Enum.GetValues<ClaimClass>().Select(ClaimSpecificity.Wire).ToHashSet(StringComparer.OrdinalIgnoreCase),
            advertisedClasses);

        var advertisedRecall = json.GetProperty("recallMethods").EnumerateArray()
            .Select(m => m.GetString()!).ToList();
        Assert.Equal(PublicationContract.RecallMethods, advertisedRecall);
        Assert.All(advertisedRecall, m => Assert.True(PublicationContract.IsKnownRecallMethod(m)));

        // The ceiling a reader is shown is the ceiling that fires.
        Assert.Equal(
            PublicationContract.MaxExclusionRate,
            json.GetProperty("maxExclusionRate").GetDouble());
    }

    [Fact]
    public async Task STAR_A_Submission_With_An_Unknown_Stratum_Is_Refused_At_The_DOOR()
    {
        // ★★ Refused where it arrives, not two endpoints later. Previously the declaration's PRESENCE was
        // required and its values were not checked at all, so the disagreement surfaced only when the vendor
        // tried to publish — after the run, after the judging, with the no-withdrawal rule already binding.
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/submissions", new
        {
            // ★ The period with a published draw — an unpublished one 404s before any vocabulary
            // check is reached, which would have made this test pass for the wrong reason.
            period = "2026-09",
            tool = "drift-probe",
            toolVersion = "1.0",
            recency = new object[] { new { repoId = "acme/x", stratum = "quite-fresh" } },
            findings = Array.Empty<object>(),
        }, Ct);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        // ★ A RECEIPT IS ALWAYS ISSUED, and that is deliberate: the receipt is the no-withdrawal record, so
        // even a rejected run is on the register and cannot be quietly replaced by a better one. The refusal
        // is `accepted: false` with its reasons — not an HTTP error, which would leave no trace.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("accepted").GetBoolean());
        Assert.Contains("quite-fresh",
            string.Join(" ", body.GetProperty("problems").EnumerateArray().Select(p => p.GetString())),
            StringComparison.Ordinal);
    }
}
