namespace SharedKernel.Primitives.Identifiers;

/// <summary>
/// The production <see cref="IIdGenerator"/>, producing RFC 9562 UUID version 7 values via
/// <see cref="Guid.CreateVersion7()"/>.
/// </summary>
/// <remarks>
/// <para>
/// Stateless and thread-safe. Register as a singleton:
/// <code>services.AddSingleton&lt;IIdGenerator, UuidV7IdGenerator&gt;();</code>
/// </para>
/// <para>
/// <b>What ordering you actually get.</b> A UUID v7 is a 48-bit Unix millisecond timestamp in the
/// high bits followed by random bits. So values generated in DIFFERENT milliseconds always compare
/// in time order under <see cref="Guid.CompareTo(Guid)"/>, and values generated within the SAME
/// millisecond have no defined order relative to each other, because only the random bits differ.
/// </para>
/// <para>
/// That per-millisecond tie is not a problem for the reason this type exists: same-millisecond
/// values still sort adjacent to each other in an index, so insert locality holds regardless of how
/// the tie breaks. It does mean you must not treat id order as an event order — if you need to know
/// which of two rows was created first, store a timestamp from <see cref="Clocks.IClock"/> and
/// compare that.
/// </para>
/// <para>
/// This type implements RFC 9562's plain random sub-method, not the optional monotonic-counter
/// variant, so it makes no attempt to order values inside a single millisecond.
/// </para>
/// </remarks>
public sealed class UuidV7IdGenerator : IIdGenerator
{
    /// <inheritdoc/>
    public Guid NewId() => Guid.CreateVersion7();
}
