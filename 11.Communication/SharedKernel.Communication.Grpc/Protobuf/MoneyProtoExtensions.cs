using Google.Protobuf.WellKnownTypes;

namespace SharedKernel.Communication.Grpc.Protobuf;

/// <summary>
/// Pure static extension methods for converting between <c>decimal</c> and the
/// Protobuf well-known <c>Money</c> representation (units + nanos).
/// Zero intermediate object allocations. No reflection.
/// </summary>
/// <remarks>
/// Money is represented as: <c>units</c> (int64 integer part) and <c>nanos</c> (int32 fractional
/// part in billionths, -999_999_999 to 999_999_999). The sign of nanos must match units.
/// </remarks>
public static class MoneyProtoExtensions
{
    private const int NanosPerUnit = 1_000_000_000;

    /// <summary>
    /// Converts a Protobuf Money message to <see cref="decimal"/> without precision loss.
    /// </summary>
    public static decimal ToDecimal(this Google.Type.Money money)
    {
        // TODO: implement
        throw new NotImplementedException();
    }

    /// <summary>
    /// Converts a <see cref="decimal"/> value to a Protobuf Money message.
    /// </summary>
    /// <param name="value">The monetary value.</param>
    /// <param name="currencyCode">ISO 4217 currency code (e.g. <c>"USD"</c>).</param>
    public static Google.Type.Money ToMoneyProto(this decimal value, string currencyCode)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
