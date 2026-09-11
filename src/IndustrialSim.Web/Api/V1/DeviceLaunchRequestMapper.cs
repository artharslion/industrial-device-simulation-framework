using System.Text.Json;
using IndustrialSim.Configuration;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;

namespace IndustrialSim.Web.Api.V1;

internal static class DeviceLaunchRequestMapper
{
    public static DeviceDefinition ToDefinition(CreateDeviceRequest request) => new(
        new DeviceId(request.Id),
        request.Type,
        request.DataPoints.Select(ToDefinition),
        request.Commands?.Select(name => new CommandDefinition(name)),
        request.Events?.Select(name => new EventDefinition(name)),
        request.Behavior is null ? null : new DeviceBehaviorDefinition(request.Behavior.Profile, request.Behavior.Parameters));

    public static DeviceLaunchDefinition ToLaunch(CreateDeviceRequest request, DeviceLaunchSource? source = null)
    {
        var definition = ToDefinition(request);
        var (opcUa, modbus) = Protocols(definition, request.Protocols, request.PortBindings);
        return new DeviceLaunchDefinition(definition, new SimulationHostOptions(request.Deterministic, request.Seed), opcUa, modbus, source ?? new DeviceLaunchSource("quickCreate"));
    }

    public static (OpcUaLaunchDefinition? OpcUa, ModbusLaunchDefinition? Modbus) Protocols(
        DeviceDefinition definition,
        DeviceProtocolsRequest? protocols,
        IReadOnlyList<CreatePortBindingRequest>? legacyBindings)
    {
        if (protocols is not null && legacyBindings is { Count: > 0 })
            throw new DeviceLaunchException("Use either 'protocols' or deprecated 'portBindings', not both.", "protocolConfigurationAmbiguous");

        if (protocols is null)
        {
            OpcUaLaunchDefinition? legacyOpcUa = null;
            foreach (var binding in legacyBindings ?? [])
            {
                if (binding.Protocol.Equals("opcua", StringComparison.OrdinalIgnoreCase))
                    legacyOpcUa = new OpcUaLaunchDefinition($"opc.tcp://0.0.0.0:{binding.Port}");
                else if (binding.Protocol.Equals("modbus", StringComparison.OrdinalIgnoreCase))
                    throw new DeviceLaunchException("Modbus requires explicit mappings; a port binding alone is not a protocol configuration.", "modbusMappingRequired");
                else
                    throw new DeviceLaunchException($"Protocol '{binding.Protocol}' is not supported.", "unknownProtocol");
            }
            return (legacyOpcUa, null);
        }

        OpcUaLaunchDefinition? opcUa = null;
        if (protocols.Opcua is { Enabled: true } opc)
        {
            var endpoint = opc.Endpoint;
            if (string.IsNullOrWhiteSpace(endpoint)) endpoint = $"opc.tcp://0.0.0.0:{opc.Port ?? 4840}";
            opcUa = new OpcUaLaunchDefinition(endpoint);
        }

        ModbusLaunchDefinition? modbus = null;
        if (protocols.Modbus is { Enabled: true } mod)
        {
            if (mod.Mappings is not { Count: > 0 })
                throw new DeviceLaunchException("Enabled Modbus requires at least one explicit mapping.", "modbusMappingRequired");
            var configuration = new ModbusConfiguration
            {
                Enabled = true,
                Port = mod.Port,
                Mappings = mod.Mappings.ToDictionary(mapping => mapping.DataPoint, ToConfiguration, StringComparer.OrdinalIgnoreCase)
            };
            IReadOnlyList<ValidatedModbusMapping> mappings;
            try
            {
                mappings = ModbusMappingValidator.Validate(configuration);
                YamlConfigurationLoader.ValidateMappings(definition, mappings);
            }
            catch (ArgumentException exception)
            {
                throw new DeviceLaunchException(exception.Message, "invalidModbusMapping");
            }
            modbus = new ModbusLaunchDefinition(mod.Port, mappings);
        }
        return (opcUa, modbus);
    }

    private static ModbusMappingConfiguration ToConfiguration(ModbusMappingRequest mapping)
    {
        var result = new ModbusMappingConfiguration
        {
            Type = mapping.DataType,
            Access = mapping.Access,
            ByteOrder = mapping.ByteOrder,
            WordOrder = mapping.WordOrder
        };
        switch (mapping.Kind.Trim().ToLowerInvariant())
        {
            case "coil": result.Coil = mapping.Address; break;
            case "discrete": result.DiscreteInput = mapping.Address; break;
            case "input": result.InputRegister = mapping.Address; break;
            case "holding":
            case "register": result.HoldingRegister = mapping.Address; break;
            default: throw new DeviceLaunchException($"Modbus mapping '{mapping.DataPoint}' has unknown kind '{mapping.Kind}'.", "invalidModbusMapping");
        }
        return result;
    }

    private static DataPointDefinition ToDefinition(CreateDataPointRequest request)
    {
        if (!Enum.TryParse<DataType>(request.DataType, true, out var dataType)) throw new DeviceLaunchException($"Unknown data type '{request.DataType}'.", "invalidDevice");
        if (!Enum.TryParse<DataPointAccess>(request.Access, true, out var access)) throw new DeviceLaunchException($"Unknown access mode '{request.Access}'.", "invalidDevice");
        return new DataPointDefinition(request.Name, dataType, access, request.Initial is { } initial ? JsonValue(initial) : null, request.Unit, request.Description);
    }

    private static object? JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null => null,
        _ => throw new DeviceLaunchException("State values must be scalar JSON values.", "invalidDevice")
    };
}
