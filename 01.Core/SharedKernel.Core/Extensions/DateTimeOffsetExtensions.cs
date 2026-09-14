namespace SharedKernel.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="DateTimeOffset"/>.
/// </summary>
/// <remarks>
/// For Unix time use the BCL's <see cref="DateTimeOffset.ToUnixTimeMilliseconds"/> and
/// <see cref="DateTimeOffset.FromUnixTimeMilliseconds(long)"/>.
/// </remarks>
public static class DateTimeOffsetExtensions
{
    /// <summary>Returns midnight at the start of the value's calendar day, keeping its offset.</summary>
    /// <remarks>
    /// <para>
    /// For a whole-day range, use a half-open interval: <c>start &lt;= x &amp;&amp; x &lt; start.AddDays(1)</c>.
    /// An inclusive end such as 23:59:59.999 silently excludes the last sub-millisecond ticks of the day.
    /// </para>
    /// <para>
    /// The offset is copied from <paramref name="value"/>. On a daylight-saving transition day, a time zone's real
    /// offset at midnight can differ; convert with <see cref="TimeZoneInfo"/> when the zone matters.
    /// </para>
    /// </remarks>
    /// <param name="value">The date and time whose day to use.</param>
    /// <returns>A value on the same calendar day at 00:00:00.0000000, with the same offset.</returns>
    /// <example>
    /// <code>
    /// DateTimeOffset today = clock.UtcNow.StartOfDay();
    /// var placedToday = orders.Where(o =&gt; o.PlacedAt &gt;= today &amp;&amp; o.PlacedAt &lt; today.AddDays(1));
    /// </code>
    /// </example>
    public static DateTimeOffset StartOfDay(this DateTimeOffset value)
        => new(value.Date, value.Offset);
}
