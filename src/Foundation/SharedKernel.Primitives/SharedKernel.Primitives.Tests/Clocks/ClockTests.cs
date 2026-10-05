using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Primitives.Tests.Clocks;

public sealed class ClockTests
{
    [Fact]
    public void SystemClock_UtcNow_IsCloseToCurrentTime()
    {
        var clock = new SystemClock();
        var before = DateTimeOffset.UtcNow;
        var now = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.True(now >= before && now <= after);
    }

    [Fact]
    public void SystemClock_Today_MatchesUtcDate()
    {
        var clock = new SystemClock();
        var expected = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        Assert.Equal(expected, clock.Today);
    }

    [Fact]
    public void FakeClock_ReturnsFixedTime()
    {
        var fixed_ = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        IClock clock = new FakeClock(fixed_);

        Assert.Equal(fixed_, clock.UtcNow);
        Assert.Equal(DateOnly.FromDateTime(fixed_.DateTime), clock.Today);
    }

    // ---- AddClock() DI extension ----

    [Fact]
    public void AddClock_RegistersIClock_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddClock();

        var sp = services.BuildServiceProvider();
        var clock = sp.GetRequiredService<IClock>();
        Assert.IsType<SystemClock>(clock);
    }

    [Fact]
    public void AddClock_ReturnsSameInstanceForSingleton()
    {
        var services = new ServiceCollection();
        services.AddClock();

        var sp = services.BuildServiceProvider();
        var a = sp.GetRequiredService<IClock>();
        var b = sp.GetRequiredService<IClock>();
        Assert.Same(a, b);
    }

    // Minimal fake for testing — demonstrates substitutability of IClock
    private sealed class FakeClock(DateTimeOffset fixedTime) : IClock
    {
        public DateTimeOffset UtcNow => fixedTime;
        public DateOnly Today => DateOnly.FromDateTime(fixedTime.DateTime);
    }
}
