using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;

namespace IndustrialSim.Hosting;

public sealed class DeviceLaunchException(string message, string errorCode) : ArgumentException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed record ProtocolPortBinding(string Protocol, int Port);

public sealed record MappingProfileReference(string Protocol, string Name);

public sealed record DeviceLaunchSource(
    string Kind,
    string? TemplateId = null,
    string? TemplateVersion = null,
    IReadOnlyList<MappingProfileReference>? MappingProfiles = null);

public sealed record OpcUaLaunchDefinition(
    string Endpoint,
    IReadOnlyDictionary<string, string>? DataPointNodeIds = null);

public sealed record ModbusLaunchDefinition(
    int Port,
    IReadOnlyList<ValidatedModbusMapping> Mappings);

public sealed record DeviceLaunchDefinition(
    DeviceDefinition Definition,
    SimulationHostOptions Options,
    OpcUaLaunchDefinition? OpcUa = null,
    ModbusLaunchDefinition? Modbus = null,
    DeviceLaunchSource? Source = null,
    int WebPort = 8080)
{
    public IReadOnlyList<ProtocolPortBinding> PortBindings
    {
        get
        {
            var bindings = new List<ProtocolPortBinding>(2);
            if (OpcUa is not null)
            {
                if (!Uri.TryCreate(OpcUa.Endpoint, UriKind.Absolute, out var endpoint))
                    throw new DeviceLaunchException($"OPC UA endpoint '{OpcUa.Endpoint}' is invalid.", "invalidOpcUaConfiguration");
                bindings.Add(new ProtocolPortBinding("opcua", endpoint.Port));
            }
            if (Modbus is not null) bindings.Add(new ProtocolPortBinding("modbus", Modbus.Port));
            return bindings;
        }
    }
}
