using System.Net;
using System.Text.Json;

namespace FamilyApp.Mcp.Tests;

[Collection(McpServerCollection.Name)]
public class TaskToolsTests
{
    private const string Id = "0199e000-0000-7000-8000-000000000101";
    private const string Category = "0199e000-0000-7000-8000-0000000000c1";

    private static readonly string Bins = $$"""
        {"id":"{{Id}}","title":"Take the bins out","description":"Green bin too","status":"todo","priority":"high",
         "points":5,"categoryId":"{{Category}}","assigneeId":null,"dueOn":"2026-10-20","createdBy":"0199e000-0000-7000-8000-000000000001",
         "createdAt":"2026-10-18T09:00:00Z","updatedAt":"2026-10-18T09:00:00Z","completedAt":null,"completedBy":null,
         "version":3,"ownerId":null}
        """;

    private readonly McpTestServer _server;

    public TaskToolsTests(McpServerFixture fixture)
    {
        _server = fixture.Server;
        _server.Api.Reset();
    }

    [Fact]
    public async Task List_tasks_asks_for_the_board_and_status()
    {
        _server.Api.Answer("GET /api/tasks?board=personal&status=todo", $"[{Bins}]");

        var result = await _server.Client.CallToolAsync("list_tasks",
            new Dictionary<string, object?> { ["board"] = "personal", ["status"] = "todo" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("\"version\":3", ShoppingToolsTests.Text(result));
    }

    [Fact]
    public async Task Create_task_sends_the_fields_in_the_apis_shape()
    {
        _server.Api.Answer("POST /api/tasks", Bins, HttpStatusCode.Created);

        var result = await _server.Client.CallToolAsync("create_task", new Dictionary<string, object?>
        {
            ["title"] = "Take the bins out",
            ["priority"] = "high",
            ["points"] = 5,
            ["categoryId"] = Category,
            ["dueOn"] = "2026-10-20",
        });

        Assert.NotEqual(true, result.IsError);
        var body = Body(Assert.Single(_server.Api.Requests).Body);
        Assert.Equal("Take the bins out", body.GetProperty("title").GetString());
        Assert.Equal("high", body.GetProperty("priority").GetString());
        Assert.Equal(5, body.GetProperty("points").GetInt32());
        Assert.Equal(Category, body.GetProperty("categoryId").GetString());
        Assert.Equal("2026-10-20", body.GetProperty("dueOn").GetString());
    }

    [Fact]
    public async Task Edit_task_changes_only_what_was_sent_and_keeps_the_callers_version()
    {
        _server.Api.Answer($"GET /api/tasks/{Id}", Bins);
        _server.Api.Answer($"PUT /api/tasks/{Id}", Bins);

        var result = await _server.Client.CallToolAsync("edit_task", new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["version"] = 2,
            ["points"] = 10,
            ["dueOn"] = "",
        });

        Assert.NotEqual(true, result.IsError);
        var put = _server.Api.Requests.Single(r => r.Route.StartsWith("PUT"));
        var body = Body(put.Body);
        Assert.Equal(10, body.GetProperty("points").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("dueOn").ValueKind);         // cleared on request
        Assert.Equal("Take the bins out", body.GetProperty("title").GetString());       // kept
        Assert.Equal("Green bin too", body.GetProperty("description").GetString());    // kept
        Assert.Equal(Category, body.GetProperty("categoryId").GetString());            // kept
        Assert.Equal(2, body.GetProperty("version").GetInt32());                        // the caller's, not the fetched 3
    }

    [Fact]
    public async Task A_stale_edit_tells_the_agent_to_read_again()
    {
        _server.Api.Answer($"GET /api/tasks/{Id}", Bins);
        _server.Api.Answer($"PUT /api/tasks/{Id}",
            """{"title":"Conflict","status":409,"detail":"Someone else changed this task. Reload it and try again."}""",
            HttpStatusCode.Conflict);

        var result = await _server.Client.CallToolAsync("edit_task",
            new Dictionary<string, object?> { ["id"] = Id, ["version"] = 2, ["title"] = "Bins" });

        Assert.True(result.IsError);
        Assert.Contains("current version", ShoppingToolsTests.Text(result));
    }

    [Theory]
    [InlineData("start_task", "start")]
    [InlineData("complete_task", "complete")]
    [InlineData("reopen_task", "reopen")]
    public async Task Moving_a_task_calls_its_command(string tool, string command)
    {
        _server.Api.Answer($"POST /api/tasks/{Id}/{command}", Bins);

        var result = await _server.Client.CallToolAsync(tool, new Dictionary<string, object?> { ["id"] = Id });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal($"POST /api/tasks/{Id}/{command}", Assert.Single(_server.Api.Requests).Route);
    }

    [Fact]
    public async Task Assign_task_sends_the_assignee()
    {
        const string member = "0199e000-0000-7000-8000-000000000002";
        _server.Api.Answer($"POST /api/tasks/{Id}/assign", Bins);

        await _server.Client.CallToolAsync("assign_task", new Dictionary<string, object?> { ["id"] = Id, ["assigneeId"] = member });

        Assert.Equal(member, Body(Assert.Single(_server.Api.Requests).Body).GetProperty("assigneeId").GetString());
    }

    [Fact]
    public async Task Categories_and_members_come_from_their_endpoints()
    {
        _server.Api.Answer("GET /api/tasks/categories", $$"""[{"id":"{{Category}}","name":"House","sortOrder":0}]""");
        _server.Api.Answer("GET /api/households/members",
            """[{"userId":"0199e000-0000-7000-8000-000000000002","displayName":"Sam","avatarUrl":null,"role":"member","joinedAt":"2026-10-01T00:00:00Z"}]""");

        var categories = await _server.Client.CallToolAsync("list_categories");
        var members = await _server.Client.CallToolAsync("list_members");

        Assert.Contains("House", ShoppingToolsTests.Text(categories));
        Assert.Contains("Sam", ShoppingToolsTests.Text(members));
    }

    private static JsonElement Body(string? json) => JsonDocument.Parse(json!).RootElement;
}
