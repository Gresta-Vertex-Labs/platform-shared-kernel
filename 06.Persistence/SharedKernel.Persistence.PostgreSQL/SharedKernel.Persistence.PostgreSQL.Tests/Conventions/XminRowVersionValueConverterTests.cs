using FluentAssertions;
using SharedKernel.Persistence.PostgreSQL.Conventions;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Conventions;

/// <summary>
/// WO-051/P-315 (T-62): <see cref="XminRowVersionValueConverter"/> round-trip unit tests — pure
/// BCL conversion logic, no live database required.
/// </summary>
public sealed class XminRowVersionValueConverterTests
{
    private readonly XminRowVersionValueConverter _converter = new();

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(12345u)]
    [InlineData(uint.MaxValue)]
    public void RoundTrips_UintValue_ThroughFourByteArray(uint value)
    {
        // model (byte[]) <- provider (uint)
        var fromProvider = (byte[])_converter.ConvertFromProvider!(value)!;
        fromProvider.Should().HaveCount(4, "xmin is always encoded as a 4-byte big-endian array");

        // provider (uint) <- model (byte[])
        var backToProvider = (uint)_converter.ConvertToProvider!(fromProvider)!;
        backToProvider.Should().Be(value, "the byte[]<->uint round trip must be lossless");
    }

    [Fact]
    public void ConvertFromProvider_Zero_ProducesAllZeroBytes()
    {
        var bytes = (byte[])_converter.ConvertFromProvider!(0u)!;
        bytes.Should().Equal(0, 0, 0, 0);
    }

    [Fact]
    public void ConvertFromProvider_MaxValue_ProducesAllOnesBytes()
    {
        var bytes = (byte[])_converter.ConvertFromProvider!(uint.MaxValue)!;
        bytes.Should().Equal(0xFF, 0xFF, 0xFF, 0xFF);
    }

    [Fact]
    public void ConvertToProvider_BigEndianEncoding_MatchesExpectedByteOrder()
    {
        // 0x01020304 big-endian == { 0x01, 0x02, 0x03, 0x04 }
        var value = (uint)_converter.ConvertToProvider!(new byte[] { 0x01, 0x02, 0x03, 0x04 })!;
        value.Should().Be(0x01020304u);
    }

    [Fact]
    public void ConvertToProvider_NonFourByteLength_ConvertsToZero_DoesNotThrow()
    {
        // A freshly-constructed aggregate's default/empty RowVersion (never sent to the server for
        // a value-generated OnAddOrUpdate column) must not throw — it converts to 0 instead.
        var emptyResult = (uint)_converter.ConvertToProvider!(Array.Empty<byte>())!;
        emptyResult.Should().Be(0u);

        var shortResult = (uint)_converter.ConvertToProvider!(new byte[] { 0x01, 0x02 })!;
        shortResult.Should().Be(0u);

        var longResult = (uint)_converter.ConvertToProvider!(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 })!;
        longResult.Should().Be(0u);
    }

    [Fact]
    public void ConvertFromProvider_AlwaysProducesWellFormedFourByteArray()
    {
        // Invariant: regardless of input, the model-side representation is always exactly 4 bytes.
        foreach (var value in new uint[] { 0u, 1u, 42u, 999_999u, uint.MaxValue })
        {
            var bytes = (byte[])_converter.ConvertFromProvider!(value)!;
            bytes.Should().HaveCount(4, $"value {value} must round-trip through a well-formed 4-byte array");
        }
    }
}
