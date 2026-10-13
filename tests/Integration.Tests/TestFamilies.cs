using System.Net.Http.Json;
using FamilyApp.Modules.Households;
using FamilyApp.Modules.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyApp.Integration.Tests;

/// <summary>Builds signed-in users and households for tests through the real API.</summary>
public static class TestFamilies
{
    public sealed record TestUser(Guid Id, HttpClient Client);

    public static async Task<TestUser> NewUserAsync(this FamilyAppFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<IdentityService>()
            .SignInExternalAsync(new ExternalLogin("google", Guid.NewGuid().ToString(), null, name, null));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.Id.ToString());
        return new TestUser(user.Id, client);
    }

    /// <summary>A household whose admin is a new user, plus <paramref name="members"/> who joined by invite.</summary>
    public static async Task<(HouseholdSummary Household, TestUser Admin, TestUser[] Members)> NewHouseholdAsync(
        this FamilyAppFactory factory, string name, int members = 0)
    {
        var admin = await factory.NewUserAsync($"{name} admin");
        var created = await admin.Client.PostAsJsonAsync("/api/households", new { name });
        created.EnsureSuccessStatusCode();
        var household = (await created.Content.ReadFromJsonAsync<HouseholdSummary>())!;

        var joined = new TestUser[members];
        for (var i = 0; i < members; i++)
        {
            var invite = await admin.Client.PostAsync("/api/households/invites", null);
            invite.EnsureSuccessStatusCode();
            var code = (await invite.Content.ReadFromJsonAsync<CreatedInvite>())!.Code;

            joined[i] = await factory.NewUserAsync($"{name} member {i + 1}");
            (await joined[i].Client.PostAsync($"/api/households/invites/{code}/accept", null)).EnsureSuccessStatusCode();
        }

        return (household, admin, joined);
    }
}
