using SharedKernel.Primitives.Logging;

namespace SharedKernel.Caching.Redis.Core.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Caching.Redis.Core</c> sub-block of <c>02.Caching</c>: EventIds 2100-2199.
/// </summary>
internal static class RedisCoreLoggingEventIds
{
    /// <summary>First id of the sub-block (2100).</summary>
    public const int Base = LoggingEventIdRanges.Caching + (1 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>The connection was restored.</summary>
    public const int ConnectionRestored = Base + 0;

    /// <summary>The connection failed.</summary>
    public const int ConnectionFailed = Base + 1;

    /// <summary>A non-loopback endpoint is configured without TLS.</summary>
    public const int NonLoopbackWithoutTls = Base + 2;

    /// <summary>Redis is not reachable at startup.</summary>
    public const int NotConnectedAtStartup = Base + 3;

    /// <summary>The <c>redis</c> readiness probe caught a failure.</summary>
    public const int ProbeFailed = Base + 4;
}
