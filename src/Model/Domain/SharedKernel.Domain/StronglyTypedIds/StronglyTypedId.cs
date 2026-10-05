using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Internal;

namespace SharedKernel.Domain.StronglyTypedIds;

/// <summary>
/// Base record for a strongly-typed identifier: a named wrapper around a primitive identity key, so an
/// <c>OrderId</c> cannot be passed where a <c>CustomerId</c> is expected.
/// </summary>
/// <typeparam name="TValue">
/// The underlying key type, such as <see cref="Guid"/>, <see cref="long"/> or <see cref="string"/>. Must be
/// non-null.
/// </typeparam>
/// <param name="Value">The underlying key value. Must not be null.</param>
/// <remarks>
/// <para>
/// <b>Usage.</b> Declare each identifier as a sealed record with a single positional parameter named
/// <c>Value</c>, as in the example. The JSON converter constructs identifiers through that public
/// constructor.
/// </para>
/// <para>
/// <b>Equality.</b> Two identifiers are equal when they have the same concrete type and equal values, so an
/// <c>OrderId</c> and a <c>CustomerId</c> wrapping the same <see cref="Guid"/> are not equal.
/// <see cref="ToString"/> returns the value's string form.
/// </para>
/// <para>
/// <b>Conversion.</b> Unwrap with <see cref="Value"/> or an explicit cast. There is no implicit conversion in
/// either direction, so an identifier never flows silently into a parameter of the underlying type.
/// </para>
/// <para>
/// <b>Validation.</b> Only <see langword="null"/> is rejected, with a <see cref="DomainException"/>. Values
/// such as <see cref="Guid.Empty"/>, zero or an empty string are accepted; enforce such constraints where the
/// identifier is created.
/// </para>
/// <para>
/// <b>Serialization.</b> Register <see cref="Serialization.StronglyTypedIdJsonConverterFactory"/> once, and
/// every identifier serializes as its bare value, including as a dictionary key.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed record OrderId(Guid Value) : StronglyTypedId&lt;Guid&gt;(Value)
/// {
///     public static OrderId New() =&gt; new(Guid.CreateVersion7());
/// }
///
/// Guid raw = (Guid)orderId;
/// </code>
/// </example>
public abstract record StronglyTypedId<TValue>(TValue Value) : IStronglyTypedId<TValue>
    where TValue : notnull
{
    /// <summary>Gets the underlying identity key value; never <see langword="null"/>.</summary>
    /// <exception cref="DomainException">
    /// The identifier was constructed with a <see langword="null"/> value.
    /// </exception>
    public TValue Value { get; } = DomainInvariants.NotNull(Value, nameof(Value));

    /// <summary>Returns the underlying value's string representation, without the record's type name.</summary>
    /// <returns>The string form of <see cref="Value"/>, or <see cref="string.Empty"/> when it has none.</returns>
    public sealed override string ToString() => Value.ToString() ?? string.Empty;

    /// <summary>Converts an identifier explicitly to its underlying key value.</summary>
    /// <param name="id">The identifier to unwrap. Must not be null.</param>
    /// <returns>The underlying key value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    public static explicit operator TValue(StronglyTypedId<TValue> id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Value;
    }
}
