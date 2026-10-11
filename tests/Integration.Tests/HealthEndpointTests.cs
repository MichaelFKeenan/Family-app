using System.Net;
using System.Text.Json;

namespace FamilyApp.Integration.Tests;

public class HealthEndpointTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    [Fact]
    public async Task Health_is_healthy_when_postgres_is_reachable()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.RootElement.GetProperty("checks").GetProperty("postgres").GetString());
    }

    [Fact]
    public async Task Liveness_runs_no_checks()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.GetProperty("checks").EnumerateObject());
    }

    [Fact]
    public async Task Unknown_api_route_returns_404_not_the_spa()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
