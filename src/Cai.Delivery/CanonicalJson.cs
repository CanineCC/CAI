using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cai.Delivery;

/// <summary>
/// Produces the canonical byte form of a delivery payload — the exact bytes that get signed and verified. Two machines
/// must derive identical bytes from the same payload, or a signature made on one would never verify on the other; that
/// determinism is the whole job.
///
/// The scheme targets RFC 8785 (JSON Canonicalization Scheme): object members are emitted in ascending key order, with
/// no insignificant whitespace, as UTF-8. Numbers are preserved as their serialized token (the payload is BUILT from
/// values already rounded to a fixed precision — see <see cref="DeliveryBuilder"/> — so there is no exponential or
/// precision ambiguity to canonicalize away). This reference canonicalizer is NORMATIVE for the closed loop: producer,
/// registry and consumer all run this one implementation, so they agree by construction. Full independent RFC 8785
/// string/number conformance (for a 3rd-party verifier written against the spec alone) is deferred with the 3rd-party
/// conformance regime.
/// </summary>
public static class CanonicalJson
{
    /// <summary>The canonicalization method name carried in <see cref="DeliverySignature.Canon"/>.</summary>
    public const string Method = "RFC8785-json";

    // Nulls are omitted so an absent optional field never enters the signed form; not indented (canonical = compact).
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>The canonical UTF-8 bytes of a payload — serialize it, then re-emit key-sorted and compact.</summary>
    public static byte[] Canonicalize(DeliveryPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Canonicalize(JsonSerializer.Serialize(payload, PayloadOptions));
    }

    /// <summary>The canonical UTF-8 bytes of an arbitrary JSON document: object keys ascending, no insignificant
    /// whitespace, number/string tokens preserved. Throws <see cref="JsonException"/> on malformed input.</summary>
    public static byte[] Canonicalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = true }))
        {
            Write(doc.RootElement, writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                // RFC 8785 orders members by the UTF-16 code units of the key. Our keys are ASCII, for which an ordinal
                // string comparison is identical to a code-unit comparison.
                foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    Write(prop.Value, writer);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(item, writer);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                // Preserve the number's serialized token verbatim (values were rounded at build time, so the token is a
                // short, unambiguous decimal).
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new JsonException($"Unexpected JSON token '{element.ValueKind}' in payload.");
        }
    }
}

/// <summary>Unpadded base64url (RFC 4648 §5) — the encoding for signature and key bytes on the wire.</summary>
public static class Base64Url
{
    /// <summary>Encode bytes as unpadded base64url.</summary>
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decode unpadded (or padded) base64url back to bytes. Throws <see cref="FormatException"/> on bad input.</summary>
    public static byte[] Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight((s.Length + 3) / 4 * 4, '='));
    }
}
