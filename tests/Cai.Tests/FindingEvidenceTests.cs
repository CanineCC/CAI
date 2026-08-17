using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The evidence a rater is shown: where the finding is, at a revision anybody can open.
/// </summary>
/// <remarks>
/// <para>★★ THE CROWD CANNOT JUDGE WHAT IT CANNOT SEE, and nothing stored the findings. A submission's receipt kept
/// counts; the queue was built from finding ids an operator supplied; and a rater was handed an id and asked
/// whether it should have fired. The one check in the method that comes from outside the model family was
/// unanswerable by design.</para>
///
/// <para>★★ THE ID IS DERIVED FROM THE FINDING, not assigned. A submitter-chosen id cannot be matched across tools
/// — which is what the pooled union needs — and an id CAI invented per submission would change every time the same
/// defect was reported. It is a hash of the coordinates, so two tools reporting one defect produce one id.</para>
///
/// <para>★ AND THE EVIDENCE IS A LINK TO PUBLIC CODE at the pinned sha. That is the whole reason the corpus is
/// public repositories only: the rater needs no access to anything, and CAI needs to store no source.</para>
/// </remarks>
public sealed class FindingEvidenceTests
{
    [Fact]
    public void STAR_The_Same_Defect_From_TWO_Tools_Gets_ONE_Id()
    {
        // ★★ THE PROPERTY THE POOLED UNION NEEDS. Two tools reporting the same line must collide, or cross-vendor
        // matching is matching on nothing — and a submitter-chosen id can never collide by accident.
        var a = FindingKey.For("dotnet/efcore", "c1d83d0e", "src/Query.cs", 42, "D4");
        var b = FindingKey.For("dotnet/efcore", "c1d83d0e", "src/Query.cs", 42, "D4");

        Assert.Equal(a, b);
        Assert.False(string.IsNullOrWhiteSpace(a));
    }

    [Fact]
    public void STAR_A_Different_LINE_Or_RULE_Is_A_Different_Finding()
    {
        var baseline = FindingKey.For("dotnet/efcore", "c1d83d0e", "src/Query.cs", 42, "D4");

        Assert.NotEqual(baseline, FindingKey.For("dotnet/efcore", "c1d83d0e", "src/Query.cs", 43, "D4"));
        Assert.NotEqual(baseline, FindingKey.For("dotnet/efcore", "c1d83d0e", "src/Query.cs", 42, "S1"));
        Assert.NotEqual(baseline, FindingKey.For("dotnet/efcore", "c1d83d0e", "src/Other.cs", 42, "D4"));

        // ★★ AND A DIFFERENT SHA IS A DIFFERENT FINDING. "The same line" means nothing across two revisions —
        // the file may have changed underneath it, which is exactly what the pinned sha exists to settle.
        Assert.NotEqual(baseline, FindingKey.For("dotnet/efcore", "ffffffff", "src/Query.cs", 42, "D4"));
    }

    [Fact]
    public void STAR_A_Finding_With_NO_Coordinate_Still_Gets_An_Id()
    {
        // ★★ A repository-level finding has no file and no line — that is the coordinate gap, not a defect in the
        // finding. It must still be identifiable, or the dimensions that cannot supply a coordinate would silently
        // drop out of everything keyed by id.
        var repoLevel = FindingKey.For("dotnet/efcore", "c1d83d0e", null, null, "D29");

        Assert.False(string.IsNullOrWhiteSpace(repoLevel));
        Assert.NotEqual(repoLevel, FindingKey.For("dotnet/efcore", "c1d83d0e", null, null, "D36"));
    }

    [Fact]
    public void STAR_The_Evidence_Link_Points_At_The_PINNED_Revision()
    {
        // ★★ Not at the branch. A link to HEAD shows the rater code that may have changed since the run, and they
        // would be judging a finding against a file that no longer matches it.
        var link = FindingEvidence.SourceUrl("dotnet/efcore", "c1d83d0e9f2b4a67", "src/Query.cs", 42);

        Assert.Contains("c1d83d0e9f2b4a67", link!, StringComparison.Ordinal);
        Assert.Contains("src/Query.cs", link!, StringComparison.Ordinal);
        Assert.Contains("#L42", link!, StringComparison.Ordinal);
        Assert.DoesNotContain("/main/", link!, StringComparison.Ordinal);
        Assert.DoesNotContain("HEAD", link!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Finding_With_No_File_Links_To_The_Repository_At_That_Revision()
    {
        var link = FindingEvidence.SourceUrl("dotnet/efcore", "c1d83d0e", null, null);

        Assert.Contains("c1d83d0e", link!, StringComparison.Ordinal);
        Assert.DoesNotContain("#L", link!, StringComparison.Ordinal);
    }
}

/// <summary>
/// Storing what was submitted, so the crowd has something to look at.
/// </summary>
public sealed class FindingEvidenceApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(JsonElement Receipt, List<(string Repo, string Sha)> Holdout)> SubmitAsync(string tool)
    {
        using var client = fx.Client();
        var holdout = JsonDocument.Parse(await client.GetStringAsync("/api/noise/holdout/2026-09", Ct))
            .RootElement.GetProperty("repositories").EnumerateArray()
            .Select(r => (Repo: r.GetProperty("repoId").GetString()!, Sha: r.GetProperty("pinnedSha").GetString()!))
            .ToList();

        var response = await client.PostAsJsonAsync("/api/noise/submissions", new
        {
            period = "2026-09",
            tool,
            toolVersion = "engine-1.0",
            runStartedAt = "2026-08-20T09:00:00Z",
            configuration = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
            recency = holdout.Select(h => new { repoId = h.Repo, stratum = "never-trained" }),
            findings = holdout.Select(h => new
            {
                repoId = h.Repo, pinnedSha = h.Sha, filePath = "src/Thing.cs", line = 42,
                ruleId = "D4", title = "a possible null dereference", claimClass = "pointwise",
            }),
            reportedFindingCount = holdout.Count,
        }, Ct);

        return (JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone(), holdout);
    }

    [Fact]
    public async Task STAR_The_Receipt_Names_The_Finding_Ids_It_Recorded()
    {
        // ★★ The submitter needs the ids to talk about their own findings — to dispute a verdict on one, or to
        // check what the crowd was shown. An id CAI holds and does not publish is one only CAI can use.
        var (receipt, holdout) = await SubmitAsync("evidence-probe");

        var ids = receipt.GetProperty("findingIds").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(holdout.Count, ids.Count);

        // ★ Derived, so the submitter can compute them independently and check they match.
        var expected = FindingKey.For(holdout[0].Repo, holdout[0].Sha, "src/Thing.cs", 42, "D4");
        Assert.Contains(expected, ids);
    }

    [Fact]
    public async Task STAR_The_Stored_Finding_Serves_Its_Evidence()
    {
        var (_, holdout) = await SubmitAsync("evidence-served");
        var id = FindingKey.For(holdout[0].Repo, holdout[0].Sha, "src/Thing.cs", 42, "D4");

        using var client = fx.Client();
        var body = JsonDocument.Parse(await client.GetStringAsync($"/api/noise/findings/{id}", Ct)).RootElement;

        Assert.Equal(holdout[0].Repo, body.GetProperty("repoId").GetString());
        Assert.Equal(holdout[0].Sha, body.GetProperty("pinnedSha").GetString());
        Assert.Equal(42, body.GetProperty("line").GetInt32());
        Assert.Contains("null dereference", body.GetProperty("title").GetString()!, StringComparison.Ordinal);
        Assert.Contains(holdout[0].Sha, body.GetProperty("sourceUrl").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_The_Evidence_Does_NOT_Say_Which_TOOL_Reported_It()
    {
        // ★★ THE DISGUISE THE WHOLE CROWD LAYER RESTS ON. A rater told which vendor produced a finding is being
        // asked a different question — and on a standard its owner competes in, "this one is Watchdog's" is the
        // single most corrupting thing the page could leak.
        var (_, holdout) = await SubmitAsync("secret-vendor");
        var id = FindingKey.For(holdout[0].Repo, holdout[0].Sha, "src/Thing.cs", 42, "D4");

        using var client = fx.Client();
        var raw = await client.GetStringAsync($"/api/noise/findings/{id}", Ct);

        Assert.DoesNotContain("secret-vendor", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_Unknown_Finding_Is_A_Stated_404()
    {
        using var client = fx.Client();
        var response = await client.GetAsync("/api/noise/findings/nothing-here", Ct);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
