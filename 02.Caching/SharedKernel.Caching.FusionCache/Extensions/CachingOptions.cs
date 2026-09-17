using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Configuration;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// Settings for the FusionCache implementation of <see cref="ICacheService"/>, bound from the
/// <c>SharedKernel:Caching</c> configuration section or set in code.
/// </summary>
/// <remarks>
/// <para>
/// Every setting is read when the cache is first resolved, after configuration binding and every
/// <c>configure</c> delegate have run, and is validated at host startup.
/// </para>
/// <para>
/// Service-wide defaults (<see cref="DistributedCacheSoftTimeout"/>, <see cref="DistributedCacheHardTimeout"/>,
/// <see cref="FailSafeThrottleDuration"/>) apply to every entry; a <see cref="CachePolicy"/> controls
/// everything else per entry.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // appsettings.json
/// // "SharedKernel": { "Caching": { "ServiceName": "orders", "L1SizeLimit": 50000,
/// //                                 "DistributedCacheSoftTimeout": "00:00:00.100" } }
/// builder.Services.AddSharedKernelCaching(builder.Configuration);
/// </code>
/// </example>
public sealed class CachingOptions : ISectionBoundOptions
{
    /// <summary>Gets the configuration section path: <c>SharedKernel:Caching</c>.</summary>
    public static string SectionName => "SharedKernel:Caching";

    /// <summary>
    /// Gets or sets the owning service's name, the prefix of every cache key. Required.
    /// </summary>
    /// <remarks>
    /// 1 to 64 lowercase ASCII letters, digits, <c>.</c>, <c>_</c> or <c>-</c>, starting with a letter
    /// or digit (<see cref="CacheKeyFormat.IsValidServiceName"/>). There is no default, so services that
    /// share a Redis instance cannot collide on a forgotten one. Host startup fails when it is invalid.
    /// </remarks>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of entries the memory cache holds. Defaults to 10 000.
    /// </summary>
    /// <remarks>
    /// An entry count, not bytes: every entry counts as one. Past the limit the memory cache evicts
    /// entries, which the distributed layer (when configured) still holds. Lower it for
    /// memory-constrained pods.
    /// </remarks>
    [Range(1, int.MaxValue, ErrorMessage = "L1SizeLimit must be at least 1.")]
    public int L1SizeLimit { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets a value indicating whether host startup waits until every
    /// <see cref="ICacheWarmupStrategy"/> has run. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, warmup runs before any hosted service starts, including the web
    /// server, so the service takes no traffic and reports no readiness until the cache is warm. A long
    /// warmup therefore needs a Kubernetes <c>startupProbe</c> with enough headroom. When
    /// <see langword="false"/>, warmup runs in the background after startup.
    /// </remarks>
    public bool WaitForWarmup { get; set; }

    /// <summary>
    /// Gets or sets how long a distributed-layer operation may take before the cache falls back to an
    /// expired value, or <see langword="null"/> for no limit. Applies to every entry.
    /// </summary>
    /// <remarks>
    /// Applies only when fail-safe is on and an expired value exists; otherwise
    /// <see cref="DistributedCacheHardTimeout"/> applies. Must be positive and shorter than
    /// <see cref="DistributedCacheHardTimeout"/> when both are set.
    /// </remarks>
    public TimeSpan? DistributedCacheSoftTimeout { get; set; }

    /// <summary>
    /// Gets or sets how long any distributed-layer operation may take before the cache continues
    /// without it, or <see langword="null"/> for no limit. Applies to every entry. Must be positive.
    /// </summary>
    /// <remarks>A slow or unreachable Redis then costs at most this long per operation.</remarks>
    public TimeSpan? DistributedCacheHardTimeout { get; set; }

    /// <summary>
    /// Gets or sets how long a value served by fail-safe is reused before the factory is tried again,
    /// or <see langword="null"/> for the provider default (30 seconds). Applies to every entry. Must be positive.
    /// </summary>
    /// <remarks>Keeps a failing source from being hit on every request while it is down.</remarks>
    public TimeSpan? FailSafeThrottleDuration { get; set; }

    /// <summary>
    /// Gets or sets a source-generated <see cref="JsonSerializerContext"/> covering every type the
    /// service caches, or <see langword="null"/> to use reflection-based serialization.
    /// </summary>
    /// <remarks>
    /// Set it in code (it cannot be bound from configuration) when the service is trimmed or published
    /// as NativeAOT. It is used for distributed entries and for the plaintext of encrypted entries.
    /// </remarks>
    public JsonSerializerContext? SerializerContext { get; set; }
}

/// <summary>
/// Validates <see cref="CachingOptions"/> rules that data annotations cannot express.
/// </summary>
internal sealed class CachingOptionsValidator : IValidateOptions<CachingOptions>
{
    public ValidateOptionsResult Validate(string? name, CachingOptions options)
    {
        var failures = new List<string>();

        if (!CacheKeyFormat.IsValidServiceName(options.ServiceName))
        {
            failures.Add(
                "CachingOptions.ServiceName must be set to 1 to 64 lowercase ASCII letters, digits, '.', '_' or '-', starting with a letter or digit.");
        }

        RequirePositive(options.DistributedCacheSoftTimeout, nameof(CachingOptions.DistributedCacheSoftTimeout), failures);
        RequirePositive(options.DistributedCacheHardTimeout, nameof(CachingOptions.DistributedCacheHardTimeout), failures);
        RequirePositive(options.FailSafeThrottleDuration, nameof(CachingOptions.FailSafeThrottleDuration), failures);

        if (options.DistributedCacheSoftTimeout is { } soft && options.DistributedCacheHardTimeout is { } hard && soft >= hard)
        {
            failures.Add("CachingOptions.DistributedCacheSoftTimeout must be shorter than DistributedCacheHardTimeout.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(TimeSpan? value, string property, List<string> failures)
    {
        if (value is { } duration && duration <= TimeSpan.Zero)
        {
            failures.Add($"CachingOptions.{property} must be positive.");
        }
    }
}
