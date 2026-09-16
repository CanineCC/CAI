using Cai.Delivery;
using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// A published catalog must survive parse → serialize unchanged.
///
/// <para>This is not tidiness. The catalog's CONTENT DIGEST is what binds a delivery to the rules it was folded
/// under (<see cref="DeliveryPayload.RubricContentHash"/>), and a model that adds or drops a field on the way
/// through produces a digest describing a document nobody published — so a holder of the artifact re-digesting the
/// real catalog gets a different answer and a sound verification fails.</para>
/// </summary>
public sealed class CatalogRoundTripTests
{
    /// <summary>A published catalog, in the shape the archive actually serves: every dimension carries id, name, lens,
    /// category, evaluator, whatItMeasures, family, ceilingRung, scoringPolarity — and NO deepScan, which no publisher
    /// has ever emitted.</summary>
    private const string Published = """
        {
          "rubricVersion": "rubric-2026.09.12",
          "lenses": [
            {
              "key": "codeHealth",
              "label": "Code Health"
            }
          ],
          "dimensions": [
            {
              "id": "D1",
              "name": "Cyclomatic Complexity",
              "lens": "codeHealth",
              "category": "code-quality",
              "evaluator": "tool",
              "whatItMeasures": "How tangled the control flow is.",
              "family": "dimension",
              "ceilingRung": "Prevented",
              "scoringPolarity": "deduction"
            }
          ]
        }
        """;

    [Fact]
    public void A_published_catalog_round_trips_without_gaining_or_losing_a_field()
    {
        var reserialized = RubricCatalog.Parse(Published).ToJson();

        Assert.Equal(Published.ReplaceLineEndings("\n").TrimEnd(), reserialized.ReplaceLineEndings("\n").TrimEnd());
    }

    [Fact]
    public void Resolving_a_catalog_from_its_document_and_from_its_parsed_form_agree_on_the_digest()
    {
        // The consequence that actually bites: FromPublished digests the DOCUMENT, FromCatalog digests the parsed
        // model. If the model does not round-trip, these disagree — and a verifier handed the parsed form would
        // refuse an artifact that is perfectly sound.
        var fromDocument = ResolvedRubric.FromPublished(Published);
        var fromParsed = ResolvedRubric.FromCatalog(RubricCatalog.Parse(Published));

        Assert.Equal(fromDocument.ContentHash, fromParsed.ContentHash);
    }

    [Fact]
    public void A_catalog_that_does_say_deepScan_keeps_saying_it()
    {
        // Making the field absent-able must not make it unsayable: a catalog that DOES declare deepScan round-trips
        // that declaration, both true and false.
        foreach (var value in new[] { "true", "false" })
        {
            var withField = Published.Replace(
                "\"scoringPolarity\": \"deduction\"",
                $"\"scoringPolarity\": \"deduction\",\n      \"deepScan\": {value}",
                StringComparison.Ordinal);

            var parsed = RubricCatalog.Parse(withField);
            Assert.Equal(value == "true", parsed.Dimensions[0].DeepScan);
            Assert.NotNull(parsed.Dimensions[0].DeepScan);
            Assert.Contains($"\"deepScan\": {value}", parsed.ToJson(), StringComparison.Ordinal);
        }
    }
}
