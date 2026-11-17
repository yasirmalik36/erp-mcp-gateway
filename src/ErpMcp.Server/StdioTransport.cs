using System.Text;
using ErpMcp.Gateway;
using ErpMcp.Protocol;

namespace ErpMcp.Server;

/// <summary>MCP stdio transport: one JSON-RPC message per line in, one per line out. Logs go to stderr only.</summary>
public static class StdioTransport
{
    public static async Task RunAsync(McpServer<Caller> server, Caller caller, Stream input, Stream output, CancellationToken ct = default)
    {
        using var reader = new StreamReader(input, new UTF8Encoding(false));
        await using var writer = new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var response = server.HandleRaw(line, caller);
            if (response is not null) await writer.WriteLineAsync(response.ToJsonString());
        }
    }
}
