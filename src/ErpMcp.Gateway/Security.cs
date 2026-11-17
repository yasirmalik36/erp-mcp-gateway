using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ErpMcp.Gateway;

/// <summary>Roles are ordered: each role can do everything the roles below it can.</summary>
public enum Role { Viewer = 1, Accountant = 2, Controller = 3 }

/// <summary>The authenticated principal behind an MCP session (the human the agent acts for).</summary>
public sealed record Caller(string Id, Role Role)
{
    public bool HasAtLeast(Role required) => Role >= required;
}

public static class Masking
{
    /// <summary>AE070331234567890123456 → AE07 •••• •••• 3456</summary>
    public static string Iban(string iban) =>
        iban.Length <= 8 ? "••••" : $"{iban[..4]} •••• •••• {iban[^4..]}";

    /// <summary>finance@nova.example → f•••••e@nova.example</summary>
    public static string Email(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "•••";
        var local = email[..at];
        return $"{local[0]}{new string('•', Math.Max(local.Length - 2, 1))}{local[^1]}{email[at..]}";
    }
}

public sealed record AuditRecord(
    long Sequence,
    DateTimeOffset At,
    string CallerId,
    Role Role,
    string Tool,
    string Arguments,
    string Outcome,
    string PreviousHash,
    string Hash);

/// <summary>
/// Append-only, hash-chained audit log. Each record's hash covers its content plus the previous hash,
/// so editing or deleting any past record breaks verification from that point on.
/// </summary>
public sealed class AuditLog(TimeProvider clock)
{
    public const string Genesis = "GENESIS";
    private const int MaxArgumentChars = 500;
    private readonly List<AuditRecord> _records = [];
    private readonly Lock _gate = new();

    public AuditRecord Append(Caller caller, string tool, JsonObject arguments, string outcome)
    {
        var args = arguments.ToJsonString();
        if (args.Length > MaxArgumentChars) args = args[..MaxArgumentChars] + "…";
        lock (_gate)
        {
            var seq = _records.Count + 1;
            var prev = _records.Count == 0 ? Genesis : _records[^1].Hash;
            var at = clock.GetUtcNow();
            var hash = ComputeHash(seq, at, caller.Id, caller.Role, tool, args, outcome, prev);
            var record = new AuditRecord(seq, at, caller.Id, caller.Role, tool, args, outcome, prev, hash);
            _records.Add(record);
            return record;
        }
    }

    public IReadOnlyList<AuditRecord> Snapshot()
    {
        lock (_gate) return [.. _records];
    }

    /// <summary>Returns the first broken sequence number, or null if the chain is intact.</summary>
    public static long? FindTampering(IReadOnlyList<AuditRecord> records)
    {
        var prev = Genesis;
        foreach (var r in records)
        {
            var expected = ComputeHash(r.Sequence, r.At, r.CallerId, r.Role, r.Tool, r.Arguments, r.Outcome, prev);
            if (r.PreviousHash != prev || r.Hash != expected) return r.Sequence;
            prev = r.Hash;
        }
        return null;
    }

    private static string ComputeHash(long seq, DateTimeOffset at, string caller, Role role, string tool, string args, string outcome, string prev)
    {
        var payload = JsonSerializer.Serialize(new object[] { seq, at.ToUnixTimeMilliseconds(), caller, role.ToString(), tool, args, outcome, prev });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}

/// <summary>Fixed-window limiter per caller, so a looping agent can't hammer the ERP.</summary>
public sealed class RateLimiter(TimeProvider clock, int maxCallsPerMinute)
{
    private readonly ConcurrentDictionary<string, (long Window, int Count)> _windows = new();

    public bool TryAcquire(string callerId)
    {
        var window = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
        var state = _windows.AddOrUpdate(callerId,
            _ => (window, 1),
            (_, s) => s.Window == window ? (window, s.Count + 1) : (window, 1));
        return state.Count <= maxCallsPerMinute;
    }
}
