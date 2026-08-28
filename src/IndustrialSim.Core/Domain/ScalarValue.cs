using System.Globalization;

namespace IndustrialSim.Core.Domain;

public sealed record ScalarValue
{
    private ScalarValue(DataType dataType, object value)
    {
        DataType = dataType;
        Value = value;
    }

    public DataType DataType { get; }
    public object Value { get; }

    public static ScalarValue Create(DataType dataType, object? value)
    {
        if (value is null)
            throw new ArgumentException("A scalar value cannot be null.", nameof(value));

        try
        {
            object normalized = dataType switch
            {
                DataType.Boolean => value is bool boolean
                    ? boolean
                    : throw Invalid(dataType, value),
                DataType.String => value is string text
                    ? text
                    : throw Invalid(dataType, value),
                DataType.Int8 => ConvertNumeric<sbyte>(dataType, value),
                DataType.Int16 => ConvertNumeric<short>(dataType, value),
                DataType.Int32 => ConvertNumeric<int>(dataType, value),
                DataType.Int64 => ConvertNumeric<long>(dataType, value),
                DataType.UInt8 => ConvertNumeric<byte>(dataType, value),
                DataType.UInt16 => ConvertNumeric<ushort>(dataType, value),
                DataType.UInt32 => ConvertNumeric<uint>(dataType, value),
                DataType.UInt64 => ConvertNumeric<ulong>(dataType, value),
                DataType.Float => ConvertNumeric<float>(dataType, value),
                DataType.Double => ConvertNumeric<double>(dataType, value),
                _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unsupported data type.")
            };

            return new ScalarValue(dataType, normalized);
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException($"Value '{value}' is outside the range of {dataType}.", nameof(value), exception);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"Value '{value}' is not valid for {dataType}.", nameof(value), exception);
        }
    }

    public static bool TryCreate(DataType dataType, object? value, out ScalarValue? result)
    {
        try
        {
            result = Create(dataType, value);
            return true;
        }
        catch (ArgumentException)
        {
            result = null;
            return false;
        }
    }

    private static T ConvertNumeric<T>(DataType dataType, object value) where T : struct
    {
        if (value is bool || value is char || value is string && dataType is not (DataType.Float or DataType.Double))
            throw Invalid(dataType, value);

        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    private static ArgumentException Invalid(DataType dataType, object value) =>
        new($"Value of type {value.GetType().Name} is not valid for {dataType}.", nameof(value));
}
