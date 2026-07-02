using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace Cai.Web.Registry;

/// <summary>
/// Validates an inbound package against the VERSIONED CAI-delivery JSON Schema
/// (<c>schemas/cai-delivery-1.0.schema.json</c>, embedded in the binary at build time). This is the registry's first
/// ingest gate: a package that is not even shaped like a delivery is rejected 400 before any cryptography runs.
/// Format assertions (RFC 3339 <c>date-time</c> etc.) are enforced, not annotation-only.
/// </summary>
public static class DeliveryPackageSchema
{
    private const string Resource = "Cai.Web.Registry.cai-delivery-1.0.schema.json";

    private static readonly JsonSchema Schema = Load();

    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    private static JsonSchema Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"embedded schema resource '{Resource}' missing from build");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }

    /// <summary>Validate a package document. Returns the schema violations (empty = valid), each as
    /// "<c>instanceLocation: error</c>" so a producer can find the offending field.</summary>
    public static IReadOnlyList<string> Validate(JsonElement package)
    {
        var results = Schema.Evaluate(package, Options);
        if (results.IsValid)
        {
            return [];
        }

        return (results.Details ?? []).Prepend(results)
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{Location(d.InstanceLocation.ToString())}: {e.Value}"))
            .Distinct()
            .ToList();
    }

    private static string Location(string pointer) => pointer.Length == 0 ? "(root)" : pointer;
}
