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

    [Fact]
    public void WellKnownBaggageKeys_TenantId_EqualsExpectedLiteral()
    {
        // MUST stay "TenantId". BaggageLogRecordProcessor copies Activity baggage generically
        // rather than by known key, so this string becomes the emitted log property name verbatim.
        // Changing it to "tenant.id" for symmetry with WellKnownTagKeys.TenantId would silently
        // rename a field that deployed dashboards, saved searches, and alert rules filter on --
        // an operational breaking change, not a tidy-up. It also matches the literal
        // SharedKernel.MultiTenancy's own TenantBaggageKeys.TenantId already writes, which is what
        // makes re-pointing that constant at this one a behaviour-identical refactor.
        Assert.Equal("TenantId", WellKnownBaggageKeys.TenantId);
    }

    [Fact]
    public void WellKnownBaggageKeys_TenantId_DeliberatelyDiffersFromTheTagKey()
    {
        // Pins the asymmetry as intentional. If someone "fixes" it, this test explains why not.
        Assert.NotEqual(WellKnownTagKeys.TenantId, WellKnownBaggageKeys.TenantId);
    }

    [Fact]
    public void WellKnownBaggageKeys_CorrelationId_MatchesTheTagKey()
    {
        // Correlation id, unlike tenant id, uses the same literal on both sides -- and that
        // agreement is the whole reason this registry exists, so it is worth pinning.
        Assert.Equal(WellKnownTagKeys.CorrelationId, WellKnownBaggageKeys.CorrelationId);
    }

    [Fact]
    public void WellKnownBaggageKeys_AreDistinct()
    {
        var keys = new[] { WellKnownBaggageKeys.CorrelationId, WellKnownBaggageKeys.TenantId };

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }
}
