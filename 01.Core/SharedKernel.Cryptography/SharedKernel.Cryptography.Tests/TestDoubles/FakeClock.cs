using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Cryptography.Tests.TestDoubles;

internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
