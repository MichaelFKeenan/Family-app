using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using FamilyApp.Host.Live;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace FamilyApp.Integration.Tests;

/// <summary>
/// Live updates (ADR-042): after a change commits, the right screens are told which queries to refetch,
/// and nobody else is. Long polling stands in for WebSockets: the test server speaks it, and what's
/// tested here is who hears what, not the transport.
/// </summary>
public class LiveUpdateTests(FamilyAppFactory factory) : IClassFixture<FamilyAppFactory>
{
    /// <summary>How long to listen before concluding that nothing is coming.</summary>
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(750);

    [Fact]
    public async Task A_change_reaches_everyone_in_the_household()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Live", members: 1);
        await using var adminScreen = await ConnectAsync(admin);
        await using var memberScreen = await ConnectAsync(members[0]);

        await members[0].Client.PostAsJsonAsync("/api/shopping", new { name = "Milk" });

        Assert.Equal(["activity", "shopping"], await adminScreen.NextAsync());
        // The sender's own screen hears it too; refetching what it just changed is harmless.
        Assert.Equal(["activity", "shopping"], await memberScreen.NextAsync());
    }

    [Fact]
    public async Task One_request_sends_one_message_with_every_topic_it_touched()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Coalesced", members: 1);
        var task = await (await admin.Client.PostAsJsonAsync("/api/tasks", new { title = "Bins", points = 5 }))
            .Content.ReadFromJsonAsync<CreatedTask>();
        await using var memberScreen = await ConnectAsync(members[0]);

        // Completing raises TaskCompleted, and its handler raises PointsAwarded: one request, one nudge.
        (await admin.Client.PostAsync($"/api/tasks/{task!.Id}/complete", null)).EnsureSuccessStatusCode();

        Assert.Equal(["activity", "leaderboard", "tasks"], await memberScreen.NextAsync());
        await memberScreen.AssertSilentAsync();
    }

    [Fact]
    public async Task Other_households_hear_nothing()
    {
        var (_, admin, _) = await factory.NewHouseholdAsync("Ours");
        var (_, stranger, _) = await factory.NewHouseholdAsync("Theirs");
        await using var strangerScreen = await ConnectAsync(stranger);

        await admin.Client.PostAsJsonAsync("/api/shopping", new { name = "Eggs" });

        await strangerScreen.AssertSilentAsync();
    }

    [Fact]
    public async Task A_personal_task_change_reaches_only_its_owner()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Private", members: 1);
        await using var ownerScreen = await ConnectAsync(admin);
        await using var memberScreen = await ConnectAsync(members[0]);

        await admin.Client.PostAsJsonAsync("/api/tasks", new { title = "Renew my passport", personal = true });

        Assert.Equal(["activity", "tasks"], await ownerScreen.NextAsync());
        await memberScreen.AssertSilentAsync();
    }

    [Fact]
    public async Task A_request_that_rolls_back_sends_nothing()
    {
        var (_, admin, members) = await factory.NewHouseholdAsync("Rolled back", members: 1);
        await using var memberScreen = await ConnectAsync(members[0]);

        try
        {
            await admin.Client.PostAsJsonAsync("/api/tasks", new { title = FailingHandler.Trigger });
        }
        catch (InvalidOperationException)
        {
            // The test server may surface the server-side exception directly instead of a 500.
        }

        await memberScreen.AssertSilentAsync();
    }

    [Fact]
    public async Task Someone_signed_out_cannot_connect()
    {
        var connection = Build(configureHeaders: _ => { });

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    private sealed record CreatedTask(Guid Id);

    private async Task<Screen> ConnectAsync(TestFamilies.TestUser user)
    {
        var connection = Build(headers => headers[TestAuthHandler.UserHeader] = user.Id.ToString());
        var screen = new Screen(connection);
        await connection.StartAsync();
        await screen.WaitUntilReadyAsync();
        return screen;
    }

    private HubConnection Build(Action<IDictionary<string, string>> configureHeaders) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/api/live"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                configureHeaders(options.Headers);
            })
            .Build();

    /// <summary>One browser tab's live connection, recording every nudge it receives.</summary>
    private sealed class Screen : IAsyncDisposable
    {
        private readonly HubConnection _connection;
        private readonly Channel<string[]> _changes = Channel.CreateUnbounded<string[]>();
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Screen(HubConnection connection)
        {
            _connection = connection;
            _connection.On<string[]>(LiveUpdates.ChangedMethod, topics => _changes.Writer.TryWrite(topics));
            _connection.On(LiveHub.ReadyMethod, () => _ready.TrySetResult());
        }

        public Task WaitUntilReadyAsync() => _ready.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public async Task<string[]> NextAsync() =>
            await _changes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        public async Task AssertSilentAsync()
        {
            await Task.Delay(Silence);
            Assert.False(_changes.Reader.TryRead(out var topics), $"Expected no update, got [{string.Join(", ", topics ?? [])}].");
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
