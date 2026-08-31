namespace IndustrialSim.Configuration.Models;

public sealed class RootConfiguration
{
    public DeviceConfiguration? Device { get; set; }
    public ProtocolsConfiguration? Protocols { get; set; }
    public WebConfiguration? Web { get; set; }
}

public sealed class DeviceConfiguration
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, DataPointConfiguration>? Datapoints { get; set; }
    public Dictionary<string, object?>? Commands { get; set; }
}

public sealed class DataPointConfiguration
{
    public string? Type { get; set; }
    public string? Unit { get; set; }
    public string? Description { get; set; }
    public object? Initial { get; set; }
    public string? Access { get; set; }
}

public sealed class ProtocolsConfiguration
{
    public OpcUaConfiguration? Opcua { get; set; }
    public ModbusConfiguration? Modbus { get; set; }
}

public sealed class OpcUaConfiguration { public bool Enabled { get; set; } public string? Endpoint { get; set; } }
public sealed class WebConfiguration { public bool Enabled { get; set; } public int Port { get; set; } = 8080; }
public sealed class ModbusConfiguration
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 5020;
    public Dictionary<string, ModbusMappingConfiguration>? Mappings { get; set; }
}
public sealed class ModbusMappingConfiguration
{
    public int? Register { get; set; }
    public int? InputRegister { get; set; }
    public int? HoldingRegister { get; set; }
    public int? Coil { get; set; }
    public int? DiscreteInput { get; set; }
    public string? Type { get; set; }
    public string? Access { get; set; }
    public string? ByteOrder { get; set; }
    public string? WordOrder { get; set; }
}
