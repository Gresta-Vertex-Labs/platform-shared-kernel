using SharedKernel.Communication.Grpc.Protobuf;

namespace SharedKernel.Communication.Grpc.Tests.Protobuf;

public sealed class TimestampProtoExtensionsTests
{
    [Fact]
    public void ToTimestampProto_AndBack_PreservesUtcInstant()
    {
        // Arrange
        var original = new DateTimeOffset(2026, 6, 17, 12, 30, 45, 500, TimeSpan.Zero);

        // Act
        var timestamp = original.ToTimestampProto();
        var roundTripped = timestamp.ToDateTimeOffset();

        // Assert
        roundTripped.Should().Be(original, "UTC round-trip must preserve the instant");
        roundTripped.Offset.Should().Be(TimeSpan.Zero, "result must always be UTC");
    }

    [Fact]
    public void ToTimestampProto_WithNonUtcOffset_NormalizesToUtc()
    {
        // Arrange — +05:30 (IST)
        var original = new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.FromHours(5.5));

        // Act
        var timestamp = original.ToTimestampProto();
        var roundTripped = timestamp.ToDateTimeOffset();

        // Assert — instants must match; offset information is discarded (Timestamp is always UTC)
        roundTripped.UtcDateTime.Should().Be(original.UtcDateTime,
            "normalised UTC instant must match regardless of original offset");
        roundTripped.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ToDateTimeOffset_ReturnsUtc()
    {
        // Arrange
        var original = DateTimeOffset.UtcNow;
        var timestamp = original.ToTimestampProto();

        // Act
        var result = timestamp.ToDateTimeOffset();

        // Assert
        result.Offset.Should().Be(TimeSpan.Zero, "ToDateTimeOffset must always return UTC");
    }

    [Fact]
    public void ToTimestampProto_UnixEpoch_RoundTrips()
    {
        // Arrange
        var epoch = DateTimeOffset.UnixEpoch;

        // Act
        var timestamp = epoch.ToTimestampProto();
        var roundTripped = timestamp.ToDateTimeOffset();

        // Assert
        roundTripped.Should().Be(epoch);
        timestamp.Seconds.Should().Be(0);
        timestamp.Nanos.Should().Be(0);
    }

    [Fact]
    public void ToTimestampProto_MinDateTimeOffset_DoesNotThrow()
    {
        // Arrange — minimum valid Timestamp is 0001-01-01T00:00:00Z
        var minDate = DateTimeOffset.MinValue;

        // Act
        Action act = () => _ = minDate.ToTimestampProto();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ToTimestampProto_MaxDateTimeOffset_DoesNotThrow()
    {
        // Arrange
        var maxDate = DateTimeOffset.MaxValue;

        // Act
        Action act = () => _ = maxDate.ToTimestampProto();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RoundTrip_PreservesSubsecondPrecision()
    {
        // Arrange
        var original = new DateTimeOffset(2026, 6, 17, 0, 0, 0, 123, TimeSpan.Zero); // 123 ms

        // Act
        var timestamp = original.ToTimestampProto();
        var roundTripped = timestamp.ToDateTimeOffset();

        // Assert
        roundTripped.Millisecond.Should().Be(123, "subsecond precision must be preserved");
    }
}
