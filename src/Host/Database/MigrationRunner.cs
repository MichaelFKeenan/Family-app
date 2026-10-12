using System.Reflection;
using Npgsql;

namespace FamilyApp.Host.Database;

/// <summary>
/// Applies plain SQL migration scripts (ADR-021).
///
/// Each module embeds its scripts as resources named <c>migrations/NNNN_description.sql</c>.
/// Scripts run in ordinal filename order, once each, recorded in <c>platform.schema_migrations</c>.
/// Every script runs in its own transaction together with its bookkeeping row, so a failing
/// script leaves no trace and the next run retries it.
/// </summary>
public sealed class MigrationRunner(NpgsqlDataSource dataSource, ILogger<MigrationRunner> logger)
{
    private const string ResourcePrefix = "migrations/";

    // Any constant works; it only has to be the same for every runner.
    // A transaction-scoped lock is used because Neon's pooler hands out a different
    // server connection per transaction, which breaks session-scoped advisory locks.
    private const long AdvisoryLockKey = 4_242_001;

    private const string Bootstrap = """
        CREATE SCHEMA IF NOT EXISTS platform;
        CREATE TABLE IF NOT EXISTS platform.schema_migrations (
            module     text        NOT NULL,
            script     text        NOT NULL,
            applied_at timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (module, script)
        );
        """;

    public async Task<int> RunAsync(IEnumerable<MigrationSource> sources, CancellationToken cancellationToken = default)
    {
        await RunLockedAsync(Bootstrap, cancellationToken);

        var applied = 0;
        foreach (var source in sources)
        {
            var scripts = source.Assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                            && name.EndsWith(".sql", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal);

            foreach (var resource in scripts)
            {
                if (await ApplyAsync(source, resource, cancellationToken))
                {
                    applied++;
                }
            }
        }

        logger.LogInformation("Migrations complete: {Applied} script(s) applied.", applied);
        return applied;
    }

    private async Task<bool> ApplyAsync(MigrationSource source, string resource, CancellationToken cancellationToken)
    {
        var script = resource[ResourcePrefix.Length..];

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        // Checked after taking the lock, so two runners can never both apply the same script.
        await using (var check = new NpgsqlCommand(
            "SELECT 1 FROM platform.schema_migrations WHERE module = @module AND script = @script",
            connection, transaction))
        {
            check.Parameters.AddWithValue("module", source.Module);
            check.Parameters.AddWithValue("script", script);
            if (await check.ExecuteScalarAsync(cancellationToken) is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        string sql;
        await using (var stream = source.Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing migration resource {resource}."))
        using (var reader = new StreamReader(stream))
        {
            sql = await reader.ReadToEndAsync(cancellationToken);
        }

        logger.LogInformation("Applying migration {Module}/{Script}", source.Module, script);

        await using (var apply = new NpgsqlCommand(sql, connection, transaction))
        {
            await apply.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var record = new NpgsqlCommand(
            "INSERT INTO platform.schema_migrations (module, script) VALUES (@module, @script)",
            connection, transaction))
        {
            record.Parameters.AddWithValue("module", source.Module);
            record.Parameters.AddWithValue("script", script);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task RunLockedAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

/// <summary>Where a module's migration scripts live. Module name doubles as the bookkeeping key.</summary>
public sealed record MigrationSource(string Module, Assembly Assembly);
