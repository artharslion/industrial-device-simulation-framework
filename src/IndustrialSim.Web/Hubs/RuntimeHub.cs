using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using IndustrialSim.Application.Security;

namespace IndustrialSim.Web.Hubs;

public sealed class RuntimeHub(RuntimeStreamBroker broker) : Hub
{
    public async IAsyncEnumerable<RuntimeStreamEvent> Stream([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var subscription = broker.Subscribe();
        await foreach (var @event in subscription.Reader.ReadAllAsync(cancellationToken)) yield return @event;
    }
}

public static class RuntimeHubEndpoints
{
    public static HubEndpointConventionBuilder MapRuntimeHub(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHub<RuntimeHub>("/hubs/runtime").RequireAuthorization(IndustrialPolicies.Viewer);
}
