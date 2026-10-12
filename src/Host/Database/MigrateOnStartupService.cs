namespace FamilyApp.Host.Database;

/// <summary>
/// Runs migrations during startup when <c>Database:MigrateOnStartup</c> is true.
/// Used for local development and tests only. Production migrates in Fly's release
/// command, before the new version takes traffic (ADR-021).
///
/// It is an <see cref="IHostedService"/> rather than a background service on purpose:
/// startup waits for it, so no request can arrive before the schema exists.
/// </summary>
public sealed class MigrateOnStartupService(
    IConfiguration configuration,
    MigrationRunner runner,
    IEnumerable<MigrationSource> sources) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            await runner.RunAsync(sources, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
