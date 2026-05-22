namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marks a type as a strongly-typed identifier wrapping a primitive value of type <typeparamref name="TValue"/>.
/// </summary>
/// <typeparam name="TValue">The underlying primitive value type (e.g., <see cref="Guid"/>, <see cref="int"/>). Must be non-null.</typeparam>
/// <remarks>
/// Implementing types should be records to obtain value equality. An implicit conversion operator
/// from the implementing type to <typeparamref name="TValue"/> is recommended so callers can
/// unwrap without casting.
/// </remarks>
public interface IStronglyTypedId<TValue> where TValue : notnull
{
    /// <summary>Gets the underlying primitive value of this strongly-typed identifier.</summary>
    TValue Value { get; }
}
