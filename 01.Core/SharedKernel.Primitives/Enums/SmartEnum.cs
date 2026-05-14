using System.Collections.Concurrent;

namespace SharedKernel.Primitives.Enums;

/// <summary>
/// Abstract base for type-safe enumeration types that pair a name with a strongly-typed value.
/// Subclasses expose a static <c>List</c> of all instances and support value/name lookups
/// without reflection in the hot path.
/// </summary>
/// <typeparam name="TEnum">The concrete enumeration type (CRTP pattern).</typeparam>
/// <typeparam name="TValue">The type of the underlying value (e.g., <see cref="int"/>, <see cref="string"/>).</typeparam>
/// <remarks>
/// <para>
/// <b>AOT lookup strategy:</b> Each concrete enum type initialises a static
/// <see cref="IReadOnlyList{T}"/> (<see cref="List"/>) at type-initialization time via a
/// protected constructor registration pattern. Value and name lookup dictionaries are built
/// lazily from that list — no reflection is used at any point.
/// </para>
/// <para>
/// To define a SmartEnum:
/// <code>
/// public sealed class Status : SmartEnum&lt;Status, int&gt;
/// {
///     public static readonly Status Active   = new(nameof(Active),   1);
///     public static readonly Status Inactive = new(nameof(Inactive), 2);
///
///     private Status(string name, int value) : base(name, value) { }
/// }
/// </code>
/// </para>
/// </remarks>
public abstract class SmartEnum<TEnum, TValue>
    where TEnum : SmartEnum<TEnum, TValue>
    where TValue : IEquatable<TValue>
{
    // Each TEnum subtype gets its own registration list, populated during static field initialisation.
    private static readonly List<TEnum> _list = [];
    private static IReadOnlyList<TEnum>? _readOnlyList;

    // Lazy lookup dictionaries — built once from _list, no reflection.
    private static readonly Lazy<Dictionary<TValue, TEnum>> _byValue = new(BuildValueDictionary);
    private static readonly Lazy<Dictionary<string, TEnum>> _byName  = new(BuildNameDictionary);

    /// <summary>
    /// Gets all declared instances of <typeparamref name="TEnum"/> in declaration order.
    /// </summary>
    public static IReadOnlyList<TEnum> List => _readOnlyList ??= _list.AsReadOnly();

    /// <summary>Gets the name of this enumeration member.</summary>
    public string Name { get; }

    /// <summary>Gets the underlying value of this enumeration member.</summary>
    public TValue Value { get; }

    /// <summary>
    /// Initialises a new SmartEnum member with the given <paramref name="name"/> and
    /// <paramref name="value"/>, and registers it in the static <see cref="List"/>.
    /// </summary>
    /// <param name="name">The name of this member (typically <c>nameof(FieldName)</c>).</param>
    /// <param name="value">The underlying value of this member.</param>
    protected SmartEnum(string name, TValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        Name  = name;
        Value = value;

        // Register during static field initialisation (before List is ever read).
        _list.Add((TEnum)this);
        _readOnlyList = null; // invalidate cached read-only wrapper
    }

    /// <summary>
    /// Returns the <typeparamref name="TEnum"/> member whose <see cref="Value"/> equals
    /// <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The value to look up.</param>
    /// <exception cref="InvalidOperationException">Thrown when no member matches <paramref name="value"/>.</exception>
    public static TEnum FromValue(TValue value)
    {
        if (!_byValue.Value.TryGetValue(value, out var member))
        {
            throw new InvalidOperationException(
                $"No {typeof(TEnum).Name} member with value '{value}' was found.");
        }

        return member;
    }

    /// <summary>
    /// Attempts to find the <typeparamref name="TEnum"/> member whose <see cref="Value"/> equals
    /// <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The value to look up.</param>
    /// <param name="result">
    /// When this method returns <c>true</c>, contains the matching member; otherwise <c>null</c>.
    /// </param>
    /// <returns><c>true</c> if a matching member was found; otherwise <c>false</c>.</returns>
    public static bool TryFromValue(TValue value, out TEnum? result)
        => _byValue.Value.TryGetValue(value, out result);

    /// <summary>
    /// Returns the <typeparamref name="TEnum"/> member whose <see cref="Name"/> equals
    /// <paramref name="name"/> (ordinal, case-sensitive comparison).
    /// </summary>
    /// <param name="name">The name to look up.</param>
    /// <exception cref="InvalidOperationException">Thrown when no member matches <paramref name="name"/>.</exception>
    public static TEnum FromName(string name)
    {
        if (!_byName.Value.TryGetValue(name, out var member))
        {
            throw new InvalidOperationException(
                $"No {typeof(TEnum).Name} member with name '{name}' was found.");
        }

        return member;
    }

    /// <inheritdoc/>
    public override string ToString() => Name;

    private static Dictionary<TValue, TEnum> BuildValueDictionary()
        => List.ToDictionary(m => m.Value, m => m);

    private static Dictionary<string, TEnum> BuildNameDictionary()
        => List.ToDictionary(m => m.Name, m => m, StringComparer.Ordinal);
}
