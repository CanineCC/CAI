namespace Cai.Web;

/// <summary>
/// The canonical VALID evidence bundle — the worked example the bundle format is learned from, served at
/// <c>GET /api/score/example</c> so it can be fetched and POSTed straight back to <c>/api/score</c>.
/// </summary>
/// <remarks>
/// It is a served artifact, and a tested one, because the examples that were only PRINTED were not valid input. The
/// calculator's "Load the sample bundle" gave every entry a <c>lens</c> key, which a deterministic dimension does not
/// have (it folds by CATEGORY, resolved from the named rubric's catalog), and put <c>AC1</c>/<c>P1</c> — META-dimensions,
/// which feed their lens directly — among them. Loading the sample and pressing Compute answered
/// <c>Value cannot be null. (Parameter 'wire')</c>: the one interactive demonstration of "don't trust the number,
/// reproduce it" failed on this project's own example, and every bundle a reader wrote from it was rejected too.
/// <c>CalculatorSampleTests</c> folds this string on every build, so it cannot rot back into an example that does not work.
/// </remarks>
public static class CalculatorSample
{
    /// <summary>The rubric the sample names. Must be a version whose catalog publishes the dimension→category map, so
    /// the sample also demonstrates that the category is derivable and need not be restated by hand.</summary>
    public const string RubricVersion = "rubric-2026.08.19";

    /// <summary>The sample bundle, exactly as it is loaded into the textarea.</summary>
    public const string Json =
        """
        {
          "rubricVersion": "rubric-2026.08.19",
          "dimensions": [
            { "id": "D1",  "score": 7.5, "confidence": 0.95 },
            { "id": "D3",  "score": 8.2, "confidence": 0.95 },
            { "id": "D8",  "score": 6.4, "confidence": 0.90 },
            { "id": "D5",  "score": 7.1, "confidence": 0.95 },
            { "id": "D7",  "score": 8.4, "confidence": 0.90 },
            { "id": "D15", "score": 6.0, "confidence": 0.85 },
            { "id": "D16", "score": 7.2, "confidence": 0.80 },
            { "id": "D30", "score": 7.6, "confidence": 0.90 },
            { "id": "D12", "score": 5.5, "confidence": 0.95 },
            { "id": "D13", "score": 8.9, "confidence": 0.95 }
          ],
          "metaDimensions": [
            { "id": "AC1", "lens": "accessibility", "score": 8.5 },
            { "id": "P1",  "lens": "productionReadiness", "score": 8.0 }
          ]
        }
        """;
}
