using System.Collections;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Domain.Internal;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Domain.ValueObjects;

/// <summary>
/// Base class for a value object: an immutable domain value with no identity, equal to another value object
/// of the same type when all their equality components are equal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Validation.</b> Assign every member in the constructor, then call <see cref="EnsureValid"/> as the
/// constructor's last statement. It runs <see cref="Validate"/> against the fully initialized object and
/// throws <see cref="ValidationException"/> carrying every error. The base constructor never calls
/// <see cref="Validate"/>, because a virtual call there would read members the subclass has not assigned yet.
/// <see cref="SingleValueObject{TValue}"/> calls <see cref="EnsureValid"/> for you.
/// </para>
/// <para>
/// <b>Creation.</b> Keep the constructor private and expose a static <c>Create</c> method that wraps it in
/// <see cref="TryCreate{T}"/>, so invalid input becomes a failed <see cref="ValidationResult{T}"/> holding
/// every error instead of an exception.
/// </para>
/// <para>
/// <b>Equality.</b> Two value objects are equal when they have the same runtime type and
/// <see cref="GetEqualityComponents"/> yields equal sequences. A component that is a sequence, other than a
/// <see cref="string"/>, is compared element by element in enumeration order, so two
/// <see cref="HashSet{T}"/> components with the same elements can compare unequal; yield an ordered sequence
/// instead. <c>==</c>, <c>!=</c> and <see cref="GetHashCode"/> follow the same rules.
/// </para>
/// <para>
/// <b>Immutability.</b> Never change a member after construction, and expose collection components as
/// read-only types. A value object whose components change breaks equality and corrupts any hash-based
/// collection that holds it.
/// </para>
/// <para>
/// <b>Pitfall.</b> A constructor that never calls <see cref="EnsureValid"/> produces unvalidated instances
/// silently; analyzer <c>SK0037</c> flags it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class DateRange : ValueObject
/// {
///     private DateRange(DateOnly start, DateOnly end)
///     {
///         Start = start;
///         End = end;
///         EnsureValid();
///     }
///
///     public DateOnly Start { get; }
///     public DateOnly End { get; }
///
///     public static ValidationResult&lt;DateRange&gt; Create(DateOnly start, DateOnly end) =&gt;
///         TryCreate(() =&gt; new DateRange(start, end));
///
///     protected override IEnumerable&lt;object?&gt; GetEqualityComponents()
///     {
///         yield return Start;
///         yield return End;
///     }
///
///     protected override IEnumerable&lt;Error&gt; Validate()
///     {
///         if (End &lt; Start)
///             yield return Error.Validation(
///                 "date_range.end_before_start", "The end date must not be before the start date.");
///     }
/// }
/// </code>
/// </example>
public abstract class ValueObject : IValueObject, IEquatable<ValueObject>
{
    /// <summary>
    /// Returns the components that define this value, always in the same order; equality and hashing compare
    /// these sequences.
    /// </summary>
    /// <returns>The equality components. Sequence components other than strings are compared by content.</returns>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <summary>Returns every validation error of this value, or an empty sequence when it is valid.</summary>
    /// <returns>
    /// The errors; <see langword="null"/> is treated as no errors. The sequence must not contain
    /// <see langword="null"/> entries.
    /// </returns>
    /// <remarks>Called by <see cref="EnsureValid"/> against the fully initialized object.</remarks>
    protected abstract IEnumerable<Error>? Validate();

    /// <summary>
    /// Throws when <see cref="Validate"/> reports any error; call it as the last statement of every constructor.
    /// </summary>
    /// <exception cref="ValidationException">
    /// <see cref="Validate"/> reported one or more errors; all are included.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="Validate"/> returned a sequence containing a <see langword="null"/> entry.
    /// </exception>
    protected void EnsureValid()
    {
        var errors = Validate()?.ToArray();
        if (errors is { Length: > 0 })
            throw new ValidationException(errors);
    }

    /// <summary>
    /// Returns whether <paramref name="obj"/> is a value object of the same runtime type with equal components.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the values are equal; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
            return true;

        if (obj is not ValueObject other || obj.GetType() != GetType())
            return false;

        using var left = GetEqualityComponents().GetEnumerator();
        using var right = other.GetEqualityComponents().GetEnumerator();

        while (true)
        {
            var leftHasNext = left.MoveNext();
            if (leftHasNext != right.MoveNext())
                return false;
            if (!leftHasNext)
                return true;
            if (!ComponentEquals(left.Current, right.Current))
                return false;
        }
    }

    /// <summary>
    /// Returns whether <paramref name="other"/> is a value object of the same runtime type with equal components.
    /// </summary>
    /// <param name="other">The value object to compare with.</param>
    /// <returns><see langword="true"/> when the values are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(ValueObject? other) => Equals((object?)other);

    /// <summary>Returns a hash code combining the runtime type and every equality component.</summary>
    /// <returns>A hash code consistent with <see cref="Equals(object)"/>.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GetType());
        foreach (var component in GetEqualityComponents())
            hash.Add(ComponentHashCode(component));
        return hash.ToHashCode();
    }

    /// <summary>Compares two value objects for equality; two <see langword="null"/> operands are equal.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>
    /// <see langword="true"/> when the values are equal or both are null; otherwise <see langword="false"/>.
    /// </returns>
    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Compares two value objects for inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);

    /// <summary>Throws when <paramref name="rule"/> is broken.</summary>
    /// <param name="rule">The business rule to enforce. Must not be null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException"><paramref name="rule"/> is broken.</exception>
    /// <remarks>
    /// Prefer <see cref="Validate"/> for checks on the value's own members, so every error is reported at once;
    /// a broken rule stops construction at the first violation.
    /// </remarks>
    protected static void CheckRule(IBusinessRule rule) => DomainInvariants.CheckRule(rule);

    /// <summary>
    /// Runs <paramref name="factory"/> and returns a failed <see cref="ValidationResult{T}"/> instead of throwing
    /// when construction fails for a domain reason.
    /// </summary>
    /// <typeparam name="T">The type the factory creates.</typeparam>
    /// <param name="factory">
    /// The construction delegate, such as <c>() =&gt; new DateRange(start, end)</c>. Must not be null.
    /// </param>
    /// <returns>
    /// A successful result holding the created value; a failed result holding every error of a
    /// <see cref="ValidationException"/>; or a failed result holding the single error of a
    /// <see cref="DomainException"/>, which includes <see cref="BusinessRuleViolationException"/> and guard
    /// violations.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <remarks>Any other exception propagates unchanged: it signals a defect, not invalid input.</remarks>
    protected static ValidationResult<T> TryCreate<T>(Func<T> factory) => DomainInvariants.TryCreate(factory);

    private static bool ComponentEquals(object? left, object? right)
    {
        if (left is IEnumerable leftSequence and not string && right is IEnumerable rightSequence and not string)
            return leftSequence.Cast<object?>().SequenceEqual(rightSequence.Cast<object?>());

        return Equals(left, right);
    }

    private static int ComponentHashCode(object? component)
    {
        if (component is IEnumerable sequence and not string)
        {
            var hash = new HashCode();
            foreach (var item in sequence)
                hash.Add(item);
            return hash.ToHashCode();
        }

        return component?.GetHashCode() ?? 0;
    }
}
