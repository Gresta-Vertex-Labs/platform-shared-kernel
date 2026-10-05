using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Internal;

namespace SharedKernel.Domain.ValueObjects;

/// <summary>
/// Base class for a value object that wraps exactly one value, such as an email address or a SKU, and validates
/// it on construction.
/// </summary>
/// <typeparam name="TValue">The wrapped value's type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Validation.</b> The base constructor stores the value and then calls
/// <see cref="ValueObject.EnsureValid"/>, so a subclass only implements <see cref="ValueObject.Validate"/>
/// against <see cref="Value"/> and does not call <see cref="ValueObject.EnsureValid"/> itself.
/// </para>
/// <para>
/// <b>Equality.</b> The single equality component is <see cref="Value"/>, and the runtime type must match, so
/// an <c>Email</c> and a <c>Username</c> wrapping the same string are not equal. <see cref="ToString"/>
/// returns the value's string form.
/// </para>
/// <para>
/// <b>Conversion.</b> Unwrap with <see cref="Value"/> or an explicit cast. There is no implicit conversion, so
/// a value never flows silently into a parameter that expects a different concept of the same primitive type.
/// </para>
/// <para>
/// <b>Pitfall.</b> Never add further state to a subclass. <see cref="ValueObject.Validate"/> runs inside the
/// base constructor, before the subclass constructor body assigns anything, and the extra state would take no
/// part in equality. Derive from <see cref="ValueObject"/> instead.
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
    /// <summary>Initializes a new value object that wraps <paramref name="value"/>, then validates it.</summary>
    /// <param name="value">The value to wrap. Must not be null.</param>
    /// <exception cref="DomainException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ValidationException">
    /// <see cref="ValueObject.Validate"/> reported one or more errors.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="ValueObject.Validate"/> returned a sequence containing a <see langword="null"/> entry.
    /// </exception>
    protected SingleValueObject(TValue value)
    {
        Value = DomainInvariants.NotNull(value, nameof(value));
        EnsureValid();
    }

    /// <summary>Gets the wrapped value; never <see langword="null"/>.</summary>
    public TValue Value { get; }

    /// <summary>Returns <see cref="Value"/> as the only equality component.</summary>
    /// <returns>A sequence containing <see cref="Value"/>.</returns>
    protected sealed override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <summary>Returns the wrapped value's string representation.</summary>
    /// <returns>The string form of <see cref="Value"/>, or <see cref="string.Empty"/> when it has none.</returns>
    public sealed override string ToString() => Value.ToString() ?? string.Empty;

    /// <summary>Converts a value object explicitly to its wrapped value.</summary>
    /// <param name="valueObject">The value object to unwrap. Must not be null.</param>
    /// <returns>The wrapped value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="valueObject"/> is <see langword="null"/>.</exception>
    public static explicit operator TValue(SingleValueObject<TValue> valueObject)
    {
        ArgumentNullException.ThrowIfNull(valueObject);
        return valueObject.Value;
    }
}
