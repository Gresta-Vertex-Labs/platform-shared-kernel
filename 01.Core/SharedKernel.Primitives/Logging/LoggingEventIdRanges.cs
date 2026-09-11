namespace SharedKernel.Primitives.Logging;

/// <summary>
/// Compile-time constant registry reserving a contiguous <c>Microsoft.Extensions.Logging</c>
/// <c>EventId</c> numeric range for every capability domain in the root folder map (00 through 20).
/// </summary>
/// <remarks>
/// <para>
/// Each domain constant equals <c>{two-digit folder-map number} * 1000</c> and reserves the
/// contiguous 1000-wide block <c>{value}..{value}+999</c> for that domain's <c>[LoggerMessage]</c>
/// authoring, platform-wide. This is the single canonical source of domain-level <c>EventId</c>
/// base values — every package anywhere in the repo that authors <c>[LoggerMessage]</c> methods
/// must derive its <c>EventId</c> values from this registry (domain base + local offset), never an
/// ad hoc numeric literal disconnected from the domain's reserved block.
/// </para>
/// <example>
/// How an <c>EventId</c> is derived in practice — domain base plus a local offset, never a bare
/// literal:
/// <code>
/// internal static partial class CachingLog
/// {
///     // 2100 = LoggingEventIdRanges.Caching (2000) + this package's sub-block offset (100).
///     [LoggerMessage(
///         EventId = LoggingEventIdRanges.Caching + 100,
///         Level = LogLevel.Warning,
///         Message = "Cache backplane reconnected after {AttemptCount} attempts")]
///     public static partial void BackplaneReconnected(ILogger logger, int attemptCount);
/// }
/// </code>
/// Note the <c>EventId</c> is written as an expression against the constant rather than as
/// <c>2100</c>. That is the point of the registry: the number stays traceable to the domain that
/// owns it, and a reader can tell at a glance which block it belongs to.
/// </example>
/// <para>
/// <b>Domain-level boundary only:</b> this registry enforces only the 1000-wide domain boundary.
/// A domain composed of multiple packages must subdivide its own 1000-wide block into
/// <see cref="PackageSubBlockWidth"/>-wide (100-wide) sub-blocks, one per package, allocated in
/// that domain's package declaration order — intra-domain sub-block assignment is each domain's
/// own documentation responsibility, not something this registry can or does enforce.
/// </para>
/// <para>
/// Worked example — <c>02.Caching</c>'s Redis role-split (five packages sharing the
/// <see cref="Caching"/> 2000-2999 block):
/// <list type="bullet">
/// <item><description><c>SharedKernel.Caching.Redis.Core</c> = 2000-2099</description></item>
/// <item><description><c>SharedKernel.Caching.Redis</c> (L2) = 2100-2199</description></item>
/// <item><description><c>SharedKernel.Caching.Redis.DistributedLocking</c> = 2200-2299</description></item>
/// <item><description><c>SharedKernel.Caching.Redis.HashStore</c> = 2300-2399</description></item>
/// <item><description><c>SharedKernel.Caching.Redis.PubSub</c> = 2400-2499</description></item>
/// </list>
/// This is exactly the sub-block discipline that was missing when <c>Redis.Core</c> and
/// <c>Redis.PubSub</c> independently collided on <c>EventId</c> 4001/4002 — a collision this
/// registry's domain-level reservation does not, by itself, prevent, but which downstream
/// governance tooling can mechanically catch once every package follows the sub-block convention.
/// </para>
/// <para>
/// Adding a new folder-map domain (00-20 today) means adding exactly one new <c>const int</c>
/// field here — never renumbering or reassigning an existing domain's base value, which would
/// silently invalidate every already-shipped <c>EventId</c> in that domain.
/// </para>
/// </remarks>
public static class LoggingEventIdRanges
{
    /// <summary>
    /// The width, in <c>EventId</c> numbers, of one domain's reserved block (1000).
    /// </summary>
    /// <remarks>
    /// Every domain constant below is a multiple of this value, and reserves the range
    /// <c>{value}..{value}+999</c>.
    /// </remarks>
    public const int DomainRangeWidth = 1000;

    /// <summary>
    /// The recommended width, in <c>EventId</c> numbers, of one package's sub-block within a
    /// multi-package domain's <see cref="DomainRangeWidth"/>-wide block (100).
    /// </summary>
    /// <remarks>
    /// A multi-package domain allocates its packages 100-wide sub-blocks in declaration order
    /// (e.g. the first package gets <c>{domain base}+0..{domain base}+99</c>, the second gets
    /// <c>{domain base}+100..{domain base}+199</c>, and so on). This registry documents the
    /// convention but does not — and cannot, being domain-agnostic — enforce a specific domain's
    /// sub-block assignment.
    /// </remarks>
    public const int PackageSubBlockWidth = 100;

    /// <summary>Reserved <c>EventId</c> block base for <c>00.Governance</c> (0-999).</summary>
    public const int Governance = 0;

    /// <summary>Reserved <c>EventId</c> block base for <c>01.Core</c> (1000-1999).</summary>
    public const int Core = 1000;

    /// <summary>Reserved <c>EventId</c> block base for <c>02.Caching</c> (2000-2999).</summary>
    public const int Caching = 2000;

    /// <summary>Reserved <c>EventId</c> block base for <c>03.Domain</c> (3000-3999).</summary>
    public const int Domain = 3000;

    /// <summary>Reserved <c>EventId</c> block base for <c>04.Contracts</c> (4000-4999).</summary>
    public const int Contracts = 4000;

    /// <summary>Reserved <c>EventId</c> block base for <c>05.Application</c> (5000-5999).</summary>
    public const int Application = 5000;

    /// <summary>Reserved <c>EventId</c> block base for <c>06.Persistence</c> (6000-6999).</summary>
    public const int Persistence = 6000;

    /// <summary>Reserved <c>EventId</c> block base for <c>07.Messaging</c> (7000-7999).</summary>
    public const int Messaging = 7000;

    /// <summary>Reserved <c>EventId</c> block base for <c>08.Storage</c> (8000-8999).</summary>
    public const int Storage = 8000;

    /// <summary>Reserved <c>EventId</c> block base for <c>09.Search</c> (9000-9999).</summary>
    public const int Search = 9000;

    /// <summary>Reserved <c>EventId</c> block base for <c>10.Intelligence</c> (10000-10999).</summary>
    public const int Intelligence = 10000;

    /// <summary>Reserved <c>EventId</c> block base for <c>11.Communication</c> (11000-11999).</summary>
    public const int Communication = 11000;

    /// <summary>Reserved <c>EventId</c> block base for <c>12.Security</c> (12000-12999).</summary>
    public const int Security = 12000;

    /// <summary>Reserved <c>EventId</c> block base for <c>13.ServiceDefaults</c> (13000-13999).</summary>
    public const int ServiceDefaults = 13000;

    /// <summary>Reserved <c>EventId</c> block base for <c>14.Presentation</c> (14000-14999).</summary>
    public const int Presentation = 14000;

    /// <summary>Reserved <c>EventId</c> block base for <c>15.Integration</c> (15000-15999).</summary>
    public const int Integration = 15000;

    /// <summary>Reserved <c>EventId</c> block base for <c>16.Testing</c> (16000-16999).</summary>
    public const int Testing = 16000;

    /// <summary>Reserved <c>EventId</c> block base for <c>17.Workflows</c> (17000-17999).</summary>
    public const int Workflows = 17000;

    /// <summary>Reserved <c>EventId</c> block base for <c>18.Idempotency</c> (18000-18999).</summary>
    public const int Idempotency = 18000;

    /// <summary>Reserved <c>EventId</c> block base for <c>19.Scheduling</c> (19000-19999).</summary>
    public const int Scheduling = 19000;

    /// <summary>Reserved <c>EventId</c> block base for <c>20.Reporting</c> (20000-20999).</summary>
    public const int Reporting = 20000;
}
