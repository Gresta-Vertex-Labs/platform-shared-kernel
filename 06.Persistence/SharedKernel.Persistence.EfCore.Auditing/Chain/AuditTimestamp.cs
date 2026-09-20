namespace SharedKernel.Persistence.EfCore.Auditing.Chain;

/// <summary>
/// Truncates a <see cref="DateTimeOffset"/> to whole microseconds and converts it to/from an integer
/// UTC microseconds-since-epoch value — the SAME precision PostgreSQL's <c>timestamptz</c> column type
/// natively stores.
/// </summary>
/// <remarks>
/// <para>
/// .NET's <see cref="DateTimeOffset"/> carries 100-nanosecond ticks — finer
/// than <c>timestamptz</c>'s microsecond resolution. Hashing the UN-truncated value and then storing it
/// (where PostgreSQL silently truncates the sub-microsecond remainder) means a later read-back and
/// re-hash would use a DIFFERENT value than what was originally hashed — every record would then
/// re-verify as "tampered" even though nothing ever touched it. Truncating BEFORE both the store and
/// the hash — not just before the store — is what makes the two agree.
/// </para>
/// <para>
/// The canonical hash input is the INTEGER microsecond count, not a re-formatted string of the
/// truncated <see cref="DateTimeOffset"/> — an integer has exactly one textual/binary representation
/// per value, while a string format (culture, "O" round-trip format, custom patterns, an offset suffix
/// choice) can vary across .NET versions in ways a "wall-clock timestamp string" should never be
/// allowed to silently drift.
/// </para>
/// </remarks>
internal static class AuditTimestamp
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    /// <summary>Truncates <paramref name="value"/> to whole microseconds, preserving its offset.</summary>
    public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        var truncatedTicks = value.Ticks - (value.Ticks % TicksPerMicrosecond);
        return new DateTimeOffset(truncatedTicks, value.Offset);
    }

    /// <summary>Converts an already-microsecond-truncated <paramref name="value"/> to integer UTC microseconds since the Unix epoch.</summary>
    public static long ToUtcMicroseconds(DateTimeOffset value) =>
        (value.UtcDateTime.Ticks - DateTime.UnixEpoch.Ticks) / TicksPerMicrosecond;
}
