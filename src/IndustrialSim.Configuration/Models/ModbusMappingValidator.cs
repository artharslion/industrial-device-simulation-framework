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
            var kinds = new[] { ("coil", mapping.Coil), ("discrete", mapping.DiscreteInput), ("input", mapping.InputRegister), ("register", mapping.Register ?? mapping.HoldingRegister) }.Where(x => x.Item2.HasValue).ToArray();
            if (kinds.Length != 1) throw new ArgumentException($"Mapping '{name}' must define exactly one Modbus address kind.");
            var kind = kinds[0].Item1; var address = kinds[0].Item2!.Value;
            if (address < 0 || address > 65535) throw new ArgumentException($"Mapping '{name}' address {address} is outside 0..65535.");
            var type = mapping.Type?.ToLowerInvariant() ?? ((kind is "coil" or "discrete") ? "boolean" : "uint16");
            var width = type is "float32" or "int32" or "uint32" ? 2 : 1;
            if (kind is "coil" or "discrete" && type != "boolean") throw new ArgumentException($"Bit mapping '{name}' must use boolean type.");
            if (address + width > 65536) throw new ArgumentException($"Mapping '{name}' exceeds the Modbus address range.");
            result.Add(new(name, address, width, kind, type, mapping.Access, mapping.ByteOrder, mapping.WordOrder));
        }
        foreach (var left in result)
            foreach (var right in result.Where(item => item != left && item.Kind == left.Kind))
                if (left.Address < right.Address + right.Width && right.Address < left.Address + left.Width)
                    throw new ArgumentException($"Modbus mappings '{left.Name}' and '{right.Name}' overlap.");
        return result;
    }
}
