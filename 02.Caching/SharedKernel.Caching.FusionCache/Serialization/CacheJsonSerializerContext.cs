using System.Text.Json.Serialization;

namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// Base marker class for STJ source-generated <see cref="JsonSerializerContext"/> instances
/// used with the SharedKernel caching infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// Consuming services must create their own <see cref="JsonSerializerContext"/> derived class
/// annotated with <see cref="JsonSerializableAttribute"/> for every type they intend to cache,
/// then register it during DI setup via
/// <c>AddSharedKernelCaching(o => o.SerializerContext = MyContext.Default)</c>.
/// </para>
/// <para>
/// Example:
/// <code>
/// [JsonSerializable(typeof(OrderDto))]
/// [JsonSerializable(typeof(CustomerDto))]
/// internal partial class MyCacheSerializerContext : JsonSerializerContext { }
/// </code>
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
