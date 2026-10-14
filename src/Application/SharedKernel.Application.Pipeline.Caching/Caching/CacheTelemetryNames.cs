using System.Diagnostics;
using SharedKernel.Application.Caching;

namespace SharedKernel.Application.Pipeline.Caching;

/// <summary>
/// The lowercase snake values the cache telemetry (tags and log placeholders) uses for the internal enums.
/// </summary>
/// <remarks>
/// Total switches over literals rather than <c>Enum.ToString()</c>: the wire value stays independent of member renames
/// and PascalCase never reaches a dashboard. An undefined member throws <see cref="UnreachableException"/>, and a test
/// enumerates each enum so a new member fails until it is mapped here.
/// </remarks>
internal static class CacheTelemetryNames
{
    /// <summary>The eviction target value for a single cache key.</summary>
    internal const string TargetKey = "key";

    /// <summary>The eviction target value for a cache tag.</summary>
    internal const string TargetTag = "tag";

    /// <summary>The telemetry value of a <see cref="CacheOutcome"/>.</summary>
    internal static string Of(CacheOutcome outcome) => outcome switch
    {
        CacheOutcome.Hit => "hit",
        CacheOutcome.Miss => "miss",
        CacheOutcome.Refreshed => "refreshed",
        CacheOutcome.NotCachedFailure => "not_cached_failure",
        CacheOutcome.NotCachedByPredicate => "not_cached_by_predicate",
        CacheOutcome.BypassedScopeUnavailable => "bypassed_scope_unavailable",
        _ => throw new UnreachableException($"The cache outcome '{outcome}' has no telemetry name."),
    };

    /// <summary>The telemetry value of a <see cref="CacheScope"/>.</summary>
    internal static string Of(CacheScope scope) => scope switch
    {
        CacheScope.Global => "global",
        CacheScope.Tenant => "tenant",
        CacheScope.User => "user",
        _ => throw new UnreachableException($"The cache scope '{scope}' has no telemetry name."),
    };

    /// <summary>The telemetry value of an eviction target.</summary>
    internal static string Target(bool isTag) => isTag ? TargetTag : TargetKey;
}
