using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Internal;

namespace SharedKernel.Domain.StronglyTypedIds;

/// <summary>
/// Base record for strongly-typed identifiers: a named wrapper around a primitive key, so an
/// <c>OrderId</c> cannot be passed where a <c>CustomerId</c> is expected.
/// </summary>
/// <typeparam name="TValue">The underlying key type, such as <see cref="Guid"/> or <see cref="long"/>. Must be non-null.</typeparam>
/// <param name="Value">The underlying key value.</param>
/// <remarks>
/// <para>Declare each identifier as a sealed record with a single positional parameter:</para>
/// <code>
/// public sealed record OrderId(Guid Value) : StronglyTypedId&lt;Guid&gt;(Value);
/// </code>
/// <para>
/// Equality is by concrete type and value: <c>OrderId</c> and <c>CustomerId</c> wrapping the same
/// <see cref="Guid"/> are not equal. Unwrap with <see cref="Value"/> or an explicit cast; there is no
/// implicit conversion, which would let one identifier silently flow into a parameter of the underlying type.
/// </para>
/// <para>
/// For JSON, register <see cref="Serialization.StronglyTypedIdJsonConverterFactory"/> once; every identifier
/// then serializes as its bare value, including as a dictionary key.
/// </para>
/// </remarks>
public abstract record StronglyTypedId<TValue>(TValue Value) : IStronglyTypedId<TValue>
    where TValue : notnull
{
    /// <summary>Gets the underlying key value.</summary>
    /// <exception cref="DomainException">Construction with a <see langword="null"/> value.</exception>
    public TValue Value { get; } = DomainInvariants.NotNull(Value, nameof(Value));

    /// <summary>Returns the underlying value's string representation.</summary>
    /// <returns>The string representation of <see cref="Value"/>.</returns>
    public sealed override string ToString() => Value.ToString() ?? string.Empty;

    /// <summary>Unwraps the identifier.</summary>
    /// <param name="id">The identifier to unwrap.</param>
    /// <returns>The underlying key value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    public static explicit operator TValue(StronglyTypedId<TValue> id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Value;
    }
}
