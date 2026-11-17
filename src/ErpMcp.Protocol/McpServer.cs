using System.Text.Json;
using System.Text.Json.Nodes;

namespace ErpMcp.Protocol;

/// <summary>A tool as advertised in <c>tools/list</c>.</summary>
public sealed record ToolDefinition(string Name, string Title, string Description, JsonObject InputSchema, bool ReadOnly);

/// <summary>Result of a tool call. Tool failures are results with <c>IsError</c>, not protocol errors (per the MCP spec).</summary>
public sealed record ToolCallResult(string Text, JsonNode? Structured = null, bool IsError = false)
{
    public static ToolCallResult Error(string message) => new(message, null, true);
}

public interface IMcpToolProvider<in TContext>
{
    IEnumerable<ToolDefinition> ListTools(TContext context);
    ToolCallResult CallTool(string name, JsonObject arguments, TContext context);
}

/// <summary>
/// Minimal, dependency-free Model Context Protocol server (JSON-RPC 2.0, protocol 2025-06-18).
/// Transport-agnostic: hand it one parsed message, get back the response (or null for notifications).
/// </summary>
public sealed class McpServer<TContext>(IMcpToolProvider<TContext> tools, string name, string serverVersion, string instructions)
{
    public const string ProtocolVersion = "2025-06-18";
    private static readonly string[] SupportedVersions = [ProtocolVersion, "2025-03-26", "2024-11-05"];

    public JsonObject? Handle(JsonNode? message, TContext context)
    {
        if (message is not JsonObject msg || msg["jsonrpc"]?.GetValue<string>() != "2.0")
            return Error(null, JsonRpcErrors.InvalidRequest, "Expected a JSON-RPC 2.0 object.");

        var id = msg["id"]?.DeepClone();
        var method = msg["method"] is JsonValue m && m.TryGetValue<string>(out var s) ? s : null;

        // A response or a notification needs no reply.
        if (method is null) return id is null ? null : Error(id, JsonRpcErrors.InvalidRequest, "Missing method.");
        if (id is null) return null;

        var @params = msg["params"] as JsonObject ?? new JsonObject();
        try
        {
            return method switch
            {
                "initialize" => Ok(id, Initialize(@params)),
                "ping" => Ok(id, new JsonObject()),
                "tools/list" => Ok(id, ListTools(context)),
                "tools/call" => CallTool(id, @params, context),
                _ => Error(id, JsonRpcErrors.MethodNotFound, $"Method '{method}' is not supported."),
            };
        }
        catch (JsonException ex)
        {
            return Error(id, JsonRpcErrors.InvalidParams, ex.Message);
        }
    }

    /// <summary>Parses raw text first; a parse failure becomes a JSON-RPC parse error.</summary>
    public JsonObject? HandleRaw(string raw, TContext context)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(raw); }
        catch (JsonException) { return Error(null, JsonRpcErrors.ParseError, "Invalid JSON."); }
        return Handle(node, context);
    }

    private JsonObject Initialize(JsonObject p)
    {
        var requested = p["protocolVersion"]?.GetValue<string>();
        var version = requested is not null && SupportedVersions.Contains(requested) ? requested : ProtocolVersion;
        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = name, ["version"] = serverVersion },
            ["instructions"] = instructions,
        };
    }

    private JsonObject ListTools(TContext context)
    {
        var list = new JsonArray();
        foreach (var t in tools.ListTools(context))
        {
            list.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["title"] = t.Title,
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema.DeepClone(),
                ["annotations"] = new JsonObject
                {
                    ["readOnlyHint"] = t.ReadOnly,
                    ["destructiveHint"] = false,
                    ["openWorldHint"] = false,
                },
            });
        }
        return new JsonObject { ["tools"] = list };
    }

    private JsonObject CallTool(JsonNode id, JsonObject p, TContext context)
    {
        if (p["name"] is not JsonValue n || !n.TryGetValue<string>(out var toolName))
            return Error(id, JsonRpcErrors.InvalidParams, "tools/call requires a 'name'.");
        var args = p["arguments"] as JsonObject ?? new JsonObject();

        var result = tools.CallTool(toolName, args, context);
        var body = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Text }),
            ["isError"] = result.IsError,
        };
        if (result.Structured is not null) body["structuredContent"] = result.Structured.DeepClone();
        return Ok(id, body);
    }

    private static JsonObject Ok(JsonNode id, JsonNode result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };

    public static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}

public static class JsonRpcErrors
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
}
