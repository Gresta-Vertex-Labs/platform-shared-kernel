using Google.Protobuf.WellKnownTypes;

namespace SharedKernel.Communication.Grpc.Protobuf;

/// <summary>
/// Pure static extension methods for converting between <see cref="DateTimeOffset"/> and the
/// Protobuf well-known <see cref="Timestamp"/> type.
/// Allocation-minimal — delegates to Protobuf's own static helpers which use direct arithmetic.
/// UTC is always preserved; offset information is discarded on round-trip (Timestamp is always UTC).
/// </summary>
public static class TimestampProtoExtensions
{
    /// <summary>
    /// Converts a Protobuf <see cref="Timestamp"/> to <see cref="DateTimeOffset"/> (UTC).
    /// </summary>
    /// <param name="timestamp">The Protobuf Timestamp to convert.</param>
    /// <returns>A <see cref="DateTimeOffset"/> in UTC representing the same instant.</returns>
    public static DateTimeOffset ToDateTimeOffset(this Timestamp timestamp)
    {
        // Timestamp.ToDateTimeOffset() preserves UTC and uses direct ticks arithmetic internally.
        return timestamp.ToDateTimeOffset();
    }

    /// <summary>
    /// Converts a <see cref="DateTimeOffset"/> to a Protobuf <see cref="Timestamp"/>.
    /// The offset is normalised to UTC before conversion.
    /// </summary>
    /// <param name="dateTimeOffset">The <see cref="DateTimeOffset"/> to convert.</param>
    /// <returns>A Protobuf <see cref="Timestamp"/> representing the same instant in UTC.</returns>
    public static Timestamp ToTimestampProto(this DateTimeOffset dateTimeOffset)
    {
        // Timestamp.FromDateTimeOffset() normalises to UTC and uses direct ticks arithmetic.
        return Timestamp.FromDateTimeOffset(dateTimeOffset);
    }
}
