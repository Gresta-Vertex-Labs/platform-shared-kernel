using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
/// <b>Member values must be distinct.</b> Two members sharing a <see cref="Value"/> (or a
/// <see cref="Name"/>) make the lookup ambiguous, so the first lookup of either kind throws an
/// <see cref="InvalidOperationException"/> naming the type and the duplicate. Because the lookup
/// tables are built on first use rather than at type initialization, that error surfaces at the
/// first <see cref="FromValue"/>/<see cref="FromName"/>/<see cref="TryFromValue"/> call, not at
/// the declaration.
/// </para>
/// <para>
/// <b>Ordering:</b> members are ordered by their underlying <see cref="Value"/> through
/// <see cref="IComparable{T}"/>, so <c>List&lt;TEnum&gt;.Sort()</c>, <c>OrderBy</c>, and
/// <c>SortedSet&lt;TEnum&gt;</c> all work; before this was added, <c>Sort()</c> threw. Equality
/// remains reference equality — every member is a singleton held in a <c>static readonly</c>
/// field, so the two agree for any type whose values are distinct as required above.
/// </para>
/// <para>
/// The comparison goes through <see cref="Comparer{T}"/> for <typeparamref name="TValue"/> rather
/// than a <c>where TValue : IComparable&lt;TValue&gt;</c> constraint, and that is deliberate. Adding
/// the constraint compiles here but breaks <c>SharedKernel.Core</c>'s
/// <c>Guard.Against.InvalidSmartEnum&lt;TEnum, TValue&gt;</c>, which is itself generic over
/// <typeparamref name="TValue"/> and constrains only <see cref="IEquatable{T}"/> — so the
/// constraint would have to be widened on a public API in another package, turning an additive
/// change into a cross-package breaking one. The trade-off is that a
/// <typeparamref name="TValue"/> with no ordering at all fails when first compared, with
/// <see cref="Comparer{T}"/>'s own exception naming the type, instead of failing at compile time.
/// In practice every underlying value is an <see cref="int"/>, <see cref="string"/>,
/// <see cref="System.Guid"/>, or <c>enum</c>, all of which are comparable.
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
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public abstract class SmartEnum<TEnum, TValue> : IComparable<TEnum>, IComparable
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
    /// <b>Trimming/AOT:</b> <see cref="ForceEnumStaticConstructor"/> carries an
    /// <see cref="UnconditionalSuppressMessageAttribute"/> for <c>IL2059</c>, because ILLink cannot
    /// prove the reachability of a static constructor reached through a generic parameter and
    /// reports the call as unrecognized. The suppression is sound rather than cosmetic:
    /// <c>typeof(TEnum)</c> is substituted with a concrete, statically-known type at every
    /// closed-generic instantiation, that type is definitionally preserved because it IS the
    /// generic argument being compiled, and <see cref="RuntimeHelpers.RunClassConstructor(RuntimeTypeHandle)"/>
    /// needs no member metadata beyond the class initializer the trimmer already keeps for any
    /// preserved type — it enumerates nothing. This is verified by execution, not asserted: a
    /// self-contained <c>TrimMode=full</c> publish resolves <c>FromValue</c>/<c>FromName</c>/
    /// <c>List</c> correctly against a trimmed <c>SmartEnum</c> subclass, and
    /// <c>SmartEnumTrimmingContractTests</c> pins the suppression so removing it cannot silently
    /// reintroduce the warning into every consuming build.
    /// </para>
    /// </remarks>
    private static readonly bool _forceEnumStaticConstructor = ForceEnumStaticConstructor();

    private static IReadOnlyList<TEnum>? _readOnlyList;

    // Lazy lookup dictionaries — built once from _list, no reflection.
    private static readonly Lazy<Dictionary<TValue, TEnum>> _byValue = new(BuildValueDictionary);
    private static readonly Lazy<Dictionary<string, TEnum>> _byName = new(BuildNameDictionary);

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

        Name = name;
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
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No member matches <paramref name="value"/>, or two members share a value.
    /// </exception>
    public static TEnum FromValue(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!_byValue.Value.TryGetValue(value, out var member))
        {
            throw new InvalidOperationException(
                $"No {typeof(TEnum).Name} member with value '{value}' was found."
            );
        }

        return member;
    }

    /// <summary>
    /// Attempts to find the <typeparamref name="TEnum"/> member whose <see cref="Value"/> equals
    /// <paramref name="value"/>.
    /// </summary>
    /// <param name="value">
    /// The value to look up. A <see langword="null"/> value simply yields <c>false</c> — this
    /// method never throws for a missing or null lookup key, which is the whole point of a
    /// <c>Try</c>-shaped member.
    /// </param>
    /// <param name="result">
    /// When this method returns <c>true</c>, contains the matching member; otherwise <c>null</c>.
    /// </param>
    /// <returns><c>true</c> if a matching member was found; otherwise <c>false</c>.</returns>
    /// <exception cref="InvalidOperationException">Two members share a value.</exception>
    public static bool TryFromValue(TValue? value, [NotNullWhen(true)] out TEnum? result)
    {
        if (value is null)
        {
            result = null;
            return false;
        }

        return _byValue.Value.TryGetValue(value, out result);
    }

    /// <summary>
    /// Returns the <typeparamref name="TEnum"/> member whose <see cref="Name"/> equals
    /// <paramref name="name"/> (ordinal, case-sensitive comparison).
    /// </summary>
    /// <param name="name">The name to look up.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No member matches <paramref name="name"/>, or two members share a name.
    /// </exception>
    public static TEnum FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_byName.Value.TryGetValue(name, out var member))
        {
            throw new InvalidOperationException(
                $"No {typeof(TEnum).Name} member with name '{name}' was found."
            );
        }

        return member;
    }

    /// <summary>
    /// Attempts to find the <typeparamref name="TEnum"/> member whose <see cref="Name"/> equals
    /// <paramref name="name"/> (ordinal, case-sensitive comparison).
    /// </summary>
    /// <param name="name">
    /// The name to look up. A <see langword="null"/> name simply yields <c>false</c>.
    /// </param>
    /// <param name="result">
    /// When this method returns <c>true</c>, contains the matching member; otherwise <c>null</c>.
    /// </param>
    /// <returns><c>true</c> if a matching member was found; otherwise <c>false</c>.</returns>
    /// <exception cref="InvalidOperationException">Two members share a name.</exception>
    public static bool TryFromName(string? name, [NotNullWhen(true)] out TEnum? result)
    {
        if (name is null)
        {
            result = null;
            return false;
        }

        return _byName.Value.TryGetValue(name, out result);
    }

    /// <summary>
    /// Compares this member to <paramref name="other"/> by underlying <see cref="Value"/>.
    /// </summary>
    /// <param name="other">The member to compare against. <see langword="null"/> sorts first.</param>
    /// <returns>
    /// A negative number, zero, or a positive number, per <see cref="IComparable{T}.CompareTo"/>.
    /// </returns>
    public int CompareTo(TEnum? other) =>
        other is null ? 1 : Comparer<TValue>.Default.Compare(Value, other.Value);

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">
    /// <paramref name="obj"/> is neither <see langword="null"/> nor a <typeparamref name="TEnum"/>.
    /// </exception>
    public int CompareTo(object? obj) =>
        obj switch
        {
            null => 1,
            TEnum other => CompareTo(other),
            _ => throw new ArgumentException(
                $"Cannot compare a {typeof(TEnum).Name} to a {obj.GetType().Name}.",
                nameof(obj)
            ),
        };

    /// <inheritdoc/>
    public override string ToString() => Name;

    // ToString() is deliberately just the Name (it is what lands in logs and messages), so the
    // debugger row carries the underlying value too -- the part you actually need when checking
    // why a lookup by value missed.
    private string DebuggerDisplay => $"{Name} ({Value})";

    private static Dictionary<TValue, TEnum> BuildValueDictionary() =>
        BuildLookup(member => member.Value, "value", EqualityComparer<TValue>.Default);

    private static Dictionary<string, TEnum> BuildNameDictionary() =>
        BuildLookup(member => member.Name, "name", StringComparer.Ordinal);

    // One shared builder so the duplicate-key diagnostic reads identically for both lookups.
    // Dictionary.Add / ToDictionary would report only "An item with the same key has already been
    // added. Key: 1" — no type name, no indication a SmartEnum declaration is at fault, raised
    // from inside a Lazy at whatever call site happened to look up first.
    private static Dictionary<TKey, TEnum> BuildLookup<TKey>(
        Func<TEnum, TKey> keySelector,
        string keyKind,
        IEqualityComparer<TKey> comparer
    )
        where TKey : notnull
    {
        var members = List;
        var lookup = new Dictionary<TKey, TEnum>(members.Count, comparer);

        foreach (var member in members)
        {
            var key = keySelector(member);
            if (lookup.TryGetValue(key, out var existing))
            {
                throw new InvalidOperationException(
                    $"{typeof(TEnum).Name} declares two members with the same {keyKind} "
                        + $"'{key}': '{existing.Name}' and '{member.Name}'. Every SmartEnum member "
                        + $"must have a distinct {keyKind}, otherwise lookups are ambiguous."
                );
            }

            lookup.Add(key, member);
        }

        return lookup;
    }

    // Runs TEnum's static constructor eagerly (see _forceEnumStaticConstructor's remarks).
    // Returning a value (rather than void) lets this be assigned to a `static readonly` field,
    // which is what makes the CLR run it as part of THIS type's own static initialization.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2059:RunClassConstructor",
        Justification = "typeof(TEnum) is substituted with a concrete, statically-known type at "
            + "every closed-generic instantiation, and that type is preserved by definition "
            + "because it is the generic argument being compiled. RunClassConstructor requires "
            + "only the class initializer the trimmer already keeps for a preserved type and "
            + "enumerates no members, so nothing here can be trimmed away. Verified by running a "
            + "self-contained TrimMode=full publish: FromValue/FromName/List all resolve "
            + "correctly. SmartEnumTrimmingContractTests pins this suppression."
    )]
    private static bool ForceEnumStaticConstructor()
    {
        RuntimeHelpers.RunClassConstructor(typeof(TEnum).TypeHandle);
        return true;
    }
}
