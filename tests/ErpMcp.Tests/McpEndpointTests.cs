using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ErpMcp.Tests;

public sealed class McpEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ValidDemoToken = "demo-viewer-token";
    private readonly HttpClient _client;

    public McpEndpointTests(WebApplicationFactory<Program> factory)
    {
        var demoFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Demo");
        });

        _client = demoFactory.CreateClient();
    }

    [Fact]
    public async Task PostMcp_WithoutBearerToken_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = CreateJsonContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostMcp_WithInvalidBearerToken_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = CreateJsonContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostMcp_WithDisallowedOrigin_Returns403()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = CreateJsonContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ValidDemoToken);
        request.Headers.Add("Origin", "https://evil.example.com");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostMcp_WithBodyOver64Kb_Returns413()
    {
        var oversizedPayload = new string('x', 64_001);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = CreateJsonContent(oversizedPayload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ValidDemoToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PostMcp_WithValidDemoTokenAndInitializeRequest_Returns200WithJsonRpcResult()
    {
        var initializePayload = """
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {
                        "name": "integration-test",
                        "version": "1.0.0"
                    }
                }
            }
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = CreateJsonContent(initializePayload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ValidDemoToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(responseBody)?.AsObject();

        Assert.NotNull(json);
        Assert.Equal("2.0", json["jsonrpc"]?.GetValue<string>());
        Assert.Equal(1, json["id"]?.GetValue<int>());
        Assert.NotNull(json["result"]);
        Assert.Null(json["error"]);
    }

    private static StringContent CreateJsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");
}