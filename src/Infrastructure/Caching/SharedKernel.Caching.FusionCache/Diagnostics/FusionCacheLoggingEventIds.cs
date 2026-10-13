using SharedKernel.Primitives.Logging;

namespace SharedKernel.Caching.FusionCache.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Caching.FusionCache</c> sub-block of <c>02.Caching</c>: EventIds 2000-2099.
/// </summary>
internal static class FusionCacheLoggingEventIds
{
    /// <summary>First id of the sub-block (2000).</summary>
    public const int Base = LoggingEventIdRanges.Caching + (0 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>Host startup waits for warmup.</summary>
    public const int WaitingForWarmup = Base + 0;

    /// <summary>No warmup strategy is registered.</summary>
    public const int NoWarmupStrategies = Base + 1;

    /// <summary>Warmup starts.</summary>
    public const int WarmupStarting = Base + 2;

    /// <summary>Warmup was cancelled before a strategy.</summary>
    public const int WarmupCancelled = Base + 3;

    /// <summary>A warmup strategy starts.</summary>
    public const int StrategyExecuting = Base + 4;

    /// <summary>A warmup strategy completed.</summary>
    public const int StrategyCompleted = Base + 5;

    /// <summary>A warmup strategy failed.</summary>
    public const int StrategyFailed = Base + 6;

    /// <summary>Warmup completed.</summary>
    public const int WarmupCompleted = Base + 7;

    /// <summary>A read missed.</summary>
    public const int CacheMiss = Base + 10;

    /// <summary>A value was written.</summary>
    public const int CacheSet = Base + 11;

    /// <summary>A factory ran.</summary>
    public const int FactoryInvoked = Base + 12;

    /// <summary>An entry was removed.</summary>
    public const int CacheRemoved = Base + 13;

    /// <summary>The entries of a tag were removed.</summary>
    public const int CacheTagRemoved = Base + 14;

    /// <summary>An encrypted entry failed to decrypt.</summary>
    public const int DecryptFailed = Base + 15;

    /// <summary>An entry was expired.</summary>
    public const int CacheExpired = Base + 16;

    /// <summary>The cache was cleared.</summary>
    public const int CacheCleared = Base + 17;

    /// <summary>Evicting an undecryptable entry failed.</summary>
    public const int EvictAfterDecryptFailureFailed = Base + 18;

    /// <summary>The cache readiness probe caught a failure.</summary>
    public const int ProbeFailed = Base + 19;
}
