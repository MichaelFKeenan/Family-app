using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace FamilyApp.Host.Health;

/// <summary>Healthy only if a real round trip to Postgres succeeds.</summary>
public sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cannot reach Postgres.", ex);
        }
    }
}
