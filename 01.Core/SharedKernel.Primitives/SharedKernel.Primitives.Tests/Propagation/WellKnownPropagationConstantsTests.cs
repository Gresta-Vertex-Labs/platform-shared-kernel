using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.Primitives.Tests.Propagation;

public sealed class WellKnownPropagationConstantsTests
{
    // T-34: pin every literal value so a future edit cannot silently drift a cross-service
    // propagation identifier.

    [Fact]
    public void WellKnownHeaders_CorrelationId_EqualsExpectedLiteral()
    {
        Assert.Equal("X-Correlation-Id", WellKnownHeaders.CorrelationId);
    }

    [Fact]
    public void WellKnownHeaders_TenantId_EqualsExpectedLiteral()
    {
        Assert.Equal("X-Tenant-Id", WellKnownHeaders.TenantId);
    }

    [Fact]
    public void WellKnownBaggageKeys_CorrelationId_EqualsExpectedLiteral()
    {
        Assert.Equal("correlation.id", WellKnownBaggageKeys.CorrelationId);
    }

    // T-38: pin every WellKnownTagKeys literal value so a future edit cannot silently drift a
    // cross-service OpenTelemetry span-attribute key.

    [Fact]
    public void WellKnownTagKeys_TenantId_EqualsExpectedLiteral()
    {
        Assert.Equal("tenant.id", WellKnownTagKeys.TenantId);
    }

    [Fact]
    public void WellKnownTagKeys_CorrelationId_EqualsExpectedLiteral()
    {
        Assert.Equal("correlation.id", WellKnownTagKeys.CorrelationId);
    }

    [Fact]
    public void WellKnownTagKeys_ErrorType_EqualsExpectedLiteral()
    {
        Assert.Equal("error.type", WellKnownTagKeys.ErrorType);
    }

    [Fact]
    public void WellKnownTagKeys_ErrorCode_EqualsExpectedLiteral()
    {
        Assert.Equal("error.code", WellKnownTagKeys.ErrorCode);
    }
}
