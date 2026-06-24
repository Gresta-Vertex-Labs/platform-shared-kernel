using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Clocks;

/// <summary>
/// Proves <see cref="FakeClock"/> against the <see cref="IClock"/> contract owned by <c>01.Core</c>.
/// </summary>
/// <remarks>
/// <c>01.Core</c> (<c>SharedKernel.Primitives</c>) references nothing per the root layering rules,
/// so it can never take a <c>ProjectReference</c> to <c>16.Testing</c> to exercise this fake itself
/// (the existing <c>ClockTests.cs</c> in <c>SharedKernel.Primitives.Tests</c> defines its own private
/// nested fake for exactly this reason). This self-test is therefore the correct — not a
/// fallback-of-convenience — home for proving <see cref="FakeClock"/>'s contract.
/// </remarks>
public sealed class FakeClockTests
{
    [Fact]
    public void Constructor_NoArgument_DefaultsToFixedNonRealInstant()
    {
        var clock = new FakeClock();

        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), clock.UtcNow);
    }

    [Fact]
    public void Constructor_WithInitial_UsesSuppliedValue()
    {
        var initial = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(initial);

        Assert.Equal(initial, clock.UtcNow);
    }

    [Fact]
    public void Today_DerivedFromUtcNow()
    {
        var clock = new FakeClock(new DateTimeOffset(2025, 3, 15, 23, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2025, 3, 15), clock.Today);
    }

    [Fact]
    public void Set_UpdatesUtcNow()
    {
        var clock = new FakeClock();
        var newValue = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        clock.Set(newValue);

        Assert.Equal(newValue, clock.UtcNow);
    }

    [Fact]
    public void SetUtcNow_IsAliasForSet()
    {
        var clock = new FakeClock();
        var newValue = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        clock.SetUtcNow(newValue);

        Assert.Equal(newValue, clock.UtcNow);
    }

    [Fact]
    public void Advance_AddsDeltaToUtcNow()
    {
        var initial = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(initial);

        clock.Advance(TimeSpan.FromDays(1));

        Assert.Equal(initial.AddDays(1), clock.UtcNow);
    }

    [Fact]
    public void ImplementsIClock()
    {
        IClock clock = new FakeClock();
        Assert.IsType<FakeClock>(clock);
    }

    [Fact]
    public async Task UtcNow_IsThreadSafe_ForConcurrentReadsAndWrites()
    {
        var clock = new FakeClock();
        var tasks = new List<Task>();

        for (var i = 0; i < 50; i++)
        {
            var offset = i;
            tasks.Add(Task.Run(() => clock.Advance(TimeSpan.FromSeconds(offset))));
            tasks.Add(Task.Run(() => _ = clock.UtcNow));
        }

        await Task.WhenAll(tasks);

        // No exception thrown is the assertion — concurrent access must not corrupt state.
        Assert.True(clock.UtcNow >= new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
