using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace FamilyApp.Mcp.Tests;

/// <summary>
/// Starts the real MCP server the way Claude Code does (its built dll as a child process, talking over
/// stdin/stdout) and connects a real MCP client to it. So these tests also catch anything that writes
/// to stdout and corrupts the protocol.
///
/// The Family Board API is faked by a tiny web server on localhost: each test says what it answers,
/// and can then check exactly what the tool sent.
/// </summary>
public sealed class McpTestServer : IAsyncDisposable
{
    private McpTestServer(McpClient client, FakeApi api)
    {
        Client = client;
        Api = api;
    }

    public McpClient Client { get; }

    public FakeApi Api { get; }

    public static async Task<McpTestServer> StartAsync()
    {
        var api = await FakeApi.StartAsync();

        var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "family-board",
            Command = "dotnet",
            Arguments = [ServerDll()],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["FAMILY_API_URL"] = api.Url,
                ["FAMILY_API_TOKEN"] = "fb_test",
            },
        }));
        return new McpTestServer(client, api);
    }

    /// <summary>
    /// The MCP project's own build output, same configuration as this test run. Not the copy beside the
    /// tests: that folder is laid out for an ASP.NET Core app, so the server's own dependencies are missing.
    /// </summary>
    private static string ServerDll()
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory);   // .../bin/{Configuration}/net10.0/
        var configuration = testOutput.Parent!.Name;
        var root = testOutput;
        while (!File.Exists(Path.Combine(root.FullName, "FamilyApp.slnx")))
        {
            root = root.Parent ?? throw new InvalidOperationException("Couldn't find the repository root.");
        }
        return Path.Combine(root.FullName, "tools", "FamilyApp.Mcp", "bin", configuration, testOutput.Name, "FamilyApp.Mcp.dll");
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Api.DisposeAsync();
    }
}

/// <summary>A stand-in for the Family Board API: canned answers by "METHOD /path", and a record of every request.</summary>
public sealed class FakeApi : IAsyncDisposable
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _answers = [];
    private WebApplication _app = null!;

    public string Url { get; private set; } = "";

    public List<(string Route, string? Body, string? Authorization)> Requests { get; } = [];

    public void Answer(string route, string json, HttpStatusCode status = HttpStatusCode.OK) => _answers[route] = (status, json);

    public static async Task<FakeApi> StartAsync()
    {
        var api = new FakeApi();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        api._app = builder.Build();
        api._app.Run(api.HandleAsync);
        await api._app.StartAsync();
        api.Url = api._app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        return api;
    }

    private async Task HandleAsync(HttpContext http)
    {
        var route = $"{http.Request.Method} {http.Request.Path}{http.Request.QueryString}";
        var body = await new StreamReader(http.Request.Body).ReadToEndAsync();
        lock (Requests)
        {
            Requests.Add((route, body.Length == 0 ? null : body, http.Request.Headers.Authorization.ToString()));
        }

        var (status, json) = _answers.TryGetValue(route, out var answer) ? answer : (HttpStatusCode.NotFound, "");
        http.Response.StatusCode = (int)status;
        if (json.Length > 0)
        {
            http.Response.ContentType = "application/json";
            await http.Response.WriteAsync(json);
        }
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
