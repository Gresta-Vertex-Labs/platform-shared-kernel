using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Primitives.Tests.Clocks;

/// <summary>
/// Covers <see cref="SystemClock"/>'s <see cref="TimeProvider"/>-backed internals (P-295/WO-049):
/// both constructors, and that <see cref="IClock.Today"/> derives from the same
/// <see cref="TimeProvider"/>-sourced instant as <see cref="IClock.UtcNow"/> in both cases.
/// </summary>
public sealed class SystemClockTimeProviderTests
{
    [Fact]
    public void ParameterlessConstructor_UsesSystemTimeProvider_ReturnsCurrentWallClockTime()
    {
        var before = TimeProvider.System.GetUtcNow();
        var clock = new SystemClock();
        var now = clock.UtcNow;
        var after = TimeProvider.System.GetUtcNow();

        Assert.True(now >= before && now <= after);
    }

    [Fact]
    public void ParameterlessConstructor_Today_MatchesSystemTimeProviderDate()
    {
        var clock = new SystemClock();
        var expected = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);

        Assert.Equal(expected, clock.Today);
    }

    [Fact]
    public void TimeProviderConstructor_UtcNow_ReflectsInjectedProvidersInstant()
    {
        var fixedInstant = new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.Zero);
        var fakeProvider = new FakeTimeProvider(fixedInstant);
        IClock clock = new SystemClock(fakeProvider);

        Assert.Equal(fixedInstant, clock.UtcNow);
    }

    [Fact]
    public void TimeProviderConstructor_UtcNow_ReflectsProviderAfterItAdvances()
    {
        var start = new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.Zero);
        var fakeProvider = new FakeTimeProvider(start);
        IClock clock = new SystemClock(fakeProvider);

        Assert.Equal(start, clock.UtcNow);

        fakeProvider.Advance(TimeSpan.FromHours(5));

        Assert.Equal(start + TimeSpan.FromHours(5), clock.UtcNow);
    }

    [Fact]
    public void TimeProviderConstructor_Today_DerivesFromSameProviderSourcedInstant()
    {
        var fixedInstant = new DateTimeOffset(2026, 3, 1, 23, 45, 0, TimeSpan.Zero);
        var fakeProvider = new FakeTimeProvider(fixedInstant);
        IClock clock = new SystemClock(fakeProvider);

        Assert.Equal(DateOnly.FromDateTime(fixedInstant.UtcDateTime), clock.Today);

        // Advance across a UTC day boundary and confirm Today tracks the same provider-sourced instant.
        fakeProvider.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(new DateOnly(2026, 3, 2), clock.Today);
        Assert.Equal(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), clock.Today);
    }

    [Fact]
    public void TimeProviderConstructor_NullTimeProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemClock(null!));
    }

    // Minimal hand-rolled TimeProvider fake — avoids adding
    // Microsoft.Extensions.TimeProvider.Testing as a new test-project NuGet dependency for what is
    // just one overridden method plus a mutable "now" field.
    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
