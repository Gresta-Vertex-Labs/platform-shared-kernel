namespace SharedKernel.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="DateTimeOffset"/>.
/// </summary>
public static class DateTimeOffsetExtensions
{
    /// <summary>
    /// Returns midnight at the start of <paramref name="value"/>'s calendar day, keeping its offset.
    /// </summary>
    /// <remarks>
    /// For a whole-day range, query <c>start &lt;= x &amp;&amp; x &lt; start.AddDays(1)</c>. An inclusive
    /// "end of day" such as 23:59:59.999 silently excludes the final sub-millisecond ticks.
    /// </remarks>
    /// <param name="value">The source date and time.</param>
    public static DateTimeOffset StartOfDay(this DateTimeOffset value)
        => new(value.Date, value.Offset);
}
