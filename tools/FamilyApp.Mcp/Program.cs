using FamilyApp.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// A local MCP server for the Family Board (ADR-040). Claude Code starts it and talks to it over
// stdin/stdout, so stdout belongs to the protocol: every log line must go to stderr instead.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddFamilyApi(builder.Configuration);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
