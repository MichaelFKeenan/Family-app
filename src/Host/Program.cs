using FamilyApp.Host.Health;
using FamilyApp.Modules.Households;
using FamilyApp.Modules.Identity;
using FamilyApp.Modules.Points;
using FamilyApp.Modules.Shopping;
using FamilyApp.Modules.Tasks;
using FamilyApp.SharedKernel;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Every module is listed explicitly. Adding a feature area = adding a line here (ADR-001).
IModule[] modules =
[
    new IdentityModule(),
    new HouseholdsModule(),
    new TasksModule(),
    new ShoppingModule(),
    new PointsModule(),
];

// One connection pool for the whole app. Resolved lazily so tests can override configuration.
builder.Services.AddSingleton(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    return NpgsqlDataSource.Create(connectionString);
});

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres");

foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
}

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Liveness: "is the process up?" Runs no checks, so platform probes never wake the database.
// Neon scales to zero when idle; a probe that queried it every few seconds would keep it awake (ADR-019).
app.MapHealthChecks("/api/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteJson,
});

// Readiness: "can it actually serve requests?" Includes a real Postgres round trip.
app.MapHealthChecks("/api/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteJson,
});

var api = app.MapGroup("/api");
foreach (var module in modules)
{
    module.MapEndpoints(api.MapGroup($"/{module.Name}"));
}

// Unknown API routes must 404, not fall through to the SPA's index.html.
app.MapFallback("/api/{**path}", () => Results.NotFound());

// Everything else is a client-side route in the React app.
app.MapFallbackToFile("index.html");

app.Run();
