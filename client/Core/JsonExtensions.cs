using System.Text.Json;
using System.Text.Json.Nodes;

namespace GitHubDemo.Core;

// Tolerant readers for server messages: a missing or wrongly typed field gives the default.
public static class JsonExtensions
{
    public static string Str(this JsonNode? node, string key, string fallback = "")
    {
        var value = node?[key];
        return value is JsonValue v && v.TryGetValue(out string? s) ? s ?? fallback : fallback;
    }

    public static long Long(this JsonNode? node, string key, long fallback = 0)
    {
        var value = node?[key];
        if (value is JsonValue v)
        {
            if (v.TryGetValue(out long l)) return l;
            if (v.TryGetValue(out int i)) return i;
            if (v.TryGetValue(out double d)) return (long)d;
        }
        return fallback;
    }

    public static int Int(this JsonNode? node, string key, int fallback = 0) => (int)node.Long(key, fallback);

    public static bool Bool(this JsonNode? node, string key, bool fallback = false)
    {
        var value = node?[key];
        return value is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
    }

    public static JsonObject? Obj(this JsonNode? node, string key) => node?[key] as JsonObject;

    public static IEnumerable<JsonObject> Objects(this JsonNode? node, string key) =>
        (node?[key] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();

    public static IEnumerable<string> Strings(this JsonNode? node, string key) =>
        (node?[key] as JsonArray)?.Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s : null)
            .Where(s => s != null).Select(s => s!) ?? Enumerable.Empty<string>();

    public static DateTime Time(this JsonNode? node, string key) =>
        DateTimeOffset.FromUnixTimeMilliseconds(node.Long(key)).LocalDateTime;

    public static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
}
