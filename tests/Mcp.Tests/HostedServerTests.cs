using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FamilyApp.Mcp.Tests;

/// <summary>
/// The hosted mode (ADR-041): the built server started with --http, as the container runs it, and real
/// MCP clients over Streamable HTTP. What matters most is whose token reaches the API.
/// </summary>
public sealed class HostedServerTests : IAsyncLifetime
{
    private FakeApi _api = null!;
    private Process _server = null!;
    private Uri _endpoint = null!;

    public async Task InitializeAsync()
    {
        _api = await FakeApi.StartAsync();
        _api.Answer("GET /api/shopping", "[]");

        var url = $"http://127.0.0.1:{FreePort()}";
        _endpoint = new Uri($"{url}/mcp");
        var start = new ProcessStartInfo("dotnet", [McpTestServer.ServerDll(), "--http"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment =
            {
                ["ASPNETCORE_URLS"] = url,
                ["FAMILY_API_URL"] = _api.Url,
                // Must be ignored: the hosted server only ever uses the caller's token.
                ["FAMILY_API_TOKEN"] = "fb_server",
            },
        };
        _server = Process.Start(start)!;
        _server.OutputDataReceived += (_, _) => { };
        _server.ErrorDataReceived += (_, _) => { };
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();

        await WaitUntilListeningAsync();
    }

    [Fact]
    public async Task A_request_without_a_token_is_turned_away_before_the_api()
    {
        using var http = new HttpClient();

        var response = await http.PostAsync(_endpoint, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Agent tokens", await response.Content.ReadAsStringAsync());
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task Each_call_carries_the_callers_own_token()
    {
        await using var alice = await ConnectAsync("fb_alice");
        await using var bob = await ConnectAsync("fb_bob");

        var aliceResult = await alice.CallToolAsync("list_shopping");
        var bobResult = await bob.CallToolAsync("list_shopping");
        await alice.CallToolAsync("list_shopping");

        Assert.NotEqual(true, aliceResult.IsError);
        Assert.NotEqual(true, bobResult.IsError);
        Assert.Equal(
            ["Bearer fb_alice", "Bearer fb_bob", "Bearer fb_alice"],
            _api.Requests.Select(r => r.Authorization));
    }

    [Fact]
    public async Task A_rejected_token_tells_the_agent_how_to_fix_it()
    {
        _api.Answer("GET /api/shopping", "", HttpStatusCode.Unauthorized);
        await using var client = await ConnectAsync("fb_revoked");

        var result = await client.CallToolAsync("list_shopping");

        Assert.True(result.IsError);
        Assert.Contains("Authorization header", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    private Task<McpClient> ConnectAsync(string token) =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = _endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        }));

    private async Task WaitUntilListeningAsync()
    {
        using var http = new HttpClient();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (_server.HasExited)
            {
                throw new InvalidOperationException($"The MCP server exited with code {_server.ExitCode}.");
            }
            try
            {
                await http.GetAsync(_endpoint);
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(100);
            }
        }
        throw new TimeoutException($"The MCP server didn't start listening on {_endpoint}.");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async Task DisposeAsync()
    {
        _server.Kill(entireProcessTree: true);
        await _server.WaitForExitAsync();
        _server.Dispose();
        await _api.DisposeAsync();
    }
}
