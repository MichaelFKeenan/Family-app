using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;

namespace FamilyApp.Mcp;

/// <summary>
/// The Family Board API as the tools see it: JSON in, JSON out, with an access token (ADR-039).
/// Failures become <see cref="McpException"/> with a message written for the agent, because that
/// message is what the agent reads. Any other exception reaches it only as a vague "an error occurred".
/// </summary>
public sealed class FamilyApi(HttpClient http)
{
    /// <summary>The API's conventions: camelCase, enums as lower-case strings ("todo").</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public Task<T> GetAsync<T>(string path, CancellationToken cancellationToken) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    /// <summary>A POST with an optional JSON body: creates and the command endpoints (/start, /check).</summary>
    public Task<T> PostAsync<T>(string path, object? body, CancellationToken cancellationToken) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body, options: Json) }, cancellationToken);

    public Task<T> PutAsync<T>(string path, object body, CancellationToken cancellationToken) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body, options: Json) }, cancellationToken);

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new McpException($"Couldn't reach the Family Board at {http.BaseAddress}: {ex.Message}", ex);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
                }

                throw new McpException(await DescribeFailureAsync(response, cancellationToken));
            }
        }
    }

    private static async Task<string> DescribeFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Errors come back as RFC 7807 problem details: { title, detail, status, errors? }.
        // Some have no body at all (a 401, a bare 404), so a missing or non-JSON body is normal.
        Problem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<Problem>(Json, cancellationToken);
        }
        catch (JsonException)
        {
        }

        var detail = problem?.Detail ?? problem?.Title;
        if (problem?.Errors is { Count: > 0 } errors)
        {
            detail = string.Join(" ", errors.SelectMany(e => e.Value));
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "The Family Board rejected the access token: it is missing, unknown, revoked or expired. " +
                "Ask the user to create a new one in the app (Family tab, Agent tokens) and set FAMILY_API_TOKEN.",
            HttpStatusCode.Forbidden => $"Not allowed: {detail ?? "the token's user is no longer in this household."}",
            HttpStatusCode.NotFound => "Not found. It may have been deleted, or the id is wrong; list again to get current ids.",
            HttpStatusCode.Conflict =>
                $"Conflict: {detail ?? "someone changed it since you read it."} Read it again for the current version, then retry if it still makes sense.",
            HttpStatusCode.BadRequest => $"The request was invalid: {detail ?? "check the arguments."}",
            _ => $"The Family Board answered {(int)response.StatusCode}: {detail ?? response.ReasonPhrase}",
        };
    }

    private sealed record Problem(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}

public static class FamilyApiRegistration
{
    public const string DefaultUrl = "https://65-109-172-182.sslip.io";

    /// <summary>
    /// FAMILY_API_URL (defaults to the live site) and FAMILY_API_TOKEN come from the environment,
    /// which .mcp.json fills from the user's own shell, so no secret is ever in the repo.
    /// </summary>
    public static IServiceCollection AddFamilyApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<FamilyApi>(http =>
        {
            http.BaseAddress = new Uri(configuration["FAMILY_API_URL"] is { Length: > 0 } url ? url : DefaultUrl);
            if (configuration["FAMILY_API_TOKEN"] is { Length: > 0 } token)
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            // No token: start anyway. The first tool call gets a 401, which says how to fix it,
            // and that reaches the user through the agent; a crash on startup would not.
        });
        return services;
    }
}
