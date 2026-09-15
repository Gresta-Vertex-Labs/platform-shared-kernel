using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Internal;

namespace SharedKernel.Domain.ValueObjects;

/// <summary>
/// Base class for a value object that wraps exactly one value, such as an email address or a SKU.
/// </summary>
/// <typeparam name="TValue">The wrapped value's type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// The constructor stores the value and then calls <see cref="ValueObject.EnsureValid"/>, so a subclass only
/// implements <see cref="ValueObject.Validate"/> against <see cref="Value"/>. Do not add further state to a
/// subclass: validation runs before a subclass constructor body would assign it.
/// </para>
/// <para>
/// Equality, hashing and <see cref="ToString"/> all delegate to <see cref="Value"/>. Unwrap with
/// <see cref="Value"/> or an explicit cast; there is no implicit conversion, so a value cannot silently flow
/// into a parameter that expects a different concept of the same primitive type.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class Sku : SingleValueObject&lt;string&gt;
/// {
///     private Sku(string value) : base(value) { }
///
///     public static ValidationResult&lt;Sku&gt; Create(string value) =&gt; TryCreate(() =&gt; new Sku(value));
///
///     protected override IEnumerable&lt;Error&gt; Validate()
///     {
///         if (Value.Length is &lt; 3 or &gt; 32)
///             yield return Error.Validation("sku.invalid_length", "A SKU is 3 to 32 characters long.");
///     }
/// }
/// </code>
/// </example>
public abstract class SingleValueObject<TValue> : ValueObject
    where TValue : notnull
{
    /// <summary>Stores <paramref name="value"/> and validates it.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ValidationException"><see cref="ValueObject.Validate"/> reported one or more errors.</exception>
    protected SingleValueObject(TValue value)
    {
        Value = DomainInvariants.NotNull(value, nameof(value));
        EnsureValid();
    }

    /// <summary>Gets the wrapped value.</summary>
    public TValue Value { get; }

    /// <inheritdoc/>
    protected sealed override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <summary>Returns the wrapped value's string representation.</summary>
    /// <returns>The string representation of <see cref="Value"/>.</returns>
    public sealed override string ToString() => Value.ToString() ?? string.Empty;

    /// <summary>Unwraps the value.</summary>
    /// <param name="valueObject">The value object to unwrap.</param>
    /// <returns>The wrapped value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="valueObject"/> is <see langword="null"/>.</exception>
    public static explicit operator TValue(SingleValueObject<TValue> valueObject)
    {
        ArgumentNullException.ThrowIfNull(valueObject);
        return valueObject.Value;
    }
}
