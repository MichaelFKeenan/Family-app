using System.Net;
using System.Net.Http.Json;
using FamilyApp.Modules.Households;
using FamilyApp.Modules.Identity;
using FamilyApp.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyApp.Integration.Tests;

public class HouseholdTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    [Fact]
    public async Task Creating_a_household_makes_you_its_admin()
    {
        var alex = await NewUserAsync("Alex");

        var created = await CreateHouseholdAsync(alex, "The Alexes");
        var mine = await alex.GetFromJsonAsync<List<HouseholdSummary>>("/api/households");

        Assert.Equal("admin", created.Role);
        var only = Assert.Single(mine!);
        Assert.Equal(created.Id, only.Id);
        Assert.Equal("The Alexes", only.Name);
    }

    [Fact]
    public async Task Household_name_is_required()
    {
        var user = await NewUserAsync("Nameless");

        var response = await user.PostAsJsonAsync("/api/households", new { name = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_invite_adds_a_member_and_members_show_their_names()
    {
        var parent = await NewUserAsync("Parent");
        var partner = await NewUserAsync("Partner");
        await CreateHouseholdAsync(parent, "Home");

        var invite = await CreateInviteAsync(parent);
        var accept = await partner.PostAsync($"/api/households/invites/{invite.Code}/accept", null);
        var members = await partner.GetFromJsonAsync<List<HouseholdMember>>("/api/households/members");

        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal(new[] { "Parent", "Partner" }, members!.Select(m => m.DisplayName));
        Assert.Equal(new[] { "admin", "member" }, members!.Select(m => m.Role));
    }

    [Fact]
    public async Task An_invite_can_only_be_used_once()
    {
        var owner = await NewUserAsync("Owner");
        var first = await NewUserAsync("First");
        var second = await NewUserAsync("Second");
        await CreateHouseholdAsync(owner, "Once");
        var invite = await CreateInviteAsync(owner);

        var firstTry = await first.PostAsync($"/api/households/invites/{invite.Code}/accept", null);
        var secondTry = await second.PostAsync($"/api/households/invites/{invite.Code}/accept", null);

        Assert.Equal(HttpStatusCode.OK, firstTry.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, secondTry.StatusCode);
    }

    [Fact]
    public async Task Two_people_racing_for_one_invite_cannot_both_join()
    {
        var owner = await NewUserAsync("Racer owner");
        await CreateHouseholdAsync(owner, "Race");
        var invite = await CreateInviteAsync(owner);
        var racers = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => NewUserAsync($"Racer {i}")));

        var responses = await Task.WhenAll(racers.Select(r => r.PostAsync($"/api/households/invites/{invite.Code}/accept", null)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_expired_invite_is_rejected()
    {
        var owner = await NewUserAsync("Expiring owner");
        var late = await NewUserAsync("Late");
        await CreateHouseholdAsync(owner, "Expired");
        var invite = await CreateInviteAsync(owner);

        await using (var command = factory.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand(
            "UPDATE households.invites SET expires_at = now() - interval '1 minute' WHERE code_hash = $1"))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = InviteCodes.Hash(invite.Code) });
            await command.ExecuteNonQueryAsync();
        }

        var response = await late.PostAsync($"/api/households/invites/{invite.Code}/accept", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_database_never_holds_the_plain_invite_code()
    {
        var owner = await NewUserAsync("Hasher");
        await CreateHouseholdAsync(owner, "Hashed");
        var invite = await CreateInviteAsync(owner);

        await using var command = factory.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand(
            "SELECT count(*) FROM households.invites WHERE position(convert_to($1, 'UTF8') in code_hash) > 0");
        command.Parameters.Add(new NpgsqlParameter { Value = invite.Code });

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Members_cannot_invite_only_admins_can()
    {
        var admin = await NewUserAsync("Admin");
        var member = await NewUserAsync("Member");
        await CreateHouseholdAsync(admin, "Roles");
        var invite = await CreateInviteAsync(admin);
        await member.PostAsync($"/api/households/invites/{invite.Code}/accept", null);

        var response = await member.PostAsync("/api/households/invites", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task One_household_cannot_see_another()
    {
        var smith = await NewUserAsync("Smith");
        var jones = await NewUserAsync("Jones");
        var smiths = await CreateHouseholdAsync(smith, "Smiths");
        await CreateHouseholdAsync(jones, "Joneses");

        // Jones asks for the Smiths' members by naming their household explicitly.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/households/members");
        request.Headers.Add(HouseholdEndpoints.HeaderName, smiths.Id.ToString());
        var snooping = await jones.SendAsync(request);

        // Without the header, Jones only ever gets the Joneses.
        var own = await jones.GetFromJsonAsync<List<HouseholdMember>>("/api/households/members");

        Assert.Equal(HttpStatusCode.Forbidden, snooping.StatusCode);
        Assert.Equal(new[] { "Jones" }, own!.Select(m => m.DisplayName));
    }

    [Fact]
    public async Task Household_endpoints_need_a_household()
    {
        var loner = await NewUserAsync("Loner");

        var response = await loner.GetAsync("/api/households/members");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task With_several_households_the_header_chooses_one()
    {
        var busy = await NewUserAsync("Busy");
        await CreateHouseholdAsync(busy, "First home");
        var second = await CreateHouseholdAsync(busy, "Second home");

        var ambiguous = await busy.GetAsync("/api/households/members");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/households/members");
        request.Headers.Add(HouseholdEndpoints.HeaderName, second.Id.ToString());
        var chosen = await busy.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
    }

    [Fact]
    public async Task Signed_out_requests_are_rejected()
    {
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/households")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/households/members")).StatusCode);
    }

    private async Task<HttpClient> NewUserAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<IdentityService>()
            .SignInExternalAsync(new ExternalLogin("google", Guid.NewGuid().ToString(), null, name, null));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.Id.ToString());
        return client;
    }

    private static async Task<HouseholdSummary> CreateHouseholdAsync(HttpClient user, string name)
    {
        var response = await user.PostAsJsonAsync("/api/households", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HouseholdSummary>())!;
    }

    private static async Task<CreatedInvite> CreateInviteAsync(HttpClient admin)
    {
        var response = await admin.PostAsync("/api/households/invites", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedInvite>())!;
    }
}
