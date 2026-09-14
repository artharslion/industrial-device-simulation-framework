using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialSim.Web.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            checks = report.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString().ToLowerInvariant()
            })
        }));
    }
}
