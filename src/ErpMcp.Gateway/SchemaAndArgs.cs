using System.Text.Json.Nodes;
using ErpMcp.Domain;

namespace ErpMcp.Gateway;

/// <summary>Small JSON Schema builders for tool input schemas.</summary>
internal static class Schema
{
    public static JsonObject Object(params (string Name, JsonObject Schema, bool Required)[] props)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema, isRequired) in props)
        {
            properties[name] = schema;
            if (isRequired) required.Add(name);
        }
        var obj = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
        if (required.Count > 0) obj["required"] = required;
        return obj;
    }

    public static JsonObject Str(string description, int maxLength = 64) =>
        new() { ["type"] = "string", ["description"] = description, ["maxLength"] = maxLength };

    public static JsonObject Int(string description, int min, int max) =>
        new() { ["type"] = "integer", ["description"] = description, ["minimum"] = min, ["maximum"] = max };

    public static JsonObject Enum(string description, params string[] values) =>
        new() { ["type"] = "string", ["description"] = description, ["enum"] = new JsonArray([.. values.Select(v => (JsonNode)v)]) };

    public static JsonObject Lines() => new()
    {
        ["type"] = "array",
        ["minItems"] = 2,
        ["maxItems"] = 20,
        ["description"] = "Journal lines; total debits must equal total credits.",
        ["items"] = Object(
            ("account", Str("Account code, e.g. 6100", 10), true),
            ("debit", new JsonObject { ["type"] = "number", ["minimum"] = 0 }, false),
            ("credit", new JsonObject { ["type"] = "number", ["minimum"] = 0 }, false)),
    };
}

/// <summary>Defensive argument readers. Agents send anything; validate everything.</summary>
internal static class Arg
{
    public static string Str(JsonObject args, string name, int maxLength = 64) =>
        OptStr(args, name, maxLength) ?? throw new ArgumentException($"'{name}' is required.");

    public static string? OptStr(JsonObject args, string name, int maxLength = 64)
    {
        if (args[name] is null) return null;
        if (args[name] is not JsonValue v || !v.TryGetValue<string>(out var s))
            throw new ArgumentException($"'{name}' must be a string.");
        s = s.Trim();
        if (s.Length == 0) return null;
        if (s.Length > maxLength) throw new ArgumentException($"'{name}' must be at most {maxLength} characters.");
        return s;
    }

    public static int? OptInt(JsonObject args, string name, int min, int max)
    {
        if (args[name] is null) return null;
        if (args[name] is not JsonValue v || v.GetValueKind() != System.Text.Json.JsonValueKind.Number ||
            !int.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
            throw new ArgumentException($"'{name}' must be an integer.");
        if (n < min || n > max) throw new ArgumentException($"'{name}' must be between {min} and {max}.");
        return n;
    }

    public static IReadOnlyList<JournalLine> Lines(JsonObject args)
    {
        if (args["lines"] is not JsonArray arr) throw new ArgumentException("'lines' must be an array.");
        if (arr.Count is < 2 or > 20) throw new ArgumentException("'lines' must have 2 to 20 items.");
        return [.. arr.Select(n =>
        {
            if (n is not JsonObject o) throw new ArgumentException("Each line must be an object.");
            return new JournalLine(Str(o, "account", 10), Money(o, "debit"), Money(o, "credit"));
        })];
    }

    private static decimal Money(JsonObject o, string name)
    {
        if (o[name] is null) return 0m;
        // Check the JSON kind, then parse the raw number text: works whether the value came from the wire or from code.
        if (o[name] is not JsonValue v || v.GetValueKind() != System.Text.Json.JsonValueKind.Number ||
            !decimal.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
            throw new ArgumentException($"'{name}' must be a number.");
        if (d is < 0 or > 10_000_000m) throw new ArgumentException($"'{name}' must be between 0 and 10,000,000.");
        return Math.Round(d, 2);
    }
}
