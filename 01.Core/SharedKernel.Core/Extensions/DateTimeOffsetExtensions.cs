namespace SharedKernel.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="DateTimeOffset"/>.
/// </summary>
public static class DateTimeOffsetExtensions
{
    private static readonly DateTimeOffset _unixEpoch = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Returns the number of milliseconds elapsed since the Unix epoch (1970-01-01T00:00:00Z).
    /// </summary>
    /// <param name="value">The date-time value to convert.</param>
    /// <returns>Milliseconds since the Unix epoch as a <see cref="long"/>.</returns>
    public static long ToUnixMilliseconds(this DateTimeOffset value)
        => (long)(value.ToUniversalTime() - _unixEpoch).TotalMilliseconds;

    /// <summary>
    /// Returns a <see cref="DateTimeOffset"/> representing the start of the day (midnight, 00:00:00.000)
    /// in the same time zone as <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The source date-time value.</param>
    public static DateTimeOffset StartOfDay(this DateTimeOffset value)
        => new(value.Year, value.Month, value.Day, 0, 0, 0, value.Offset);

    /// <summary>
    /// Returns a <see cref="DateTimeOffset"/> representing the last moment of the day (23:59:59.999)
    /// in the same time zone as <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The source date-time value.</param>
    public static DateTimeOffset EndOfDay(this DateTimeOffset value)
        => new(value.Year, value.Month, value.Day, 23, 59, 59, 999, value.Offset);
}
