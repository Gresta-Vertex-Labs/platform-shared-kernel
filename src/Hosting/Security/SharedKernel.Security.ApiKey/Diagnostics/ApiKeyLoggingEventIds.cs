using SharedKernel.Primitives.Logging;

namespace SharedKernel.Security.ApiKey.Diagnostics;

/// <summary>The EventIds of this package: sub-block 2 of the <see cref="LoggingEventIdRanges.Security"/> block (12200-12299).</summary>
internal static class ApiKeyLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Security + 2 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>The API key was rejected.</summary>
    internal const int KeyRejected = Base + 0;

    /// <summary>The request carries more than one API key header value.</summary>
    internal const int AmbiguousKey = Base + 1;
}
