using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// Configuration options for the SharedKernel caching infrastructure.
/// Bound to <c>SharedKernelCaching</c> section in application configuration.
/// </summary>
public sealed class CachingOptions
{
    /// <summary>
    /// The configuration section name used when binding these options from
    /// <c>IConfiguration</c>.
    /// </summary>
    public const string SectionName = "SharedKernelCaching";

    /// <summary>
    /// Maximum number of entries the L1 in-process memory cache may hold simultaneously.
    /// Defaults to <c>10 000</c>. Must be a positive integer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an <b>entry count</b> limit, not a byte limit. Each cache entry contributes
    /// exactly 1 unit toward the limit regardless of payload size. When the limit is reached
    /// the least-recently-used entries are evicted automatically by
    /// <see cref="Microsoft.Extensions.Caching.Memory.MemoryCache"/>.
    /// </para>
    /// <para>
    /// In Kubernetes deployments with strict memory limits, set this value explicitly to
    /// bound per-pod L1 cache cardinality. A value of <c>10 000</c> (the default) is
    /// appropriate for most services; reduce it for high-churn or memory-constrained pods.
    /// </para>
    /// </remarks>
    [Range(1, int.MaxValue, ErrorMessage = "L1SizeLimit must be at least 1.")]
    public int L1SizeLimit { get; set; } = 10_000;

    /// <summary>
    /// Name of the FusionCache instance. Used to differentiate multiple cache instances in the
    /// same DI container. Defaults to <c>"default"</c>.
    /// </summary>
    [Required]
    public string CacheName { get; set; } = "default";

    /// <summary>
    /// The logical name of the owning service. Used as the first segment of every cache key
    /// produced by <c>ICacheKeyProvider</c> in the format <c>{service}:{entity}:{id}</c>.
    /// </summary>
    /// <remarks>
    /// Required, with no default, so services sharing a Redis instance can never collide on a
    /// forgotten default. Must satisfy <c>CacheKeyFormat.IsValidServiceName</c>: 1 to 64 lowercase
    /// ASCII letters, digits, <c>.</c>, <c>_</c> or <c>-</c>, starting with a letter or digit. Host
    /// startup fails otherwise.
    /// </remarks>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// When <see langword="true"/>, the <c>CacheWarmupHostedService</c> integrates with the
    /// host lifecycle to delay the readiness signal until all registered
    /// <c>ICacheWarmupStrategy</c> instances have completed. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set this to <see langword="true"/> in combination with
    /// <c>ICachingBuilder.AddCacheWarmup&lt;TStrategy&gt;()</c> to ensure Kubernetes readiness
    /// probes do not pass until the L1 cache has been pre-populated.
    /// </para>
    /// <para>
    /// When <see langword="false"/> (the default), warmup runs concurrently with normal
    /// service startup and traffic may arrive before warmup completes.
    /// </para>
    /// </remarks>
    public bool WaitForWarmup { get; set; } = false;

    /// <summary>
    /// Configuration for opt-in Brotli compression applied to the L2 Redis distributed cache
    /// path. L1 in-process entries are never affected by this setting.
    /// </summary>
    /// <remarks>
    /// Call <c>AddBrotliCompression()</c> on the returned <see cref="SharedKernel.Caching.Abstractions.ICachingBuilder"/>
    /// to activate compression. Payloads smaller than <see cref="CompressionOptions.L2ThresholdBytes"/>
    /// are stored uncompressed regardless of <see cref="CompressionOptions.Enabled"/>.
    /// </remarks>
    public CompressionOptions Compression { get; set; } = new();

    /// <summary>
    /// The application-level <see cref="JsonSerializerContext"/> to use when configuring the
    /// FusionCache System.Text.Json serializer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set, <c>AddSharedKernelCaching</c> passes a combined
    /// <see cref="System.Text.Json.JsonSerializerOptions"/> to
    /// <c>WithSystemTextJsonSerializer()</c> using
    /// <c>JsonTypeInfoResolver.Combine(SerializerContext, EncryptedCacheEntryJsonContext.Default)</c>,
    /// so the application's cached types and the internal encrypted-entry type are both handled by
    /// source-generated contexts, keeping the serializer NativeAOT-safe.
    /// </para>
    /// <para>
    /// When <see langword="null"/> (the default), FusionCache falls back to reflection-based
    /// System.Text.Json serialization — acceptable for non-AOT builds but will break NativeAOT.
    /// </para>
    /// <para>
    /// Example (NativeAOT build):
    /// <code>
    /// [JsonSerializable(typeof(OrderDto))]
    /// [JsonSerializable(typeof(CustomerDto))]
    /// internal partial class MyAppSerializerContext : JsonSerializerContext { }
    ///
    /// services.AddSharedKernelCaching(o =>
    /// {
    ///     o.ServiceName = "my-service";
    ///     o.SerializerContext = MyAppSerializerContext.Default;
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public JsonSerializerContext? SerializerContext { get; set; }

    // -------------------------------------------------------------------------
    // Nested types
    // -------------------------------------------------------------------------

    /// <summary>
    /// Options that control opt-in Brotli compression for the L2 Redis distributed cache path.
    /// </summary>
    /// <remarks>
    /// These settings are only effective when compression is activated via
    /// <c>ICachingBuilder.AddBrotliCompression()</c>. L1 in-process cache entries are never
    /// compressed.
    /// </remarks>
    public sealed class CompressionOptions
    {
        /// <summary>
        /// Whether Brotli compression is enabled. Defaults to <see langword="false"/>.
        /// </summary>
        /// <remarks>
        /// This flag is informational — the act of calling <c>AddBrotliCompression()</c> enables
        /// the compressor. Setting this property without calling the extension method has no effect.
        /// </remarks>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Minimum serialized payload size in bytes required to trigger compression.
        /// Payloads strictly below this threshold are stored uncompressed.
        /// Defaults to <c>1024</c> bytes. Must be greater than zero.
        /// </summary>
        public int L2ThresholdBytes { get; set; } = 1024;

        /// <summary>
        /// The Brotli compression level to apply. Defaults to <see cref="CompressionLevel.Fastest"/>
        /// to minimise per-entry latency overhead in the cache hot path.
        /// </summary>
        public CompressionLevel Level { get; set; } = CompressionLevel.Fastest;
    }
}

/// <summary>
/// Validates <see cref="CachingOptions"/> beyond what data annotations can express.
/// Registered automatically by <c>AddSharedKernelCaching</c>.
/// </summary>
internal sealed class CachingOptionsValidator : IValidateOptions<CachingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CachingOptions options)
    {
        if (!CacheKeyFormat.IsValidServiceName(options.ServiceName))
            return ValidateOptionsResult.Fail(
                "CachingOptions.ServiceName must be set to 1 to 64 lowercase ASCII letters, digits, '.', '_' or '-', starting with a letter or digit.");

        return ValidateOptionsResult.Success;
    }
}
