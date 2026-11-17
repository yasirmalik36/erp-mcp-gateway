using System.Security.Cryptography;
using System.Text;
using ErpMcp.Gateway;
using Microsoft.Extensions.Primitives;

namespace ErpMcp.Server;

public sealed class GatewaySettings
{
    /// <summary>Public demo: console needs no key, seeds a pending draft, enables the playground.</summary>
    public bool DemoMode { get; set; }
    public int CallsPerMinute { get; set; } = 60;
    public string ConsoleUser { get; set; } = "controller.human";
    public string? ConsoleKey { get; set; }

    /// <summary>Bearer token → "userId:Role". Set real tokens through environment variables, never in git.</summary>
    public Dictionary<string, string> Tokens { get; set; } = [];

    public string[] AllowedOrigins { get; set; } = [];

    public Caller? Authenticate(StringValues authorization)
    {
        var header = authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[7..].Trim();
        foreach (var (known, identity) in Tokens)
        {
            if (!FixedTimeEquals(known, token)) continue;
            var parts = identity.Split(':', 2);
            return parts.Length == 2 && Enum.TryParse<Role>(parts[1], true, out var role) ? new Caller(parts[0], role) : null;
        }
        return null;
    }

    /// <summary>MCP spec: validate Origin on HTTP to block DNS-rebinding attacks. Non-browser clients send none.</summary>
    public bool IsOriginAllowed(StringValues origin) =>
        StringValues.IsNullOrEmpty(origin) || AllowedOrigins.Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase);

    public bool IsConsoleAuthorized(StringValues key) =>
        DemoMode || (!string.IsNullOrEmpty(ConsoleKey) && FixedTimeEquals(ConsoleKey, key.ToString()));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
