using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.ValueObjects;

/// <summary>
/// Abstract base class for all domain value objects. Equality is structural: two instances
/// are equal if and only if all components returned by <see cref="GetEqualityComponents"/>
/// are equal in sequence.
/// </summary>
/// <remarks>
/// <para>
/// The base constructor invokes <see cref="Validate"/> immediately. Return <see langword="null"/>
/// for valid state; return one or more <see cref="Error"/> instances to signal validation failure.
/// When errors are returned, a <see cref="ValidationException"/> is thrown before the instance
/// is observable to callers.
/// </para>
/// <para>
/// Design choice: abstract class (not abstract record) to give full control over
/// <see cref="Equals(object?)"/> and <see cref="GetHashCode()"/>, and to support
/// <see cref="Validate"/> with arbitrary constructor logic. Abstract records with primary
/// constructors do not compose cleanly with this validation hook.
/// </para>
/// <para>
/// <strong>Construction-order hazard:</strong> The base constructor calls <see cref="Validate"/>
/// before the subclass constructor body executes. If <see cref="Validate"/> reads properties that
/// are assigned inside the subclass constructor body (rather than as field initializers), those
/// properties will have their default values (<c>null</c>, <c>0</c>, <c>false</c>) when
/// <see cref="Validate"/> runs — leading to incorrect validation results or
/// <see cref="NullReferenceException"/>.
/// </para>
/// <para>
/// Two safe patterns to avoid this hazard:
/// <list type="number">
///   <item>
///     <term>Field-initializer / primary-constructor assignment</term>
///     <description>
///     Assign properties via field initializers or primary constructor parameter assignments.
///     These execute before the base constructor body, so <see cref="Validate"/> sees the
///     correct values:
///     <code>
///     public sealed class Email : ValueObject
///     {
///         public string Value { get; } = value; // field initializer — safe
///         public Email(string value) { }        // base() called after initializer
///         ...
///     }
///     </code>
///     </description>
///   </item>
///   <item>
///     <term>Factory method pattern</term>
///     <description>
///     Keep the constructor <c>private</c> or <c>protected</c> and expose a
///     <c>static Result&lt;TValueObject&gt; Create(...)</c> factory. The factory constructs
///     the object (triggering validation) and wraps <see cref="ValidationException"/> into
///     a railway-friendly <c>Result.Failure</c>. See the example block below.
///     </description>
///   </item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Factory method pattern (recommended for complex value objects):
/// <code>
/// public sealed class Money : ValueObject
/// {
///     public decimal Amount { get; }
///     public string Currency { get; }
///
///     private Money(decimal amount, string currency)
///     {
///         Amount = amount;
///         Currency = currency;
///     }
///
///     public static Result&lt;Money&gt; Create(decimal amount, string currency)
///     {
///         try { return Result&lt;Money&gt;.Success(new Money(amount, currency)); }
///         catch (ValidationException ex) { return Result&lt;Money&gt;.Failure(ex.Errors[0]); }
///     }
///
///     protected override IEnumerable&lt;object?&gt; GetEqualityComponents()
///     {
///         yield return Amount;
///         yield return Currency;
///     }
///
///     protected override IEnumerable&lt;Error&gt;? Validate()
///     {
///         if (Amount &lt; 0)
///             yield return Error.Validation("Money.NegativeAmount", "Amount must be non-negative.");
///         if (string.IsNullOrWhiteSpace(Currency))
///             yield return Error.Validation("Money.InvalidCurrency", "Currency code is required.");
///     }
/// }
/// </code>
/// </example>
public abstract class ValueObject : IValueObject
{
    /// <summary>
    /// Initialises the value object and validates it.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when <see cref="Validate"/> returns one or more errors.
    /// </exception>
    protected ValueObject()
    {
        var errors = Validate();
        if (errors is not null)
        {
            var errorList = errors.ToList();
            if (errorList.Count > 0)
                throw new ValidationException(errorList);
        }
    }

    /// <summary>
    /// Returns the sequence of components used for structural equality comparison.
    /// Components are compared in order using <see cref="object.Equals(object)"/>.
    /// </summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <summary>
    /// Validates the value object's state at construction time.
    /// Return <see langword="null"/> (or an empty sequence) to indicate valid state.
    /// Return one or more <see cref="Error"/> instances to signal validation failure.
    /// </summary>
    protected abstract IEnumerable<Error>? Validate();

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType()) return false;
        return GetEqualityComponents().SequenceEqual(((ValueObject)obj).GetEqualityComponents());
    }

    /// <inheritdoc/>
    public override int GetHashCode() =>
        GetEqualityComponents().Aggregate(0, (hash, component) =>
            HashCode.Combine(hash, component?.GetHashCode() ?? 0));

    /// <summary>Returns <see langword="true"/> when both value objects are structurally equal.</summary>
    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Returns <see langword="true"/> when the value objects are not structurally equal.</summary>
    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
