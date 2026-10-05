using System.Collections.Concurrent;
using System.Reflection;

namespace SharedKernel.Contracts.Events;

/// <summary>
/// The validated wire name and schema version of an integration event type, read from its
/// <see cref="IntegrationEventAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Use it wherever an event type needs its wire identity outside an envelope: a webhook routing key,
/// a subscription filter, a telemetry tag, a consumer's dispatch table.
/// </para>
/// <para>
/// <b>Caching.</b> Each type is inspected once per process and the result is cached. A type that fails
/// validation is not cached, so every call for it throws.
/// </para>
/// <para>
/// <b>Uniqueness.</b> Two different types that declare the same name and version cannot both be resolved in one
/// process: resolving the second throws <see cref="InvalidOperationException"/>. The same name with different
/// versions is allowed, which is how two schema versions of one event coexist.
/// </para>
/// </remarks>
public sealed class IntegrationEventDescriptor
{
    private const int MaxNameLength = 128;

    private static readonly ConcurrentDictionary<Type, IntegrationEventDescriptor> Cache = new();
    private static readonly ConcurrentDictionary<(string Name, int Version), Type> Claims = new();

    private IntegrationEventDescriptor(Type eventType, string name, int version)
    {
        EventType = eventType;
        Name = name;
        Version = version;
    }

    /// <summary>Gets the integration event type this descriptor describes.</summary>
    public Type EventType { get; }

    /// <summary>Gets the event's wire name, such as <c>orders.order-placed</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the schema version of the event's data, at least 1.</summary>
    public int Version { get; }

    /// <summary>Returns the descriptor for <typeparamref name="TEvent"/>.</summary>
    /// <typeparam name="TEvent">A concrete integration event type.</typeparam>
    /// <returns>The cached, validated descriptor.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TEvent"/> is abstract, has no <see cref="IntegrationEventAttribute"/>, declares an
    /// invalid name or a version below 1, or declares the same name and version as another type already
    /// resolved in this process.
    /// </exception>
    public static IntegrationEventDescriptor For<TEvent>()
        where TEvent : class, IIntegrationEvent =>
        For(typeof(TEvent));

    /// <summary>Returns the descriptor for <paramref name="eventType"/>.</summary>
    /// <param name="eventType">A concrete type implementing <see cref="IIntegrationEvent"/>. Must not be null.</param>
    /// <returns>The cached, validated descriptor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventType"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="eventType"/> does not implement <see cref="IIntegrationEvent"/>, is abstract or an open
    /// generic type, has no <see cref="IntegrationEventAttribute"/>, declares an invalid name or a version below 1,
    /// or declares the same name and version as another type already resolved in this process.
    /// </exception>
    public static IntegrationEventDescriptor For(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (Cache.TryGetValue(eventType, out var cached))
            return cached;

        var descriptor = Resolve(eventType);

        var claimedBy = Claims.GetOrAdd((descriptor.Name, descriptor.Version), eventType);
        if (claimedBy != eventType)
        {
            throw new InvalidOperationException(
                $"Integration event '{eventType.FullName}' declares name '{descriptor.Name}' version "
                + $"{descriptor.Version}, which '{claimedBy.FullName}' already declares. Each name and version "
                + "pair must belong to exactly one type.");
        }

        return Cache.GetOrAdd(eventType, descriptor);
    }

    /// <summary>Returns the wire name and version, such as <c>orders.order-placed v2</c>.</summary>
    /// <returns>The name, a space, and the version prefixed with <c>v</c>.</returns>
    public override string ToString() => $"{Name} v{Version}";

    private static IntegrationEventDescriptor Resolve(Type eventType)
    {
        if (!typeof(IIntegrationEvent).IsAssignableFrom(eventType))
            throw Invalid(eventType, $"does not implement {nameof(IIntegrationEvent)}");

        if (eventType.IsAbstract || eventType.IsInterface || eventType.ContainsGenericParameters)
            throw Invalid(eventType, "is not a concrete type");

        var attribute = eventType.GetCustomAttribute<IntegrationEventAttribute>(inherit: false)
            ?? throw Invalid(eventType, $"has no [IntegrationEvent(\"...\")] attribute");

        if (!IsValidName(attribute.Name))
        {
            throw Invalid(
                eventType,
                $"declares the name '{attribute.Name}'. A name is 1 to {MaxNameLength} lowercase ASCII letters and "
                + "digits, in segments separated by a single '.', '-' or '_', such as 'orders.order-placed'");
        }

        if (attribute.Version < 1)
            throw Invalid(eventType, $"declares version {attribute.Version}. Versions start at 1");

        return new IntegrationEventDescriptor(eventType, attribute.Name, attribute.Version);
    }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return false;

        var previousWasSeparator = true;
        foreach (var c in name)
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                previousWasSeparator = false;
            }
            else if (c is '.' or '-' or '_' && !previousWasSeparator)
            {
                previousWasSeparator = true;
            }
            else
            {
                return false;
            }
        }

        return !previousWasSeparator;
    }

    private static InvalidOperationException Invalid(Type eventType, string problem) =>
        new($"Type '{eventType.FullName}' cannot be used as an integration event: it {problem}.");
}
