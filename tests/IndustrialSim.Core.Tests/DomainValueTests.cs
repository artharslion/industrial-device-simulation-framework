using IndustrialSim.Core.Domain;

namespace IndustrialSim.Core.Tests;

public class DomainValueTests
{
    [Theory]
    [InlineData(DataType.Boolean, true)]
    [InlineData(DataType.Int8, -12)]
    [InlineData(DataType.Int16, -1234)]
    [InlineData(DataType.Int32, -123456)]
    [InlineData(DataType.Int64, -123456789L)]
    [InlineData(DataType.UInt8, 12)]
    [InlineData(DataType.UInt16, 1234)]
    [InlineData(DataType.UInt32, 123456)]
    [InlineData(DataType.UInt64, 123456789L)]
    [InlineData(DataType.Float, 12.5f)]
    [InlineData(DataType.Double, 12.5d)]
    [InlineData(DataType.String, "pump")]
    public void ScalarValue_accepts_supported_scalar_values(DataType type, object value)
    {
        var scalar = ScalarValue.Create(type, value);

        Assert.Equal(type, scalar.DataType);
        Assert.Equal(type switch
        {
            DataType.Int8 => (object)Convert.ToSByte(value),
            DataType.Int16 => Convert.ToInt16(value),
            DataType.Int32 => Convert.ToInt32(value),
            DataType.Int64 => Convert.ToInt64(value),
            DataType.UInt8 => Convert.ToByte(value),
            DataType.UInt16 => Convert.ToUInt16(value),
            DataType.UInt32 => Convert.ToUInt32(value),
            DataType.UInt64 => Convert.ToUInt64(value),
            DataType.Float => Convert.ToSingle(value),
            DataType.Double => Convert.ToDouble(value),
            _ => value
        }, scalar.Value);
    }

    [Theory]
    [InlineData(DataType.Int8, 128)]
    [InlineData(DataType.UInt8, -1)]
    [InlineData(DataType.Int32, "not-an-integer")]
    [InlineData(DataType.Boolean, 1)]
    [InlineData(DataType.String, 1)]
    public void ScalarValue_rejects_invalid_conversions(DataType type, object value)
    {
        Assert.Throws<ArgumentException>(() => ScalarValue.Create(type, value));
    }

    [Fact]
    public void Access_modes_are_complete()
    {
        Assert.Equal(new[] { DataPointAccess.Read, DataPointAccess.Write, DataPointAccess.ReadWrite },
            Enum.GetValues<DataPointAccess>());
    }

    [Fact]
    public void Simulation_time_is_value_based_and_supports_arithmetic()
    {
        var time = SimulationTime.FromSeconds(1.5) + TimeSpan.FromMilliseconds(500);

        Assert.Equal(TimeSpan.FromSeconds(2), time.Elapsed);
        Assert.True(time > SimulationTime.Zero);
        Assert.Equal(time, new SimulationTime(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Identifiers_reject_blank_values_and_compare_by_value()
    {
        Assert.Throws<ArgumentException>(() => new DeviceId(" "));
        Assert.Equal(new DeviceId("pump-001"), new DeviceId("pump-001"));
        Assert.Equal("pump-001", new DeviceId("pump-001").Value);
    }
}
