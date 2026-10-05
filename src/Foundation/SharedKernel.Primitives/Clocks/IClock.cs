namespace SharedKernel.Primitives.Clocks;

/// <summary>
/// The platform's only source of the current time. Inject this instead of reading
/// <see cref="DateTime.UtcNow"/> or <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule:</b> production code never reads the system clock directly. Reading it inline makes
/// the surrounding logic untestable — you cannot assert what happens at a boundary you cannot
/// move. <c>00.Governance</c>'s <c>SK0001</c> analyzer flags a direct <c>UtcNow</c> read, so this
/// is enforced rather than merely encouraged.
/// </para>
/// <example>
/// <code>
/// services.AddClock();
///
/// public sealed class ExpireSessionHandler(IClock clock)
/// {
///     public bool HasExpired(Session s) =&gt; s.ExpiresAt &lt;= clock.UtcNow;
/// }
/// </code>
/// </example>
/// <para>
/// In tests, register a fake returning a fixed time; <c>src/Testing/SharedKernel.Testing</c> ships
/// one, so there is no need to hand-roll a mock.
/// </para>
/// <para>
/// <b>Do not inject <see cref="System.TimeProvider"/> in its place.</b> The BCL's
/// <c>TimeProvider</c> covers more than reading the time (timers, time zones), and the platform
/// deliberately keeps one narrow seam so that every call site reads the same two members and every
/// test substitutes the same thing. <see cref="SystemClock"/> does read from a
/// <c>TimeProvider</c> internally, but that is its private implementation detail and does not make
/// <c>TimeProvider</c> a sanctioned injection target elsewhere.
/// </para>
/// <para>
/// <b>Both members are UTC.</b> There is no local-time member and none should be added: local time
/// is a presentation concern, and a kernel-level clock that could return either would make every
/// consuming comparison ambiguous about which one it got.
/// </para>
/// </remarks>
public interface IClock
{
    /// <summary>Gets the current UTC date and time.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Gets the current UTC date, with no time component.
    /// </summary>
    /// <remarks>
    /// A convenience over <see cref="UtcNow"/> for date-only comparisons, so a caller does not
    /// repeat the conversion — and so it cannot accidentally convert via local time on the way.
    /// </remarks>
    DateOnly Today { get; }
}
