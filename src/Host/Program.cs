using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyApp.Host.Database;
using FamilyApp.Host.Events;
using FamilyApp.Host.Health;
using FamilyApp.Host.Security;
using FamilyApp.Modules.Households;
using FamilyApp.Modules.Identity;
using FamilyApp.Modules.Points;
using FamilyApp.Modules.Shopping;
using FamilyApp.Modules.Tasks;
using FamilyApp.SharedKernel;
using FamilyApp.SharedKernel.Events;
using FamilyApp.SharedKernel.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
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

// Platform scripts first, then each module's, in the order above (ADR-021).
MigrationSource[] migrationSources =
[
    new("platform", typeof(Program).Assembly),
    .. modules.Select(module => new MigrationSource(module.Name, module.GetType().Assembly)),
];

// One connection pool for the whole app. Resolved lazily so tests can override configuration.
builder.Services.AddSingleton(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    return NpgsqlDataSource.Create(connectionString);
});

builder.Services.AddSingleton(TimeProvider.System);

// Enums travel as lower-case strings ("todo", "high"), matching the database and readable in the API.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// Set per request by .RequireHousehold(); read by modules to scope their queries (ADR-009, ADR-026).
builder.Services.AddScoped<CurrentHousehold>();
builder.Services.AddScoped<CurrentUser>();

// One connection per request, shared by every module's DbContext; write requests run in one
// transaction on it (ADR-030). Events raised during the request are dispatched before commit (ADR-031).
builder.Services.AddScoped<DbSession>();
builder.Services.AddScoped<DomainEvents>();
builder.Services.AddScoped<DomainEventDispatcher>();
builder.Services.AddScoped<ActivityLog>();

builder.Services.AddSingleton<MigrationRunner>();
foreach (var source in migrationSources)
{
    builder.Services.AddSingleton(source);
}
builder.Services.AddHostedService<MigrateOnStartupService>();

// Cookie-encryption keys live in Postgres so they survive restarts and deploys (ADR-023).
builder.Services.AddDataProtection().SetApplicationName("family-app");
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<NpgsqlDataSource>((options, dataSource) => options.XmlRepository = new PostgresXmlRepository(dataSource));

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres");

foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
}

var app = builder.Build();

// `dotnet FamilyApp.Host.dll migrate` applies migrations and exits. Fly runs this as the
// release command, before the new version receives traffic (ADR-021).
if (args.Contains("migrate"))
{
    await app.Services.GetRequiredService<MigrationRunner>().RunAsync(migrationSources);
    return;
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

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

var api = app.MapGroup("/api").UseWriteTransactions();
api.MapActivity();
foreach (var module in modules)
{
    module.MapEndpoints(api.MapGroup($"/{module.Name}"));
}

// Unknown API routes must 404, not fall through to the SPA's index.html.
app.MapFallback("/api/{**path}", () => Results.NotFound());

// Everything else is a client-side route in the React app.
app.MapFallbackToFile("index.html");

app.Run();

// Lets the integration tests reference the entry point.
public partial class Program;
