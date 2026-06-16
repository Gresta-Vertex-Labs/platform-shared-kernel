using Google.Protobuf.WellKnownTypes;

namespace SharedKernel.Communication.Grpc.Protobuf;

/// <summary>
/// Pure static extension methods for converting between <see cref="DateTimeOffset"/> and the
/// Protobuf well-known <see cref="Timestamp"/> type.
/// Allocation-minimal — uses direct arithmetic rather than intermediate string representations.
/// UTC is always preserved; offset information is discarded on round-trip (Timestamp is always UTC).
/// </summary>
public static class TimestampProtoExtensions
{
    /// <summary>
    /// Converts a Protobuf <see cref="Timestamp"/> to <see cref="DateTimeOffset"/> (UTC).
    /// </summary>
    public static DateTimeOffset ToDateTimeOffset(this Timestamp timestamp)
    {
        // TODO: implement
        throw new NotImplementedException();
    }

    /// <summary>
    /// Converts a <see cref="DateTimeOffset"/> to a Protobuf <see cref="Timestamp"/>.
    /// The offset is normalised to UTC before conversion.
    /// </summary>
    public static Timestamp ToTimestampProto(this DateTimeOffset dateTimeOffset)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
