namespace IndustrialSim.Observability.Metrics;

public static class MetricLabels
{
    public static string Mode(bool deterministic) => deterministic ? "deterministic" : "realtime";

    public static string ScenarioAction(string? action) => Normalize(action) switch
    {
        "set" => "set",
        "ramp" => "ramp",
        "command" => "command",
        "wait" => "wait",
        "fault" => "fault",
        _ => "unknown"
    };

    public static string Protocol(string? protocol) => Normalize(protocol) switch
    {
        "opcua" => "opcua",
        "modbus" or "modbustcp" => "modbus",
        _ => "other"
    };

    public static string ProtocolOperation(string? operation) => Normalize(operation) switch
    {
        "start" => "start",
        "stop" => "stop",
        "read" => "read",
        "write" => "write",
        "command" => "command",
        _ => "other"
    };

    public static string FaultCategory(Faults.FaultCategory category) => category switch
    {
        Faults.FaultCategory.Data => "data",
        Faults.FaultCategory.Device => "device",
        Faults.FaultCategory.Network => "network",
        _ => "device"
    };

    public static string Stream(string? stream) => Normalize(stream) == "signalr" ? "signalr" : "runtime-log";
    public static string DropStage(string? stage) => Normalize(stage) == "subscriber" ? "subscriber" : "ingress";

    private static string Normalize(string? value) => string.Concat((value ?? string.Empty)
        .Where(char.IsLetterOrDigit))
        .ToLowerInvariant();
}
