using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FamilyApp.Host.Health;

/// <summary>Writes health results as small JSON: overall status plus one entry per check.</summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static Task WriteJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Status.ToString()),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Options));
    }
}
