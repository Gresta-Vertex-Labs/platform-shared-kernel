using FluentAssertions;
using SharedKernel.Messaging.Abstractions.Faults;

namespace SharedKernel.Messaging.Abstractions.Tests;

/// <summary>
/// T-15: CircuitBreakerOptions default values test.
/// T-16: FaultExceptionInfo record equality test.
/// </summary>
public sealed class FaultTypesTests
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

    // -------------------------------------------------------------------------
    // T-16: FaultExceptionInfo record equality
    // -------------------------------------------------------------------------

    [Fact]
    public void FaultExceptionInfo_SameValues_AreEqual()
    {
        var a = new FaultExceptionInfo("System.InvalidOperationException", "Something went wrong");
        var b = new FaultExceptionInfo("System.InvalidOperationException", "Something went wrong");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void FaultExceptionInfo_DifferentExceptionType_AreNotEqual()
    {
        var a = new FaultExceptionInfo("System.InvalidOperationException", "Message");
        var b = new FaultExceptionInfo("System.ArgumentException", "Message");

        a.Should().NotBe(b);
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void FaultExceptionInfo_DifferentMessage_AreNotEqual()
    {
        var a = new FaultExceptionInfo("System.InvalidOperationException", "Message A");
        var b = new FaultExceptionInfo("System.InvalidOperationException", "Message B");

        a.Should().NotBe(b);
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void FaultExceptionInfo_WithExpression_ProducesNewInstance()
    {
        var original = new FaultExceptionInfo("System.InvalidOperationException", "Original message");
        var modified = original with { Message = "Updated message" };

        modified.Should().NotBeSameAs(original);
        modified.ExceptionType.Should().Be(original.ExceptionType);
        modified.Message.Should().Be("Updated message");
        original.Message.Should().Be("Original message", "original must be unchanged");
    }

    [Fact]
    public void FaultExceptionInfo_IsSealed()
    {
        typeof(FaultExceptionInfo).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void FaultExceptionInfo_IsRecord()
    {
        // Records expose a synthetic Clone method; use Equals + GetHashCode consistency.
        var a = new FaultExceptionInfo("Ex", "Msg");
        var b = new FaultExceptionInfo("Ex", "Msg");

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void FaultExceptionInfo_ExceptionType_Accessible()
    {
        var info = new FaultExceptionInfo("System.Exception", "test");
        info.ExceptionType.Should().Be("System.Exception");
    }

    [Fact]
    public void FaultExceptionInfo_Message_Accessible()
    {
        var info = new FaultExceptionInfo("System.Exception", "test message");
        info.Message.Should().Be("test message");
    }
}
