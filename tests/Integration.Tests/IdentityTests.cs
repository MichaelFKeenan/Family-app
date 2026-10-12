using System.Net;
using System.Net.Http.Json;
using FamilyApp.Modules.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyApp.Integration.Tests;

public class IdentityTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    [Fact]
    public async Task First_sign_in_creates_a_user_and_later_sign_ins_reuse_it()
    {
        var subject = Guid.NewGuid().ToString();

        var first = await SignInAsync(new ExternalLogin("google", subject, "sam@example.com", "Sam", null));
        var second = await SignInAsync(new ExternalLogin("google", subject, "sam@example.com", "Sam K", "https://example.com/sam.png"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Sam K", second.DisplayName);
        Assert.Equal("https://example.com/sam.png", second.AvatarUrl);
    }

    [Fact]
    public async Task Different_subjects_are_different_users_even_with_the_same_email()
    {
        // Matching on email would let anyone who controls that address at another provider
        // take over the account. Identities are matched on (provider, subject) only (ADR-007).
        var a = await SignInAsync(new ExternalLogin("google", Guid.NewGuid().ToString(), "shared@example.com", "A", null));
        var b = await SignInAsync(new ExternalLogin("google", Guid.NewGuid().ToString(), "shared@example.com", "B", null));

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public async Task Missing_name_falls_back_to_email()
    {
        var user = await SignInAsync(new ExternalLogin("google", Guid.NewGuid().ToString(), "nameless@example.com", null, null));

        Assert.Equal("nameless@example.com", user.DisplayName);
    }

    [Fact]
    public async Task Me_is_401_when_signed_out()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/identity/me");

        // 401, not a redirect to a login page: this is an API.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user()
    {
        var user = await SignInAsync(new ExternalLogin("google", Guid.NewGuid().ToString(), "jo@example.com", "Jo", null));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.Id.ToString());

        var me = await client.GetFromJsonAsync<UserProfile>("/api/identity/me");

        Assert.NotNull(me);
        Assert.Equal(user.Id, me.Id);
        Assert.Equal("Jo", me.DisplayName);
    }

    [Fact]
    public async Task Me_is_401_for_a_user_that_no_longer_exists()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.CreateVersion7().ToString());

        var response = await client.GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_is_503_when_google_is_not_configured()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/identity/login");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/board?tab=1", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_local_return_urls_are_allowed(string? url, bool allowed)
    {
        Assert.Equal(allowed, IdentityModule.IsLocalPath(url));
    }

    [Fact]
    public async Task Cookie_encryption_keys_are_stored_in_postgres()
    {
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        protector.Protect("hello");

        await using var command = factory.Services.GetRequiredService<NpgsqlDataSource>()
            .CreateCommand("SELECT count(*) FROM platform.data_protection_keys");
        var keys = (long)(await command.ExecuteScalarAsync())!;

        Assert.True(keys >= 1);
    }

    private async Task<UserProfile> SignInAsync(ExternalLogin login)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityService>().SignInExternalAsync(login);
    }
}
