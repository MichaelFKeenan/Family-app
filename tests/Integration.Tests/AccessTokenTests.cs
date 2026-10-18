using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyApp.Modules.Households;
using FamilyApp.Modules.Identity;
using FamilyApp.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyApp.Integration.Tests;

/// <summary>Personal access tokens for agents (ADR-039).</summary>
public class AccessTokenTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    // Creating, listing and revoking: cookie only.

    [Fact]
    public async Task A_new_token_is_shown_once_and_the_list_never_shows_it()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Tokens shown once");

        var response = await admin.Client.PostAsJsonAsync("/api/identity/tokens", new { name = " laptop Claude Code " });
        var created = (await response.Content.ReadFromJsonAsync<CreatedAccessToken>())!;
        var listJson = await admin.Client.GetStringAsync("/api/identity/tokens");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith("fb_", created.Token);
        Assert.Equal("laptop Claude Code", created.Name);
        Assert.InRange(created.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromDays(89), TimeSpan.FromDays(91));
        Assert.Contains(created.Id.ToString(), listJson);
        Assert.DoesNotContain(created.Token, listJson);
    }

    [Fact]
    public async Task Only_a_hash_of_the_token_is_stored()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Tokens hashed");
        var created = await CreateTokenAsync(admin.Client);

        await using var command = DataSource.CreateCommand("SELECT token_hash FROM identity.access_tokens WHERE id = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = created.Id });
        var stored = (byte[])(await command.ExecuteScalarAsync())!;

        Assert.Equal(AccessTokenFormat.Hash(created.Token), stored);
    }

    [Fact]
    public async Task A_token_needs_a_name()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Tokens named");

        var response = await admin.Client.PostAsJsonAsync("/api/identity/tokens", new { name = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nobody_can_revoke_someone_elses_token()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Tokens revoke others", members: 1);
        var created = await CreateTokenAsync(admin.Client);

        var response = await members[0].Client.PostAsync($"/api/identity/tokens/{created.Id}/revoke", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Using a token: your handler makes these pass.

    [Fact]
    public async Task A_token_can_read_and_change_the_shopping_list()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Tokens shopping");
        using var agent = AgentClient((await CreateTokenAsync(admin.Client)).Token);

        var added = await agent.PostAsJsonAsync("/api/shopping", new { name = "Milk" });
        var list = await agent.GetStringAsync("/api/shopping");

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Contains("Milk", list);
    }

    [Fact]
    public async Task A_token_can_read_the_board_and_the_members()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Tokens board", members: 1);
        using var agent = AgentClient((await CreateTokenAsync(admin.Client)).Token);

        var board = await agent.GetAsync("/api/tasks");
        var members = await agent.GetAsync("/api/households/members");

        Assert.Equal(HttpStatusCode.OK, board.StatusCode);
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
    }

    [Fact]
    public async Task Changes_made_with_a_token_are_made_as_its_user()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Tokens act as user");
        using var agent = AgentClient((await CreateTokenAsync(admin.Client)).Token);

        var added = await agent.PostAsJsonAsync("/api/shopping", new { name = "Bread" });
        var json = await added.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Contains($"\"addedBy\":\"{admin.Id}\"", json);
    }

    [Fact]
    public async Task Using_a_token_records_when_it_was_last_used()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Tokens last used");
        var created = await CreateTokenAsync(admin.Client);
        using var agent = AgentClient(created.Token);

        (await agent.GetAsync("/api/shopping")).EnsureSuccessStatusCode();
        var tokens = await admin.Client.GetFromJsonAsync<AccessTokenSummary[]>("/api/identity/tokens");

        Assert.NotNull(Assert.Single(tokens!).LastUsedAt);
    }

    [Fact]
    public async Task A_token_acts_only_in_its_own_household_whatever_the_header_says()
    {
        var (home, admin, _) = await factory.NewHouseholdAsync("Tokens home");
        var other = await CreateSecondHouseholdAsync(admin.Client, "Tokens other");
        var token = await CreateTokenAsync(admin.Client, householdId: home.Id);
        using var agent = AgentClient(token.Token);
        agent.DefaultRequestHeaders.Add(HouseholdEndpoints.HeaderName, other.Id.ToString());

        (await agent.PostAsJsonAsync("/api/shopping", new { name = "Butter" })).EnsureSuccessStatusCode();

        Assert.Contains("Butter", await GetShoppingAsync(admin.Client, home.Id));
        Assert.DoesNotContain("Butter", await GetShoppingAsync(admin.Client, other.Id));
    }

    // Refusals. Some of these pass before the handler exists; they guard against it letting too much in.

    [Theory]
    [InlineData("fb_not-a-real-token")]
    [InlineData("not-even-our-format")]
    public async Task An_unknown_token_is_401(string token)
    {
        using var agent = AgentClient(token);

        var response = await agent.GetAsync("/api/shopping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_revoked_token_is_401()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Tokens revoked");
        var created = await CreateTokenAsync(admin.Client);
        using var agent = AgentClient(created.Token);
        (await agent.GetAsync("/api/shopping")).EnsureSuccessStatusCode();

        (await admin.Client.PostAsync($"/api/identity/tokens/{created.Id}/revoke", null)).EnsureSuccessStatusCode();
        var response = await agent.GetAsync("/api/shopping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_401()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Tokens expired");
        var created = await CreateTokenAsync(admin.Client);
        using var agent = AgentClient(created.Token);
        (await agent.GetAsync("/api/shopping")).EnsureSuccessStatusCode();

        await ExecuteAsync("UPDATE identity.access_tokens SET expires_at = now() - interval '1 second' WHERE id = $1", created.Id);
        var response = await agent.GetAsync("/api/shopping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_stops_working_when_its_user_leaves_the_household()
    {
        var (home, _, members) = await factory.NewHouseholdAsync("Tokens leaver", members: 1);
        using var agent = AgentClient((await CreateTokenAsync(members[0].Client)).Token);
        (await agent.GetAsync("/api/shopping")).EnsureSuccessStatusCode();

        await ExecuteAsync("DELETE FROM households.memberships WHERE household_id = $1 AND user_id = $2", home.Id, members[0].Id);
        var response = await agent.GetAsync("/api/shopping");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/identity/tokens")]
    [InlineData("GET", "/api/identity/tokens")]
    [InlineData("POST", "/api/households/invites")]
    [InlineData("POST", "/api/households")]
    [InlineData("GET", "/api/identity/me")]
    [InlineData("GET", "/api/activity")]
    public async Task A_token_cannot_reach_endpoints_that_did_not_opt_in(string method, string path)
    {
        var (_, admin, _) = await factory.NewHouseholdAsync($"Tokens closed {method} {path}");
        using var agent = AgentClient((await CreateTokenAsync(admin.Client)).Token);

        var response = await agent.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" ? JsonContent.Create(new { name = "sneaky" }) : null,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private NpgsqlDataSource DataSource => factory.Services.GetRequiredService<NpgsqlDataSource>();

    private async Task ExecuteAsync(string sql, params object[] parameters)
    {
        await using var command = DataSource.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A client like an agent's: no cookie, no test user header, just the token.</summary>
    private HttpClient AgentClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<CreatedAccessToken> CreateTokenAsync(HttpClient client, Guid? householdId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/tokens")
        {
            Content = JsonContent.Create(new { name = "test agent" }),
        };
        if (householdId is { } id)
        {
            request.Headers.Add(HouseholdEndpoints.HeaderName, id.ToString());
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedAccessToken>())!;
    }

    private static async Task<HouseholdSummary> CreateSecondHouseholdAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/households", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HouseholdSummary>())!;
    }

    private static async Task<string> GetShoppingAsync(HttpClient client, Guid householdId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shopping");
        request.Headers.Add(HouseholdEndpoints.HeaderName, householdId.ToString());
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
