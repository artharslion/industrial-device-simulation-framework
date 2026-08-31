namespace IndustrialSim.Configuration.Models;

public sealed record ValidatedModbusMapping(string Name, int Address, int Width, string Kind, string DataType, string? Access, string? ByteOrder, string? WordOrder);

public static class ModbusMappingValidator
{
    public static IReadOnlyList<ValidatedModbusMapping> Validate(ModbusConfiguration configuration)
    {
        var mappings = configuration.Mappings ?? new Dictionary<string, ModbusMappingConfiguration>();
        var result = new List<ValidatedModbusMapping>();
        foreach (var (name, mapping) in mappings)
        {
            var hasRegister = mapping.Register.HasValue;
            var hasCoil = mapping.Coil.HasValue;
            if (hasRegister == hasCoil) throw new ArgumentException($"Mapping '{name}' must define exactly one register or coil.");
            var address = hasRegister ? mapping.Register!.Value : mapping.Coil!.Value;
            if (address < 0 || address > 65535) throw new ArgumentException($"Mapping '{name}' address {address} is outside 0..65535.");
            var type = mapping.Type?.ToLowerInvariant() ?? (hasCoil ? "boolean" : "uint16");
            var width = type is "float32" or "int32" or "uint32" ? 2 : 1;
            if (hasCoil && type != "boolean") throw new ArgumentException($"Coil mapping '{name}' must use boolean type.");
            if (address + width > 65536) throw new ArgumentException($"Mapping '{name}' exceeds the Modbus address range.");
            result.Add(new(name, address, width, hasCoil ? "coil" : "register", type, mapping.Access, mapping.ByteOrder, mapping.WordOrder));
        }
        foreach (var left in result)
            foreach (var right in result.Where(item => item != left && item.Kind == left.Kind))
                if (left.Address < right.Address + right.Width && right.Address < left.Address + left.Width)
                    throw new ArgumentException($"Modbus mappings '{left.Name}' and '{right.Name}' overlap.");
        return result;
    }
}
