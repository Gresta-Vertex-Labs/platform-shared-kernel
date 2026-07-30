using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.StronglyTypedIds;

/// <summary>
/// Abstract base record for strongly-typed identifiers. Wraps a primitive value of type
/// <typeparamref name="TValue"/> and provides implicit unwrapping.
/// </summary>
/// <typeparam name="TValue">The underlying primitive type (e.g., <see cref="Guid"/>, <see cref="int"/>). Must be non-null.</typeparam>
/// <param name="Value">The underlying primitive value of this identifier.</param>
/// <remarks>
/// <para>
/// Concrete strongly-typed ID records should be sealed:
/// <code>
/// public sealed record OrderId(Guid Value) : StronglyTypedId&lt;Guid&gt;(Value);
/// </code>
/// </para>
/// <para>
/// Record-based equality is derived from <typeparamref name="TValue"/> automatically.
/// </para>
/// <para>
/// <strong>STJ serialisation note:</strong> This package ships <see cref="Serialization.StronglyTypedIdJsonConverterFactory"/>,
/// an opt-in <see cref="System.Text.Json.Serialization.JsonConverterFactory"/> that (de)serializes
/// concrete <see cref="StronglyTypedId{TValue}"/> types as the bare underlying <typeparamref name="TValue"/>
/// (e.g. a JSON string for <see cref="Guid"/>/<see cref="string"/>, a JSON number for <see cref="int"/>/<see cref="long"/>),
/// never as an object wrapper. It is not registered automatically — consuming services opt in via
/// <c>options.Converters.Add(new StronglyTypedIdJsonConverterFactory())</c>. Concrete types must follow
/// the documented shape above (a public primary constructor <c>(TValue Value)</c> on a non-abstract closed type).
/// </para>
/// </remarks>
public abstract record StronglyTypedId<TValue>(TValue Value) : IStronglyTypedId<TValue>
    where TValue : notnull
{
    /// <summary>Gets the underlying primitive value of this identifier.</summary>
    /// <remarks>
    /// WO-051/P-311 — the positional parameter is redeclared as an explicit property with a
    /// guarded initializer (<see cref="GuardValue"/>) so a reference-type <typeparamref name="TValue"/>
    /// instantiated with <see langword="null"/> (e.g. <c>new SomeId(null!)</c>) throws
    /// <see cref="DomainException"/> at construction rather than surfacing a
    /// <see cref="NullReferenceException"/> later at first use. This is a no-op for value-type
    /// <typeparamref name="TValue"/> instantiations (e.g. <see cref="Guid"/>, <see cref="int"/>),
    /// since <c>value is null</c> is always <see langword="false"/> for those.
    /// </remarks>
    public TValue Value { get; } = GuardValue(Value);

    /// <summary>Returns the string representation of the underlying <see cref="Value"/>.</summary>
    public sealed override string ToString() => Value.ToString()!;

    /// <summary>Implicitly converts a <see cref="StronglyTypedId{TValue}"/> to its underlying <typeparamref name="TValue"/>.</summary>
    public static implicit operator TValue(StronglyTypedId<TValue> id) => id.Value;

    /// <summary>
    /// Throws <see cref="DomainException"/> when <paramref name="value"/> is <see langword="null"/>
    /// and <typeparamref name="TValue"/> is a reference type; a no-op for value-type instantiations,
    /// since <c>Guard.Throw.Null&lt;T&gt;</c>'s <c>where T : class</c> constraint cannot apply to an
    /// unconstrained <typeparamref name="TValue"/>.
    /// </summary>
    private static TValue GuardValue(TValue value)
    {
        if (value is null)
            throw new DomainException(
                Error.Validation("StronglyTypedId.Value.Null", "Value must not be null."));

        return value;
    }
}
