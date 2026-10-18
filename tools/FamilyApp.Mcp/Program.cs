using FamilyApp.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The Family Board's MCP server: the same tools in two modes.
// - Local (ADR-040): Claude Code starts it and talks over stdin/stdout, with the user's own token.
// - Hosted (ADR-041), with --http: Streamable HTTP at /mcp beside the API, each caller's own token.
if (args.Contains("--http"))
{
    await RunHostedAsync(args);
}
else
{
    await RunLocalAsync(args);
}

static async Task RunLocalAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);
    // stdout belongs to the protocol, so every log line must go to stderr instead.
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddFamilyApi(builder.Configuration, FamilyApiToken.FromEnvironment);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}

static async Task RunHostedAsync(string[] args)
{
    var builder = WebApplication.CreateSlimBuilder(args);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddFamilyApi(builder.Configuration, FamilyApiToken.FromCaller);
    builder.Services
        .AddMcpServer()
        // Stateless: no sessions to lose when a deploy restarts the container.
        .WithHttpTransport(options => options.Stateless = true)
        .WithToolsFromAssembly();

    var app = builder.Build();

    // The API checks the token on every call; this only turns away requests that carry none, with a
    // message a person can act on. A 401 here never touches the database.
    app.Use(async (http, next) =>
    {
        if (!http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            http.Response.Headers.WWWAuthenticate = "Bearer";
            await http.Response.WriteAsync(
                "Send an access token as 'Authorization: Bearer fb_...'. Create one in the Family Board app: Family tab, Agent tokens.");
            return;
        }
        await next(http);
    });

    app.MapMcp("/mcp");

    await app.RunAsync();
}
