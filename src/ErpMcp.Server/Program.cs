using System.Text;
using System.Text.Json.Nodes;
using ErpMcp.Domain;
using ErpMcp.Gateway;
using ErpMcp.Protocol;
using ErpMcp.Server;

// ── stdio transport: for Claude Desktop, Cursor, VS Code, MCP Inspector ───────────────
if (args.Contains("--stdio"))
{
    var role = Enum.TryParse<Role>(Environment.GetEnvironmentVariable("ERP_MCP_ROLE"), true, out var r) ? r : Role.Viewer;
    var user = Environment.GetEnvironmentVariable("ERP_MCP_USER") ?? $"{role.ToString().ToLowerInvariant()}.local";
    var host = new GatewayHost(TimeProvider.System, callsPerMinute: 120, seedDemoDraft: false);
    await Console.Error.WriteLineAsync($"[{GatewayHost.ServerName}] stdio ready as {user} ({role})");
    await StdioTransport.RunAsync(host.Mcp, new Caller(user, role), Console.OpenStandardInput(), Console.OpenStandardOutput());
    return;
}

// ── HTTP transport + Controller Console ──────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Gateway").Get<GatewaySettings>() ?? new GatewaySettings();
var gateway = new GatewayHost(TimeProvider.System, settings.CallsPerMinute, settings.DemoMode);
builder.Services.AddSingleton(gateway);
builder.Services.AddSingleton(settings);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok", server = GatewayHost.ServerName, demo = settings.DemoMode }));

// Streamable HTTP endpoint (JSON responses). Bearer token identifies the human the agent acts for.
app.MapPost("/mcp", async (HttpRequest req, GatewaySettings cfg, GatewayHost gw) =>
{
    if (!cfg.IsOriginAllowed(req.Headers.Origin))
        return Results.Json(McpServer<Caller>.Error(null, JsonRpcErrors.InvalidRequest, "Origin not allowed."), statusCode: 403);
    if (cfg.Authenticate(req.Headers.Authorization) is not { } caller)
        return Results.Json(McpServer<Caller>.Error(null, JsonRpcErrors.InvalidRequest, "Missing or invalid bearer token."), statusCode: 401);

    using var reader = new StreamReader(req.Body, Encoding.UTF8);
    var body = await reader.ReadToEndAsync();
    if (body.Length > 64_000)
        return Results.Json(McpServer<Caller>.Error(null, JsonRpcErrors.InvalidRequest, "Request too large."), statusCode: 413);

    var response = gw.Mcp.HandleRaw(body, caller);
    return response is null ? Results.Accepted() : Results.Text(response.ToJsonString(), "application/json");
});

app.MapGet("/mcp", () => Results.StatusCode(405)); // no server-initiated stream

// ── Console API (human side). In demo mode the console acts as "controller.human". ──
var api = app.MapGroup("/api").AddEndpointFilter(async (ctx, next) =>
{
    var cfg = ctx.HttpContext.RequestServices.GetRequiredService<GatewaySettings>();
    return cfg.IsConsoleAuthorized(ctx.HttpContext.Request.Headers["X-Console-Key"])
        ? await next(ctx)
        : Results.Unauthorized();
});

api.MapGet("/summary", (GatewayHost gw) =>
{
    var audit = gw.Audit.Snapshot();
    return Results.Text(new JsonObject
    {
        ["aging"] = gw.Tools.AgingJson(),
        ["pendingApprovals"] = gw.Ledger.Journal.Count(j => j.Status == JournalStatus.PendingApproval),
        ["toolCalls"] = audit.Count,
        ["denied"] = audit.Count(a => a.Outcome != "ok"),
        ["chainVerified"] = AuditLog.FindTampering(audit) is null,
        ["callsByRole"] = new JsonObject(audit.GroupBy(a => a.Role.ToString())
            .Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Count()))),
    }.ToJsonString(), "application/json");
});

api.MapGet("/approvals", (GatewayHost gw) =>
    Results.Text(new JsonArray([.. gw.Ledger.Journal.Reverse().Select(ErpToolProvider.JournalJson)]).ToJsonString(), "application/json"));

api.MapPost("/approvals/{id}/{decision}", (string id, string decision, GatewayHost gw, GatewaySettings cfg) =>
{
    if (decision is not ("approve" or "reject")) return Results.BadRequest(new { error = "Decision must be approve or reject." });
    try
    {
        var entry = gw.Ledger.Decide(id, cfg.ConsoleUser, decision == "approve");
        gw.Audit.Append(new Caller(cfg.ConsoleUser, Role.Controller), $"console:{decision}", new JsonObject { ["entryId"] = entry.Id }, "ok");
        return Results.Text(ErpToolProvider.JournalJson(entry).ToJsonString(), "application/json");
    }
    catch (DomainException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

api.MapGet("/audit", (GatewayHost gw, int? limit) =>
{
    var records = gw.Audit.Snapshot();
    return Results.Ok(new
    {
        chainVerified = AuditLog.FindTampering(records) is null,
        total = records.Count,
        records = records.TakeLast(Math.Clamp(limit ?? 50, 1, 200)).Reverse(),
    });
});

// Playground: lets visitors without an MCP client call tools as a demo role (demo mode only).
api.MapPost("/playground", async (HttpRequest req, GatewayHost gw, GatewaySettings cfg) =>
{
    if (!cfg.DemoMode) return Results.NotFound();
    var body = await JsonNode.ParseAsync(req.Body) as JsonObject ?? new JsonObject();
    if (!Enum.TryParse<Role>(body["role"]?.GetValue<string>(), true, out var role))
        return Results.BadRequest(new { error = "role must be Viewer, Accountant or Controller." });
    var caller = new Caller($"{role.ToString().ToLowerInvariant()}.agent", role);
    var message = new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 1,
        ["method"] = body["method"]?.GetValue<string>() ?? "tools/list",
        ["params"] = body["params"]?.DeepClone() ?? new JsonObject(),
    };
    return Results.Text(gw.Mcp.Handle(message, caller)!.ToJsonString(), "application/json");
});

app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
