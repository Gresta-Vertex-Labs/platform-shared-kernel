using System.Text.Json.Serialization;

namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// Base marker class for STJ source-generated <see cref="JsonSerializerContext"/> instances
/// used with the SharedKernel caching infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// <strong>NativeAOT registration pattern:</strong> For any build that requires NativeAOT
/// compatibility, create an application-level <c>partial class</c> that derives from (or
/// independently extends) <see cref="JsonSerializerContext"/> and is annotated with
/// <see cref="JsonSerializableAttribute"/> for every type your service stores in the cache.
/// Then pass the singleton instance to <see cref="SharedKernel.Caching.FusionCache.Extensions.CachingOptions.SerializerContext"/>
/// at startup.
/// </para>
/// <para>
/// <c>AddSharedKernelCaching</c> will automatically combine your context with the internal
/// <c>EncryptedCacheEntryJsonContext</c> via <c>JsonTypeInfoResolver.Combine</c>, ensuring
/// that both your application types and the encrypted-entry payload are handled by
/// source-generated contexts — no reflection is used anywhere in the serialization path.
/// </para>
/// <para>
/// Example (NativeAOT build):
/// <code>
/// [JsonSerializable(typeof(OrderDto))]
/// [JsonSerializable(typeof(CustomerDto))]
/// internal partial class MyAppSerializerContext : JsonSerializerContext { }
///
/// // In your DI startup:
/// services.AddSharedKernelCaching(o =>
/// {
///     o.ServiceName = "my-service";
///     o.SerializerContext = MyAppSerializerContext.Default;
/// });
/// </code>
/// </para>
/// <para>
/// When <c>SerializerContext</c> is <see langword="null"/> (the default), FusionCache falls
/// back to reflection-based System.Text.Json serialization, which is acceptable for non-AOT
/// builds but will break NativeAOT publishing.
/// </para>
/// <para>
/// This class itself is intentionally empty — it exists solely to document the pattern
/// and provide a stable import anchor for documentation.
/// </para>
/// </remarks>
public abstract class CacheJsonSerializerContext : JsonSerializerContext
{
    /// <summary>
    /// Initialises a new instance of <see cref="CacheJsonSerializerContext"/>.
    /// </summary>
    /// <param name="options">Optional JSON serializer options.</param>
    protected CacheJsonSerializerContext(System.Text.Json.JsonSerializerOptions? options)
        : base(options)
    {
    }
}
