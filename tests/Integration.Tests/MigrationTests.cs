using FamilyApp.Host.Database;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyApp.Integration.Tests;

public class MigrationTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    [Fact]
    public async Task Startup_applies_every_script_once()
    {
        var dataSource = factory.Services.GetRequiredService<NpgsqlDataSource>();

        var applied = await ScriptsAsync(dataSource);

        Assert.Contains("platform/0001_create_data_protection_keys.sql", applied);
        Assert.Contains("identity/0001_create_users.sql", applied);
    }

    [Fact]
    public async Task Running_again_applies_nothing()
    {
        var runner = factory.Services.GetRequiredService<MigrationRunner>();
        var sources = factory.Services.GetServices<MigrationSource>();
        var dataSource = factory.Services.GetRequiredService<NpgsqlDataSource>();
        var before = await ScriptsAsync(dataSource);

        var applied = await runner.RunAsync(sources);

        Assert.Equal(0, applied);
        Assert.Equal(before, await ScriptsAsync(dataSource));
    }

    private static async Task<List<string>> ScriptsAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT module || '/' || script FROM platform.schema_migrations ORDER BY 1");
        await using var reader = await command.ExecuteReaderAsync();

        var scripts = new List<string>();
        while (await reader.ReadAsync())
        {
            scripts.Add(reader.GetString(0));
        }
        return scripts;
    }
}
