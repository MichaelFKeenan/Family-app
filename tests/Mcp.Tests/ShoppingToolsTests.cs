using System.Net;
using ModelContextProtocol.Protocol;

namespace FamilyApp.Mcp.Tests;

public class ShoppingToolsTests
{
    [Fact]
    public async Task The_server_offers_list_shopping_as_a_read_only_tool()
    {
        await using var server = await McpTestServer.StartAsync();

        var tools = await server.Client.ListToolsAsync();

        var tool = Assert.Single(tools, t => t.Name == "list_shopping");
        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Contains("shopping list", tool.Description);
    }

    [Fact]
    public async Task List_shopping_returns_what_the_api_returns()
    {
        await using var server = await McpTestServer.StartAsync();
        server.Api.Answer("GET /api/shopping", """
            [{"id":"0199e000-0000-7000-8000-000000000201","name":"Milk","quantity":"2","note":null,
              "addedBy":"0199e000-0000-7000-8000-000000000001","addedAt":"2026-10-18T09:00:00Z",
              "checkedBy":null,"checkedAt":null,"checkReason":null}]
            """);

        var result = await server.Client.CallToolAsync("list_shopping");

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("Milk", Text(result));
        var request = Assert.Single(server.Api.Requests);
        Assert.Equal("GET /api/shopping", request.Route);
        Assert.Equal("Bearer fb_test", request.Authorization);
    }

    [Fact]
    public async Task A_rejected_token_tells_the_agent_how_to_fix_it()
    {
        await using var server = await McpTestServer.StartAsync();
        server.Api.Answer("GET /api/shopping", "", HttpStatusCode.Unauthorized);

        var result = await server.Client.CallToolAsync("list_shopping");

        Assert.True(result.IsError);
        Assert.Contains("Agent tokens", Text(result));
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
