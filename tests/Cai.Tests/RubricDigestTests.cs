using System.Text.Json;
using Cai.Delivery;
using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The rubric content-binding contract. The catalog store's attestation proves a catalog is filed under the name it
/// declares; it cannot prove the content behind that name never changed. These tests pin the digest that closes the
/// gap — stable under reformatting, sensitive to any semantic edit, and specifically able to catch the edit the
/// attestation check passes.
/// </summary>
public sealed class RubricDigestTests
{
    private const string Catalog = """
        {
          "rubricVersion": "rubric-2026.08.18",
          "lenses": [ { "key": "code-health", "label": "Code Health" } ],
          "dimensions": [
            { "id": "D1", "name": "Cyclomatic complexity", "lens": "code-health",
              "evaluator": "tool", "whatItMeasures": "branching per method", "family": "dimension" }
          ]
        }
        """;

    private static string WriteCatalog(string root, string version, string json)
    {
        var dir = Path.Combine(root, version);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "rubric-catalog.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Digest_Is_Stable_Under_Reformatting()
    {
        // The digest is taken over CANONICAL bytes, so whitespace, indentation and key order are not content.
        var reordered = """
            {"dimensions":[{"family":"dimension","evaluator":"tool","id":"D1","lens":"code-health","name":"Cyclomatic complexity","whatItMeasures":"branching per method"}],"lenses":[{"label":"Code Health","key":"code-health"}],"rubricVersion":"rubric-2026.08.18"}
            """;

        Assert.Equal(RubricDigest.Of(Catalog), RubricDigest.Of(reordered));
    }

    [Fact]
    public void Digest_Carries_Its_Algorithm_Label()
    {
        Assert.StartsWith("sha256:", RubricDigest.Of(Catalog), StringComparison.Ordinal);
        Assert.Equal("sha256", RubricDigest.Algorithm);
    }

    [Theory]
    // Each edit is a way a rubric could be quietly re-calibrated under its own name.
    [InlineData("\"branching per method\"", "\"branching per method (revised)\"")]   // a definition reworded
    [InlineData("\"evaluator\": \"tool\"", "\"evaluator\": \"llm\"")]                 // a dimension's authority changed
    [InlineData("\"D1\"", "\"D2\"")]                                                  // a dimension swapped
    public void Digest_Changes_On_Any_Semantic_Edit(string from, string to)
    {
        var edited = Catalog.Replace(from, to, StringComparison.Ordinal);
        Assert.True(edited != Catalog, "the test fixture must actually change");

        Assert.NotEqual(RubricDigest.Of(Catalog), RubricDigest.Of(edited));
    }

    [Fact]
    public void Digest_Detects_The_Edit_That_Attestation_Passes()
    {
        // THE POINT OF THIS TYPE. Rewrite a published catalog's CONTENT while keeping its declared rubricVersion and
        // its directory identical. The store still serves it — attestation only compares the declared version to the
        // directory name — so the naming check cannot see this. The digest can.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = WriteCatalog(root, "rubric-2026.08.18", Catalog);
            var store = new RubricCatalogStore(root);

            var before = RubricDigest.Of(store.RawCatalogJson("rubric-2026.08.18")!);

            // The quiet recalibration: same name, same directory, different rule.
            File.WriteAllText(path, Catalog.Replace("\"evaluator\": \"tool\"", "\"evaluator\": \"llm\"", StringComparison.Ordinal));
            var after = RubricDigest.Of(File.ReadAllText(path));

            // Attestation is unmoved — which is exactly the gap.
            Assert.NotNull(new RubricCatalogStore(root).Get("rubric-2026.08.18"));

            Assert.NotEqual(before, after);
            Assert.False(RubricDigest.Matches(File.ReadAllText(path), before));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RawCatalogJson_Withholds_An_Unattested_Catalog()
    {
        // A catalog that declares a different version than its directory is unattestable, so it has no publishable
        // digest either — the two controls agree rather than one covering for the other.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            WriteCatalog(root, "rubric-2026.08.18", Catalog.Replace("rubric-2026.08.18", "rubric-2026.08.17", StringComparison.Ordinal));
            Assert.Null(new RubricCatalogStore(root).RawCatalogJson("rubric-2026.08.18"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Matches_Degrades_To_False_Without_An_Expectation()
    {
        // A holder of a pre-digest artifact can't check; that must read as "cannot verify", never as a silent pass.
        Assert.False(RubricDigest.Matches(Catalog, null));
        Assert.False(RubricDigest.Matches(Catalog, ""));
        Assert.True(RubricDigest.Matches(Catalog, RubricDigest.Of(Catalog)));
    }

    [Fact]
    public void Malformed_Catalog_Has_No_Digest()
    {
        Assert.ThrowsAny<JsonException>(() => RubricDigest.Of("{ not json"));
    }
}
