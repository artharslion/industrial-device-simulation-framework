using System.Diagnostics;

namespace IndustrialSim.Observability.Tracing;

public static class IndustrialSimActivitySource
{
    public const string Name = "IndustrialSim.Observability";
    public const string Version = "1.0.0";
    public static ActivitySource Source { get; } = new(Name, Version);
}
