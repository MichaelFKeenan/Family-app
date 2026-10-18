using System.Net;
using ModelContextProtocol.Protocol;

namespace FamilyApp.Mcp.Tests;

[Collection(McpServerCollection.Name)]
public class ShoppingToolsTests
{
    private const string Milk = """
        {"id":"0199e000-0000-7000-8000-000000000201","name":"Milk","quantity":"2","note":null,
         "addedBy":"0199e000-0000-7000-8000-000000000001","addedAt":"2026-10-18T09:00:00Z",
         "checkedBy":null,"checkedAt":null,"checkReason":null}
        """;

    private readonly McpTestServer _server;

    public ShoppingToolsTests(McpServerFixture fixture)
    {
        _server = fixture.Server;
        _server.Api.Reset();
    }

    [Fact]
    public async Task The_server_offers_every_tool_with_the_right_hints()
    {
        var tools = (await _server.Client.ListToolsAsync()).ToDictionary(t => t.Name);

        Assert.Equal(
            new[]
            {
                "add_shopping_item", "assign_task", "complete_task", "create_task", "edit_task", "list_categories",
                "list_members", "list_shopping", "list_tasks", "mark_shopping_not_needed", "reopen_task", "start_task",
                "tick_shopping_item",
            },
            tools.Keys.Order());
        Assert.True(tools["list_shopping"].ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools["list_tasks"].ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools["tick_shopping_item"].ProtocolTool.Annotations?.IdempotentHint);
        Assert.NotEqual(true, tools["complete_task"].ProtocolTool.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public async Task List_shopping_returns_what_the_api_returns()
    {
        _server.Api.Answer("GET /api/shopping", $"[{Milk}]");

        var result = await _server.Client.CallToolAsync("list_shopping");

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("Milk", Text(result));
        var request = Assert.Single(_server.Api.Requests);
        Assert.Equal("GET /api/shopping", request.Route);
        Assert.Equal("Bearer fb_test", request.Authorization);
    }

    [Fact]
    public async Task Add_shopping_item_sends_the_name_and_quantity()
    {
        _server.Api.Answer("POST /api/shopping", $$"""{"item":{{Milk}},"alreadyOnList":false}""", HttpStatusCode.Created);

        var result = await _server.Client.CallToolAsync("add_shopping_item",
            new Dictionary<string, object?> { ["name"] = "Milk", ["quantity"] = "2" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("\"alreadyOnList\":false", Text(result));
        var request = Assert.Single(_server.Api.Requests);
        Assert.Equal("POST /api/shopping", request.Route);
        Assert.Contains("\"name\":\"Milk\"", request.Body);
        Assert.Contains("\"quantity\":\"2\"", request.Body);
    }

    [Theory]
    [InlineData("tick_shopping_item", "check")]
    [InlineData("mark_shopping_not_needed", "not-needed")]
    public async Task Taking_an_item_off_the_list_calls_its_command(string tool, string command)
    {
        const string id = "0199e000-0000-7000-8000-000000000201";
        _server.Api.Answer($"POST /api/shopping/{id}/{command}", Milk);

        var result = await _server.Client.CallToolAsync(tool, new Dictionary<string, object?> { ["id"] = id });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal($"POST /api/shopping/{id}/{command}", Assert.Single(_server.Api.Requests).Route);
    }

    [Fact]
    public async Task A_rejected_token_tells_the_agent_how_to_fix_it()
    {
        _server.Api.Answer("GET /api/shopping", "", HttpStatusCode.Unauthorized);

        var result = await _server.Client.CallToolAsync("list_shopping");

        Assert.True(result.IsError);
        Assert.Contains("Agent tokens", Text(result));
    }

    internal static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
