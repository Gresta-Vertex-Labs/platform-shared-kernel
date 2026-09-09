using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

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
/// <para>
/// <b>Static-initialization trap (and why it cannot bite here):</b> <see cref="FromValue"/>,
/// <see cref="TryFromValue"/>, <see cref="FromName"/>, and <see cref="List"/> are all physically
/// declared on this closed generic base type, <c>SmartEnum&lt;TEnum,TValue&gt;</c> — never on
/// <typeparamref name="TEnum"/> itself. Calling <c>Status.FromValue(1)</c> therefore resolves, at
/// the CLR level, to an INHERITED static member. Per ECMA-335 type-initialization semantics,
/// reaching an inherited static member through a derived type name does NOT guarantee the derived
/// type's own static constructor has already run — and it is exactly that static constructor
/// (<typeparamref name="TEnum"/>'s cctor) whose <c>public static readonly Status Active = new(...)</c>
/// field initializers call the protected <see cref="SmartEnum{TEnum,TValue}(string,TValue)"/>
/// instance constructor that populates <see cref="_list"/>. If the very first touch of a
/// <c>SmartEnum</c>-derived type is one of these inherited static calls, <see cref="_list"/> could
/// observably still be empty at that moment. <see cref="List"/>'s own
/// <c>_readOnlyList ??= _list.AsReadOnly()</c> self-heals on a later read, because every instance
/// constructor also resets <c>_readOnlyList</c> to <see langword="null"/> — but <see cref="_byValue"/>
/// and <see cref="_byName"/> are one-shot <see cref="Lazy{T}"/> fields with no equivalent
/// invalidation hook: once <c>.Value</c> forces the dictionary build against an empty
/// <see cref="_list"/>, the built (empty) dictionary is cached for the remaining process lifetime.
/// <see cref="_forceEnumStaticConstructor"/> below closes this gap structurally, at zero per-call
/// cost — see its own remarks for the mechanism.
/// </para>
/// </remarks>
public abstract class SmartEnum<TEnum, TValue>
    where TEnum : SmartEnum<TEnum, TValue>
    where TValue : IEquatable<TValue>
{
    // Each TEnum subtype gets its own registration list, populated during static field initialisation.
    private static readonly List<TEnum> _list = [];

    /// <summary>
    /// Forces <typeparamref name="TEnum"/>'s own static constructor to run exactly once, as part of
    /// THIS type's (<c>SmartEnum&lt;TEnum,TValue&gt;</c>'s) own static initialization sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This field exists solely to close the static-initialization trap documented on the type-level
    /// remarks above. It is declared here — on the same closed generic type that declares
    /// <see cref="_list"/>, <see cref="_byValue"/>, <see cref="_byName"/>, <see cref="List"/>,
    /// <see cref="FromValue"/>, <see cref="TryFromValue"/>, and <see cref="FromName"/> — specifically
    /// so the CLR's own static-initialization guarantee does the work: a type's static constructor
    /// runs every one of that type's static field initializers, in declaration order, before any of
    /// that type's static members become reachable to a caller. Because this field's initializer
    /// calls <see cref="RuntimeHelpers.RunClassConstructor(RuntimeTypeHandle)"/> against
    /// <typeparamref name="TEnum"/>'s own type handle, <typeparamref name="TEnum"/>'s cctor — and
    /// therefore every member's registration into <see cref="_list"/> via the protected instance
    /// constructor — is guaranteed to have already run before <see cref="_byValue"/>/<see cref="_byName"/>'s
    /// lazy factories could ever observe <see cref="_list"/>, no matter which static member a caller
    /// touches first.
    /// </para>
    /// <para>
    /// <b>Zero hot-path cost:</b> this force-call happens exactly ONCE per closed generic type
    /// (e.g. once for <c>SmartEnum&lt;Status,int&gt;</c>, once for
    /// <c>SmartEnum&lt;AnotherEnum,string&gt;</c>), during that type's own one-time static
    /// initialization — never inside <see cref="FromValue"/>, <see cref="TryFromValue"/>, or
    /// <see cref="FromName"/> themselves, which remain untouched and pay no per-call re-check cost.
    /// </para>
    /// <para>
    /// <b>AOT/trim note:</b> <c>typeof(TEnum)</c> here is a directly-expressed reference to a generic
    /// type parameter, substituted with a concrete, statically-known type at every closed-generic
    /// call site by the JIT/AOT compiler — a pattern the ILLink trimmer and NativeAOT analyzer
    /// already recognize and preserve correctly. This is not a dynamically-computed <see cref="Type"/>,
    /// and <see cref="RuntimeHelpers.RunClassConstructor(RuntimeTypeHandle)"/> itself performs no
    /// reflection-based member enumeration — it only runs a type's initializer. No
    /// <c>[DynamicallyAccessedMembers]</c> annotation is needed for this specific intrinsic.
    /// </para>
    /// </remarks>
    private static readonly bool _forceEnumStaticConstructor = ForceEnumStaticConstructor();

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

    // Runs TEnum's static constructor eagerly (see _forceEnumStaticConstructor's remarks).
    // Returning a value (rather than void) lets this be assigned to a `static readonly` field,
    // which is what makes the CLR run it as part of THIS type's own static initialization.
    private static bool ForceEnumStaticConstructor()
    {
        RuntimeHelpers.RunClassConstructor(typeof(TEnum).TypeHandle);
        return true;
    }
}
