namespace IndustrialSim.Configuration.Models;

public sealed record ValidatedModbusMapping(string Name, int Address, int Width, string Kind, string DataType, string? Access, string? ByteOrder, string? WordOrder);

public static class ModbusMappingValidator
{
    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64", "float", "float32", "double"
    };

    public static IReadOnlyList<ValidatedModbusMapping> Validate(ModbusConfiguration configuration)
    {
        var mappings = configuration.Mappings ?? new Dictionary<string, ModbusMappingConfiguration>();
        var result = new List<ValidatedModbusMapping>();
        foreach (var (name, mapping) in mappings)
        {
            if (mapping.Register.HasValue && mapping.HoldingRegister.HasValue)
                throw new ArgumentException($"Mapping '{name}' cannot define both 'register' and 'holdingRegister'.");
            var kinds = new[] { ("coil", mapping.Coil), ("discrete", mapping.DiscreteInput), ("input", mapping.InputRegister), ("register", mapping.Register), ("register", mapping.HoldingRegister) }.Where(x => x.Item2.HasValue).ToArray();
            if (kinds.Length != 1) throw new ArgumentException($"Mapping '{name}' must define exactly one Modbus address kind.");
            var kind = kinds[0].Item1; var address = kinds[0].Item2!.Value;
            if (address < 0 || address > 65535) throw new ArgumentException($"Mapping '{name}' address {address} is outside 0..65535.");
            var type = mapping.Type?.Trim().ToLowerInvariant() ?? ((kind is "coil" or "discrete") ? "boolean" : "uint16");
            var access = mapping.Access?.Trim().ToLowerInvariant();
            var byteOrder = mapping.ByteOrder?.Trim().ToLowerInvariant();
            var wordOrder = mapping.WordOrder?.Trim().ToLowerInvariant();
            var width = type switch
            {
                "int8" or "uint8" or "int16" or "uint16" => 1,
                "int32" or "uint32" or "float" or "float32" => 2,
                "int64" or "uint64" or "double" => 4,
                "boolean" when kind is "coil" or "discrete" => 1,
                _ => throw new ArgumentException($"Mapping '{name}' has unsupported Modbus type '{type}'.")
            };
            if (kind is "coil" or "discrete" && type != "boolean") throw new ArgumentException($"Bit mapping '{name}' must use boolean type.");
            if (kind is "input" or "register" && !NumericTypes.Contains(type)) throw new ArgumentException($"Register mapping '{name}' must use a numeric type.");
            if (access is not null && access is not ("read" or "write" or "readwrite")) throw new ArgumentException($"Mapping '{name}' has invalid access '{mapping.Access}'.");
            if (kind is "input" or "discrete" && access is "write" or "readwrite") throw new ArgumentException($"Mapping '{name}' uses read-only Modbus address kind '{kind}'.");
            if (byteOrder is not null && byteOrder is not ("big" or "little")) throw new ArgumentException($"Mapping '{name}' has invalid byte order '{mapping.ByteOrder}'.");
            if (wordOrder is not null && wordOrder is not ("big" or "little")) throw new ArgumentException($"Mapping '{name}' has invalid word order '{mapping.WordOrder}'.");
            if (kind is "coil" or "discrete" && (byteOrder is not null || wordOrder is not null)) throw new ArgumentException($"Bit mapping '{name}' cannot configure byte or word order.");
            if (address + width > 65536) throw new ArgumentException($"Mapping '{name}' exceeds the Modbus address range.");
            result.Add(new(name, address, width, kind, type, access, byteOrder, wordOrder));
        }
        foreach (var left in result)
            foreach (var right in result.Where(item => item != left && item.Kind == left.Kind))
                if (left.Address < right.Address + right.Width && right.Address < left.Address + left.Width)
                    throw new ArgumentException($"Modbus mappings '{left.Name}' and '{right.Name}' overlap.");
        return result;
    }
}
