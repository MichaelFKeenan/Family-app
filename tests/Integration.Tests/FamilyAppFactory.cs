using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace FamilyApp.Integration.Tests;

/// <summary>
/// Boots the real app against a real, throwaway Postgres in Docker.
/// No mocks of the database: if it works here, it works against Postgres.
///
/// Google is not configured, so nobody can sign in for real. Instead, a request carrying the
/// <see cref="TestAuthHandler.UserHeader"/> header is treated as signed in as that user id.
/// Requests without it go through the real cookie handler, so anonymous behaviour is genuine.
/// </summary>
public sealed class FamilyAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _postgres.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });

            // The cookie handler asks the test handler who the user is. Challenges (401s) still
            // come from the real cookie handler.
            services.PostConfigure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme,
                options => options.ForwardAuthenticate = TestAuthHandler.Scheme);
        });
    }
}
