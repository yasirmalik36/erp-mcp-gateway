using System.Text.Json.Nodes;
using ErpMcp.Domain;
using ErpMcp.Gateway;
using ErpMcp.Protocol;
using ErpMcp.Server;

namespace ErpMcp.Tests;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public class GatewayTests
{
    private static readonly Caller Viewer = new("viewer.test", Role.Viewer);
    private static readonly Caller Accountant = new("accountant.test", Role.Accountant);
    private static readonly Caller Controller = new("controller.test", Role.Controller);

    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly GatewayHost _host;

    public GatewayTests() => _host = new GatewayHost(_clock, callsPerMinute: 1000, seedDemoDraft: false);

    private JsonObject Rpc(string method, JsonObject? @params, Caller caller, int id = 1) =>
        _host.Mcp.Handle(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = @params ?? new JsonObject(),
        }, caller)!;

    private JsonObject Call(string tool, JsonObject args, Caller caller) =>
        Rpc("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = args }, caller)["result"]!.AsObject();

    private static string[] ToolNames(JsonObject listResponse) =>
        [.. listResponse["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>())];

    [Fact]
    public void Initialize_negotiates_protocol_and_advertises_tools()
    {
        var res = Rpc("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" }, Viewer)["result"]!;
        Assert.Equal("2025-06-18", res["protocolVersion"]!.GetValue<string>());
        Assert.NotNull(res["capabilities"]!["tools"]);
        Assert.Equal("erp-mcp-gateway", res["serverInfo"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Notifications_get_no_response()
    {
        var res = _host.Mcp.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }, Viewer);
        Assert.Null(res);
    }

    [Fact]
    public void Invalid_json_returns_parse_error()
    {
        var res = _host.Mcp.HandleRaw("{not json", Viewer)!;
        Assert.Equal(JsonRpcErrors.ParseError, res["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void Unknown_method_returns_method_not_found()
    {
        var res = Rpc("resources/list", null, Viewer);
        Assert.Equal(JsonRpcErrors.MethodNotFound, res["error"]!["code"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(Role.Viewer, 4)]
    [InlineData(Role.Accountant, 5)]
    [InlineData(Role.Controller, 7)]
    public void Tools_list_is_filtered_by_role(Role role, int expected)
    {
        var names = ToolNames(Rpc("tools/list", null, new Caller("u", role)));
        Assert.Equal(expected, names.Length);
    }

    [Fact]
    public void Hidden_tool_is_still_denied_when_called_directly()
    {
        var res = Call("draft_journal_entry", new JsonObject(), Viewer);
        Assert.True(res["isError"]!.GetValue<bool>());
        Assert.Contains("not allowed", res["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("forbidden", _host.Audit.Snapshot()[^1].Outcome);
    }

    [Fact]
    public void Viewer_sees_masked_email_and_iban()
    {
        var data = Call("get_customer", new JsonObject { ["customerId"] = "acme" }, Viewer)["structuredContent"]!["data"]!;
        Assert.Equal("AE07 •••• •••• 3456", data["iban"]!.GetValue<string>());
        Assert.Contains("•", data["email"]!.GetValue<string>());
    }

    [Fact]
    public void Controller_sees_full_iban()
    {
        var data = Call("get_customer", new JsonObject { ["customerId"] = "ACME" }, Controller)["structuredContent"]!["data"]!;
        Assert.Equal("AE070331234567890123456", data["iban"]!.GetValue<string>());
    }

    [Fact]
    public void Tool_output_is_wrapped_in_untrusted_data_envelope()
    {
        var result = Call("list_invoices", new JsonObject { ["minDaysOverdue"] = 90 }, Viewer)["structuredContent"]!;
        Assert.Equal(ErpToolProvider.UntrustedNotice, result["notice"]!.GetValue<string>());
        var ids = result["data"]!.AsArray().Select(i => i!["id"]!.GetValue<string>()).ToArray();
        Assert.Equal(["INV-1004", "INV-1007"], ids);
    }

    [Fact]
    public void Aging_buckets_add_up_to_total()
    {
        var aging = Call("get_ar_aging", new JsonObject(), Viewer)["structuredContent"]!["data"]!;
        var sum = aging["buckets"]!.AsArray().Sum(b => b!["amount"]!.GetValue<decimal>());
        Assert.Equal(aging["totalOutstanding"]!.GetValue<decimal>(), sum);
    }

    [Fact]
    public void Draft_is_pending_until_a_human_approves()
    {
        var draft = Call("draft_journal_entry", new JsonObject
        {
            ["description"] = "Write off INV-1007",
            ["lines"] = new JsonArray(
                new JsonObject { ["account"] = "6100", ["debit"] = 4250 },
                new JsonObject { ["account"] = "1100", ["credit"] = 4250 }),
        }, Accountant)["structuredContent"]!["data"]!;

        Assert.Equal("PendingApproval", draft["status"]!.GetValue<string>());
        var posted = _host.Ledger.Decide(draft["id"]!.GetValue<string>(), "controller.human", approve: true);
        Assert.Equal(JournalStatus.Posted, posted.Status);
    }

    [Fact]
    public void Unbalanced_entry_is_rejected()
    {
        var res = Call("draft_journal_entry", new JsonObject
        {
            ["description"] = "Bad",
            ["lines"] = new JsonArray(
                new JsonObject { ["account"] = "6100", ["debit"] = 10 },
                new JsonObject { ["account"] = "1100", ["credit"] = 9 }),
        }, Accountant);
        Assert.True(res["isError"]!.GetValue<bool>());
        Assert.Contains("not balanced", res["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Unknown_account_is_rejected()
    {
        var res = Call("draft_journal_entry", new JsonObject
        {
            ["description"] = "Bad account",
            ["lines"] = new JsonArray(
                new JsonObject { ["account"] = "9999", ["debit"] = 10 },
                new JsonObject { ["account"] = "1100", ["credit"] = 10 }),
        }, Accountant);
        Assert.True(res["isError"]!.GetValue<bool>());
    }

    [Fact]
    public void Drafter_cannot_approve_own_entry()
    {
        var entry = _host.Ledger.DraftJournalEntry("Fee",
            [new JournalLine("6200", 25m, 0m), new JournalLine("1000", 0m, 25m)], "alice");
        var ex = Assert.Throws<DomainException>(() => _host.Ledger.Decide(entry.Id, "ALICE", approve: true));
        Assert.Contains("Segregation of duties", ex.Message);
    }

    [Fact]
    public void Entry_cannot_be_decided_twice()
    {
        var entry = _host.Ledger.DraftJournalEntry("Fee",
            [new JournalLine("6200", 25m, 0m), new JournalLine("1000", 0m, 25m)], "alice");
        _host.Ledger.Decide(entry.Id, "bob", approve: false);
        Assert.Throws<DomainException>(() => _host.Ledger.Decide(entry.Id, "bob", approve: true));
    }

    [Fact]
    public void Oversized_or_wrongly_typed_arguments_are_rejected()
    {
        Assert.True(Call("get_customer", new JsonObject { ["customerId"] = new string('A', 500) }, Viewer)["isError"]!.GetValue<bool>());
        Assert.True(Call("list_invoices", new JsonObject { ["minDaysOverdue"] = "lots" }, Viewer)["isError"]!.GetValue<bool>());
    }

    [Fact]
    public void Rate_limit_resets_each_minute()
    {
        var limiter = new RateLimiter(_clock, maxCallsPerMinute: 2);
        Assert.True(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("a"));
        Assert.False(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("b"));
        _clock.Now = _clock.Now.AddMinutes(1);
        Assert.True(limiter.TryAcquire("a"));
    }

    [Fact]
    public void Audit_chain_verifies_and_detects_tampering()
    {
        Call("list_customers", new JsonObject(), Viewer);
        Call("get_ar_aging", new JsonObject(), Viewer);
        Call("list_invoices", new JsonObject(), Viewer);
        var records = _host.Audit.Snapshot();
        Assert.Null(AuditLog.FindTampering(records));

        var tampered = records.ToList();
        tampered[1] = tampered[1] with { Outcome = "forbidden" };
        Assert.Equal(2, AuditLog.FindTampering(tampered));
    }

    [Fact]
    public void Masking_helpers()
    {
        Assert.Equal("GB29 •••• •••• 6819", Masking.Iban("GB29NWBK60161331926819"));
        Assert.Equal("a••p@x.example", Masking.Email("abcp@x.example"));
    }
}
