using FluentAssertions;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.Tests.BuilderTests;

/// <summary>
/// T-15: <see cref="CircuitBreakerOptions"/> default values.
/// </summary>
/// <remarks>
/// Moved here from the Abstractions test project by P-560, together with the type itself: every
/// one of these knobs is a MassTransit middleware parameter, so it belongs in the transport
/// package rather than the transport-agnostic one.
/// </remarks>
public sealed class CircuitBreakerOptionsTests
{
    // -------------------------------------------------------------------------
    // T-15: CircuitBreakerOptions defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void CircuitBreakerOptions_DefaultTripThreshold_IsFive()
    {
        var opts = new CircuitBreakerOptions();
        opts.TripThreshold.Should().Be(5);
    }

    [Fact]
    public void CircuitBreakerOptions_DefaultActiveThreshold_IsTen()
    {
        var opts = new CircuitBreakerOptions();
        opts.ActiveThreshold.Should().Be(10);
    }

    [Fact]
    public void CircuitBreakerOptions_DefaultResetInterval_IsSixtySeconds()
    {
        var opts = new CircuitBreakerOptions();
        opts.ResetInterval.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void CircuitBreakerOptions_DefaultTrackingPeriod_IsSixtySeconds()
    {
        var opts = new CircuitBreakerOptions();
        opts.TrackingPeriod.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void CircuitBreakerOptions_IsSealed()
    {
        typeof(CircuitBreakerOptions).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void CircuitBreakerOptions_SectionName_IsExpectedValue()
    {
        CircuitBreakerOptions.SectionName.Should().Be("SharedKernel:Messaging:CircuitBreaker");
    }

    [Fact]
    public void CircuitBreakerOptions_PropertiesAreMutable()
    {
        var opts = new CircuitBreakerOptions
        {
            TripThreshold = 10,
            ActiveThreshold = 20,
            ResetInterval = TimeSpan.FromSeconds(30),
            TrackingPeriod = TimeSpan.FromSeconds(120)
        };

        opts.TripThreshold.Should().Be(10);
        opts.ActiveThreshold.Should().Be(20);
        opts.ResetInterval.Should().Be(TimeSpan.FromSeconds(30));
        opts.TrackingPeriod.Should().Be(TimeSpan.FromSeconds(120));
    }
}
