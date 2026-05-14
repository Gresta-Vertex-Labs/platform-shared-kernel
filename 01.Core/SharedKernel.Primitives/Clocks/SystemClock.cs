namespace SharedKernel.Primitives.Clocks;

/// <summary>
/// Production implementation of <see cref="IClock"/> that delegates to
/// <see cref="DateTimeOffset.UtcNow"/>. Register as a singleton.
/// </summary>
/// <remarks>
/// This class holds no mutable state. Every property access reads directly from the system clock.
/// </remarks>
public sealed class SystemClock : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
}
