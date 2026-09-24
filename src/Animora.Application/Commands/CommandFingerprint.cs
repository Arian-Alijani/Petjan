using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Animora.Application.Commands;

/// <summary>
/// Computes the <c>CommandFingerprint</c> (A-RULE 019): a SHA-256 hex over
/// <c>CommandType + separator + canonical payload JSON</c>. The canonical form is stable across processes and
/// platforms — object keys sorted ordinally, no indentation, invariant-culture numbers — so a byte-identical
/// retry always hashes equal and any real payload change hashes differently.
/// </summary>
public static class CommandFingerprint
{
    /// <summary>Field separator inside the hashed input (unit separator, never present in a type name).</summary>
    public const char Separator = '\u001f';

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>Hashes <paramref name="commandType"/> + <paramref name="payloadJson"/> into lowercase hex.</summary>
    public static string Compute(string commandType, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        ArgumentNullException.ThrowIfNull(payloadJson);
        var bytes = Encoding.UTF8.GetBytes(commandType + Separator + payloadJson);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Hashes a command type plus a payload serialized into the canonical form.</summary>
    public static string Compute<T>(string commandType, T payload) =>
        Compute(commandType, CanonicalJson(payload));

    /// <summary>
    /// Serializes a payload into the canonical JSON form used by the fingerprint and the stored envelope:
    /// object keys sorted ordinally and recursively, no indentation, invariant-culture numbers.
    /// </summary>
    public static string CanonicalJson<T>(T payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var node = JsonSerializer.SerializeToNode(payload, SerializeOptions);
        var builder = new StringBuilder();
        Write(node, builder);
        return builder.ToString();
    }

    private static void Write(JsonNode? node, StringBuilder builder)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject obj:
                builder.Append('{');
                var first = true;
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!first) builder.Append(',');
                    first = false;
                    builder.Append(JsonSerializer.Serialize(property.Key));
                    builder.Append(':');
                    Write(property.Value, builder);
                }
                builder.Append('}');
                break;
            case JsonArray array:
                builder.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i != 0) builder.Append(',');
                    Write(array[i], builder);
                }
                builder.Append(']');
                break;
            default:
                builder.Append(node.ToJsonString());
                break;
        }
    }
}
