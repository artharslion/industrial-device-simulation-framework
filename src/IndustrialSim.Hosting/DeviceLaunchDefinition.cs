using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.OpcUa;

namespace IndustrialSim.Hosting;

public sealed class DeviceLaunchException(string message, string errorCode) : ArgumentException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed record ProtocolPortBinding(string Protocol, int Port, string? ListenerKey = null);

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
                OpcUaEndpointDescriptor endpoint;
                try { endpoint = OpcUaEndpointDescriptor.Parse(OpcUa.Endpoint); }
                catch (ArgumentException exception)
                {
                    throw new DeviceLaunchException(exception.Message, "invalidOpcUaConfiguration");
                }
                bindings.Add(new ProtocolPortBinding("opcua", endpoint.Port, endpoint.Endpoint));
            }
            if (Modbus is not null) bindings.Add(new ProtocolPortBinding("modbus", Modbus.Port));
            return bindings;
        }
    }
}
