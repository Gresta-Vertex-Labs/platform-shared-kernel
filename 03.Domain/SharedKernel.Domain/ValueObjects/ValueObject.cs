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
/// Base class for value objects: immutable domain values with no identity, equal when all their equality
/// components are equal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Validation is explicit.</b> Assign every member in the constructor, then call
/// <see cref="EnsureValid"/> as the constructor's last statement. It runs <see cref="Validate"/> against the
/// fully initialized object and throws <see cref="ValidationException"/> carrying every error. The base
/// constructor never calls <see cref="Validate"/>, because a virtual call there would read the subclass's
/// members before its constructor assigned them. <see cref="SingleValueObject{TValue}"/> calls
/// <see cref="EnsureValid"/> for you.
/// </para>
/// <para>
/// <b>Create through a factory.</b> Keep the constructor private and expose a static <c>Create</c> that uses
/// <see cref="TryCreate{T}"/>, so invalid input becomes a failed <see cref="ValidationResult{T}"/> holding
/// every error instead of an exception.
/// </para>
/// <para>
/// <b>Collection components compare by content.</b> A component that is a sequence (other than a
/// <see cref="string"/>) is compared element by element, so two value objects holding equal lists are equal.
/// Expose such a collection as a read-only type, since a value object must never change after construction.
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
///             yield return Error.Validation("date_range.end_before_start", "The end date must not be before the start date.");
///     }
/// }
/// </code>
/// </example>
public abstract class ValueObject : IValueObject, IEquatable<ValueObject>
{
    /// <summary>
    /// Returns the components that define this value, in a fixed order. Two value objects of the same type
    /// are equal when these sequences are equal.
    /// </summary>
    /// <returns>The equality components.</returns>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <summary>
    /// Returns the validation errors of this value, or an empty sequence when it is valid. Called by
    /// <see cref="EnsureValid"/>.
    /// </summary>
    /// <returns>The errors, if any. <see langword="null"/> is treated as no errors.</returns>
    protected abstract IEnumerable<Error>? Validate();

    /// <summary>
    /// Throws when <see cref="Validate"/> reports any error. Call it as the last statement of the constructor.
    /// </summary>
    /// <exception cref="ValidationException">Validation reported one or more errors; all are included.</exception>
    protected void EnsureValid()
    {
        var errors = Validate()?.ToArray();
        if (errors is { Length: > 0 })
            throw new ValidationException(errors);
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="obj"/> is a value object of the same type with equal components.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when equal; otherwise <see langword="false"/>.</returns>
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

    /// <summary>Returns <see langword="true"/> when <paramref name="other"/> is a value object of the same type with equal components.</summary>
    /// <param name="other">The value object to compare with.</param>
    /// <returns><see langword="true"/> when equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(ValueObject? other) => Equals((object?)other);

    /// <summary>Returns a hash code combining every equality component.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GetType());
        foreach (var component in GetEqualityComponents())
            hash.Add(ComponentHashCode(component));
        return hash.ToHashCode();
    }

    /// <summary>Returns <see langword="true"/> when both operands are equal, or both are null.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Returns <see langword="true"/> when the operands are not equal.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);

    /// <summary>Throws when <paramref name="rule"/> is broken.</summary>
    /// <param name="rule">The rule to enforce.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    /// <exception cref="BusinessRuleViolationException"><paramref name="rule"/> is broken.</exception>
    protected static void CheckRule(IBusinessRule rule) => DomainInvariants.CheckRule(rule);

    /// <summary>
    /// Runs <paramref name="factory"/> and turns a domain failure during construction into a failed
    /// <see cref="ValidationResult{T}"/> instead of an exception.
    /// </summary>
    /// <typeparam name="T">The type the factory creates.</typeparam>
    /// <param name="factory">The construction delegate, typically <c>() =&gt; new Email(value)</c>.</param>
    /// <returns>
    /// A successful result holding the created value; or a failed result holding every error from a
    /// <see cref="ValidationException"/>, or the single error of any <see cref="DomainException"/>, which
    /// includes <see cref="BusinessRuleViolationException"/> and guard violations.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <remarks>Any other exception propagates: it signals a defect, not invalid input.</remarks>
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
