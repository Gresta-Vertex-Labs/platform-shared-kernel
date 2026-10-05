using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Primitives.Tests.Clocks;

/// <summary>
/// Covers <see cref="ClockExtensions.AddClock"/>'s <c>TryAddSingleton</c>-based registration
/// (SK.01.P518) — a consumer-supplied <see cref="IClock"/> registration made before this call
/// must always win over the platform default, and calling it twice must never double-register.
/// </summary>
public sealed class ClockExtensionsTests
{
    [Fact]
    public void AddClock_ConsumerFakeRegisteredFirst_WinsOverPlatformDefault()
    {
        var services = new ServiceCollection();
        var fake = new FixedClock(DateTimeOffset.UnixEpoch);

        services.AddSingleton<IClock>(fake);
        services.AddClock();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(fake, provider.GetRequiredService<IClock>());
    }

    [Fact]
    public void AddClock_CalledTwice_RegistersSystemClockExactlyOnce()
    {
        var services = new ServiceCollection();

        services.AddClock();
        services.AddClock();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IClock>());
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly Today => DateOnly.FromDateTime(now.UtcDateTime);
    }
}
