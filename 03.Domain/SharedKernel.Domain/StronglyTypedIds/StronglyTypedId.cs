using SharedKernel.Domain.Abstractions;

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
/// <strong>STJ serialisation note:</strong> This package ships no <c>JsonConverter</c>.
/// Consuming services must provide their own <c>JsonConverter&lt;TId&gt;</c> and register it
/// in their serialisation context (e.g., in <c>04.Contracts</c> or <c>06.Persistence</c>).
/// </para>
/// </remarks>
public abstract record StronglyTypedId<TValue>(TValue Value) : IStronglyTypedId<TValue>
    where TValue : notnull
{
    /// <summary>Returns the string representation of the underlying <see cref="Value"/>.</summary>
    public sealed override string ToString() => Value.ToString()!;

    /// <summary>Implicitly converts a <see cref="StronglyTypedId{TValue}"/> to its underlying <typeparamref name="TValue"/>.</summary>
    public static implicit operator TValue(StronglyTypedId<TValue> id) => id.Value;
}
