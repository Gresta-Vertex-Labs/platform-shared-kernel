using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.ValueObjects;

/// <summary>
/// Abstract base class for single-component value objects — value objects whose entire
/// identity is captured in a single <typeparamref name="TValue"/> property.
/// </summary>
/// <typeparam name="TValue">The type of the wrapped value. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <see cref="GetEqualityComponents"/> is sealed and returns <c>[Value]</c> so that two
/// instances are equal when their inner values are equal. <see cref="ToString"/> is sealed
/// and returns <c>Value?.ToString() ?? string.Empty</c>.
/// </para>
/// <para>
/// <strong>Construction-order safety:</strong> This class uses C# primary constructor syntax
/// so that the <c>value</c> parameter is captured as a field initializer. In C#, field
/// initializers for the declaring type execute before <c>base()</c> is called, which means
/// <see cref="Value"/> is already set when the base <see cref="ValueObject"/> constructor
/// invokes <c>Validate()</c>. Subclasses that add their own validated members must
/// follow the same pattern (field initializers, not constructor body assignments) to avoid
/// the construction-order hazard described on <see cref="ValueObject"/>.
/// </para>
/// <para>
/// <strong>Distinction from <c>StronglyTypedId&lt;TValue&gt;</c>:</strong>
/// Use <see cref="SingleValueObject{TValue}"/> for domain concepts with validation rules
/// (e.g., <c>EmailAddress</c>, <c>Money</c>, <c>Percentage</c>).
/// Use <c>StronglyTypedId&lt;TValue&gt;</c> for entity or aggregate identity keys that are
/// persisted to the database without domain validation.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class EmailAddress : SingleValueObject&lt;string&gt;
/// {
///     public EmailAddress(string value) : base(value) { }
///
///     protected override IEnumerable&lt;Error&gt;? Validate()
///     {
///         if (string.IsNullOrWhiteSpace(Value))
///             yield return Error.Validation("Email.Required", "Email address is required.");
///         else if (!Value.Contains('@'))
///             yield return Error.Validation("Email.InvalidFormat", "Email address must contain '@'.");
///     }
/// }
/// </code>
/// </example>
public abstract class SingleValueObject<TValue>(TValue value) : ValueObject where TValue : notnull
{
    // Field initializer: captured from the primary constructor parameter, guarded against null
    // for reference-type TValue instantiations (WO-051/P-311; a no-op for value types since
    // `value is null` is always false for those). Field initializers execute before base() is
    // called, so Value is set before ValueObject() invokes Validate() — the construction-order fix.
    private readonly TValue _value = GuardValue(value);

    /// <summary>Gets the wrapped value.</summary>
    public TValue Value => _value;

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
                Error.Validation("SingleValueObject.Value.Null", "Value must not be null."));

        return value;
    }

    /// <inheritdoc/>
    protected sealed override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc/>
    public sealed override string ToString() => Value?.ToString() ?? string.Empty;

    /// <summary>
    /// Implicitly unwraps the <see cref="SingleValueObject{TValue}"/> to its inner
    /// <typeparamref name="TValue"/>.
    /// </summary>
    public static implicit operator TValue(SingleValueObject<TValue> svo) => svo.Value;
}
