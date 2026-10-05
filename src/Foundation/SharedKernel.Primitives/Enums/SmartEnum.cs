using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SharedKernel.Primitives.Enums;

/// <summary>
/// Base class for a type-safe enumeration: a fixed set of named singleton members, each carrying
/// an underlying value, with lookups and behaviour that a plain <c>enum</c> cannot have.
/// </summary>
/// <typeparam name="TEnum">The concrete enumeration type — the class deriving from this one.</typeparam>
/// <typeparam name="TValue">The underlying value type, e.g. <see cref="int"/> or <see cref="string"/>.</typeparam>
/// <remarks>
/// <para>
/// Reach for this over a plain <c>enum</c> when the set needs to carry behaviour or data per
/// member, or needs lookup by value that fails loudly instead of silently accepting
/// <c>(Status)999</c>. A plain <c>enum</c> is still the right choice for a simple flag set.
/// </para>
/// <example>
/// <code>
/// public sealed class OrderStatus : SmartEnum&lt;OrderStatus, int&gt;
/// {
///     public static readonly OrderStatus Pending  = new(nameof(Pending),  1);
///     public static readonly OrderStatus Shipped  = new(nameof(Shipped),  2);
///     public static readonly OrderStatus Complete = new(nameof(Complete), 3);
///
///     private OrderStatus(string name, int value) : base(name, value) { }
///
///     public bool IsTerminal =&gt; this == Complete;
/// }
/// </code>
/// Members are <c>static readonly</c> fields, the constructor is private, and the class is
/// <c>sealed</c> — that shape is what makes every member a singleton and reference equality
/// correct.
/// </example>
/// <para>
/// <b>Lookups.</b> <see cref="List"/> returns every member in declaration order.
/// <see cref="FromValue"/> and <see cref="FromName"/> throw when nothing matches;
/// <see cref="TryFromValue"/> and <see cref="TryFromName"/> return <c>false</c> instead and never
/// throw, including for a <see langword="null"/> key. Use the <c>Try</c> pair for anything
/// parsed from outside the process — a request field, a database column — and the throwing pair
/// only where a miss is a bug.
/// </para>
/// <para>
/// <b>Rule: every member needs a distinct value AND a distinct name.</b> A duplicate makes lookup
/// ambiguous, so the first lookup of that kind throws an <see cref="InvalidOperationException"/>
/// naming the type, the duplicated key, and both colliding members. Because the lookup tables are
/// built on first use, that error surfaces at the first lookup rather than at the declaration —
/// so a duplicate can sit undetected until something reads it.
/// </para>
/// <para>
/// <b>Equality is reference equality; ordering is by value.</b> Every member is a singleton, so
/// <c>FromValue(2) == OrderStatus.Shipped</c> holds and <c>==</c> is the right comparison. Ordering
/// comes from <see cref="IComparable{T}"/> over <see cref="Value"/>, so
/// <c>List&lt;TEnum&gt;.Sort()</c>, <c>OrderBy</c>, and <c>SortedSet&lt;TEnum&gt;</c> all work.
/// </para>
/// <para>
/// The comparison goes through <see cref="Comparer{T}"/> rather than a
/// <c>where TValue : IComparable&lt;TValue&gt;</c> constraint, deliberately. Adding that constraint
/// compiles here but breaks <c>SharedKernel.Core</c>'s
/// <c>Guard.Against.InvalidSmartEnum&lt;TEnum, TValue&gt;</c>, which is itself generic over
/// <typeparamref name="TValue"/> and constrains only <see cref="IEquatable{T}"/> — so it would
/// widen a public API in another package. The cost is that a <typeparamref name="TValue"/> with no
/// ordering at all fails when first compared rather than at compile time; in practice every
/// underlying value is an <see cref="int"/>, <see cref="string"/>, <see cref="System.Guid"/>, or
/// <c>enum</c>, all of which are comparable.
/// </para>
/// <para>
/// <b>Serializing one needs <see cref="SmartEnumJsonConverter{TEnum, TValue}"/>.</b> Without it a
/// SmartEnum is write-only over JSON: the default object serializer emits both properties and then
/// cannot read them back, because the constructor is private. See that converter for the opt-in.
/// </para>
/// <para>
/// <b>Trimming and AOT:</b> safe, and verified against a <c>TrimMode=full</c> publish. No
/// reflection is used for registration or lookup. The one trim-analyzer interaction is documented
/// on <see cref="_forceEnumStaticConstructor"/>, along with the initialization hazard it exists to
/// close — read that before changing anything about how members register.
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
    /// <b>The trap this closes, which is subtle enough to be worth stating before the fix.</b>
    /// <see cref="FromValue"/>, <see cref="TryFromValue"/>, <see cref="FromName"/>, and
    /// <see cref="List"/> are all physically declared on this closed generic BASE type,
    /// <c>SmartEnum&lt;TEnum,TValue&gt;</c> — never on <typeparamref name="TEnum"/> itself. So
    /// <c>OrderStatus.FromValue(1)</c> resolves, at the CLR level, to an INHERITED static member,
    /// and per ECMA-335 type-initialization semantics, reaching an inherited static member through
    /// a derived type name does NOT guarantee the derived type's own static constructor has run.
    /// That cctor is exactly what registers the members: it runs the
    /// <c>public static readonly OrderStatus Pending = new(...)</c> field initializers, which call
    /// the protected instance constructor that appends to <see cref="_list"/>. So if the very first
    /// touch of a SmartEnum-derived type is one of those inherited static calls,
    /// <see cref="_list"/> can still be empty at that moment. <see cref="List"/> would survive it —
    /// its <c>_readOnlyList ??=</c> cache is invalidated by every registration and so self-heals on
    /// a later read — but <see cref="_byValue"/> and <see cref="_byName"/> are one-shot
    /// <see cref="Lazy{T}"/> fields with no invalidation hook: force either against an empty
    /// <see cref="_list"/> and the empty dictionary is cached for the rest of the process, so every
    /// subsequent lookup silently misses.
    /// </para>
    /// <para>
    /// <b>The fix.</b> It is declared here — on the same closed generic type that declares
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
    /// Gets every declared member of <typeparamref name="TEnum"/>, in declaration order.
    /// </summary>
    /// <remarks>
    /// Useful for populating a dropdown, validating an incoming value against the full set, or
    /// iterating members in a test. Order follows the order the <c>static readonly</c> fields are
    /// declared in, not value order — call <c>.Order()</c> if you need the latter.
    /// </remarks>
    public static IReadOnlyList<TEnum> List => _readOnlyList ??= _list.AsReadOnly();

    /// <summary>
    /// Gets this member's name — the identifier it was declared under.
    /// </summary>
    /// <remarks>
    /// Also what <see cref="ToString"/> returns, so this is the text that reaches log messages and
    /// exception text. Prefer <see cref="Value"/> for anything persisted or sent over the wire, so
    /// that renaming a member stays a source-only change.
    /// </remarks>
    public string Name { get; }

    /// <summary>
    /// Gets this member's underlying value — its stable identity.
    /// </summary>
    /// <remarks>
    /// This is the field to persist and to put on the wire, and the one
    /// <see cref="SmartEnumJsonConverter{TEnum, TValue}"/> serializes. It also defines ordering.
    /// </remarks>
    public TValue Value { get; }

    /// <summary>
    /// Initialises a member and registers it in <see cref="List"/>.
    /// </summary>
    /// <param name="name">
    /// This member's name. Pass <c>nameof(TheField)</c> so the name cannot drift from the field it
    /// names.
    /// </param>
    /// <param name="value">This member's underlying value. Must be distinct across members.</param>
    /// <remarks>
    /// Called only from a derived type's <c>static readonly</c> field initializers — keep the
    /// derived constructor <c>private</c> so nothing outside the type can create an unregistered
    /// member that no lookup would ever find.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
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
