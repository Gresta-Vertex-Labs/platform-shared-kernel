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
}
