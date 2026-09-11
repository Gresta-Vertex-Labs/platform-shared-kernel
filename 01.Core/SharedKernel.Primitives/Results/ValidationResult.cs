using System.Diagnostics;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Primitives.Results;

/// <summary>
/// Represents the aggregate outcome of a multi-step validation operation.
/// Unlike <see cref="Result{T}"/>, which carries a single <see cref="Error"/>,
/// <see cref="ValidationResult"/> can carry multiple errors collected across
/// all validation rules.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="ValidationResult"/> for compound input validation scenarios (e.g., validating
/// an entire command object). Use <see cref="Result{T}"/> for single-error operation outcomes.
/// Never conflate the two: they model different failure cardinalities.
/// </para>
/// <para>
/// A <see cref="ValidationResult"/> is valid when <see cref="IsValid"/> is <c>true</c>
/// and <see cref="Errors"/> is empty.
/// </para>
/// <para>
/// <b>Immutable by copy, and equal by value.</b> <see cref="Failure(IReadOnlyList{Error})"/>
/// snapshots the supplied sequence, so a caller that keeps mutating its own list afterwards cannot
/// change an already-constructed result. Equality compares the error sequence element by element,
/// so two results built from equal errors are equal. Both behaviours are what a
/// <c>record</c> declaration promises and neither is what the compiler generates for an
/// <see cref="IReadOnlyList{T}"/> member — see the type-level remarks on
/// <see cref="ValidationResult{T}"/> for the same reasoning applied there.
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed record ValidationResult
{
    private static readonly Error[] NoErrors = [];

    private ValidationResult(bool isValid, IReadOnlyList<Error> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    /// <summary>Gets a value indicating whether validation passed (no errors).</summary>
    public bool IsValid { get; }

    /// <summary>Gets the collection of errors found during validation. Empty when <see cref="IsValid"/> is <c>true</c>.</summary>
    /// <remarks>
    /// Always a private snapshot taken at construction time — never the caller's own list
    /// instance, so it cannot change after the fact.
    /// </remarks>
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>Creates a successful <see cref="ValidationResult"/> with no errors.</summary>
    public static ValidationResult Success() => new(true, NoErrors);

    /// <summary>
    /// Creates a failed <see cref="ValidationResult"/> containing the supplied <paramref name="errors"/>.
    /// </summary>
    /// <param name="errors">
    /// One or more validation errors. Must not be empty. The sequence is copied, so later
    /// mutations of the argument do not affect the returned result.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="errors"/> is empty, or contains a <see langword="null"/> element.
    /// </exception>
    public static ValidationResult Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var snapshot = ValidationErrors.Snapshot(errors);
        return new(false, snapshot);
    }

    /// <inheritdoc/>
    public bool Equals(ValidationResult? other) =>
        other is not null
        && IsValid == other.IsValid
        && ValidationErrors.SequenceEquals(Errors, other.Errors);

    /// <inheritdoc/>
    public override int GetHashCode() => ValidationErrors.CombineHashCode(IsValid, Errors);

    private string DebuggerDisplay =>
        IsValid ? "Valid" : $"Invalid: {Errors.Count} error(s)";
}

/// <summary>
/// Represents the aggregate outcome of a multi-step validation operation that, on success,
/// produces a value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of the validated value.</typeparam>
/// <remarks>
/// <para>
/// Use <see cref="ValidationResult{T}"/> when validation produces a parsed or transformed value
/// (e.g., a validated command DTO). When validation fails, accessing <see cref="Value"/> throws —
/// check <see cref="IsValid"/> first.
/// </para>
/// <para>
/// Like its non-generic sibling, this type snapshots the supplied error sequence and compares it
/// element by element. Its <see cref="Value"/> takes part in equality using the default comparer
/// for <typeparamref name="T"/>, so a <typeparamref name="T"/> without value equality of its own
/// still compares by reference — the usual <c>record</c> semantics for a member of that shape.
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed record ValidationResult<T>
{
    private static readonly Error[] NoErrors = [];

    private readonly T? _value;

    private ValidationResult(bool isValid, T? value, IReadOnlyList<Error> errors)
    {
        IsValid = isValid;
        _value = value;
        Errors = errors;
    }

    /// <summary>Gets a value indicating whether validation passed (no errors).</summary>
    public bool IsValid { get; }

    /// <summary>Gets the collection of errors found during validation. Empty when <see cref="IsValid"/> is <c>true</c>.</summary>
    /// <remarks>
    /// Always a private snapshot taken at construction time — never the caller's own list
    /// instance, so it cannot change after the fact.
    /// </remarks>
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>
    /// Gets the validated value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation failed. Check <see cref="IsValid"/> first.</exception>
    public T Value => IsValid
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed ValidationResult. Check IsValid before accessing Value.");

    /// <summary>Creates a successful <see cref="ValidationResult{T}"/> carrying the specified <paramref name="value"/>.</summary>
    /// <param name="value">The validated value.</param>
    public static ValidationResult<T> Success(T value) => new(true, value, NoErrors);

    /// <summary>
    /// Creates a failed <see cref="ValidationResult{T}"/> containing the supplied <paramref name="errors"/>.
    /// </summary>
    /// <param name="errors">
    /// One or more validation errors. Must not be empty. The sequence is copied, so later
    /// mutations of the argument do not affect the returned result.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="errors"/> is empty, or contains a <see langword="null"/> element.
    /// </exception>
    public static ValidationResult<T> Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var snapshot = ValidationErrors.Snapshot(errors);
        return new(false, default, snapshot);
    }

    /// <inheritdoc/>
    public bool Equals(ValidationResult<T>? other) =>
        other is not null
        && IsValid == other.IsValid
        && EqualityComparer<T?>.Default.Equals(_value, other._value)
        && ValidationErrors.SequenceEquals(Errors, other.Errors);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(
            ValidationErrors.CombineHashCode(IsValid, Errors),
            _value is null ? 0 : EqualityComparer<T?>.Default.GetHashCode(_value)
        );

    // Reads the backing field, not the Value property, which throws when validation failed.
    private string DebuggerDisplay =>
        IsValid ? $"Valid: {_value}" : $"Invalid: {Errors.Count} error(s)";
}

/// <summary>
/// Shared snapshot/equality helpers for <see cref="ValidationResult"/> and
/// <see cref="ValidationResult{T}"/>.
/// </summary>
/// <remarks>
/// Internal on purpose. The behaviour it implements is part of the two public types' documented
/// contracts, not a separate capability, and exposing it would invite call sites to depend on
/// validation-result internals.
/// </remarks>
internal static class ValidationErrors
{
    /// <summary>
    /// Copies <paramref name="errors"/> into a private array, rejecting an empty sequence or a
    /// <see langword="null"/> element.
    /// </summary>
    internal static Error[] Snapshot(IReadOnlyList<Error> errors)
    {
        if (errors.Count == 0)
        {
            throw new ArgumentException(
                "At least one error is required for a failed ValidationResult.",
                nameof(errors)
            );
        }

        var snapshot = new Error[errors.Count];
        for (var i = 0; i < snapshot.Length; i++)
        {
            snapshot[i] =
                errors[i]
                ?? throw new ArgumentException(
                    $"The error at index {i} is null. Error.None expresses \"no error\"; null is "
                        + "never a valid Error.",
                    nameof(errors)
                );
        }

        return snapshot;
    }

    /// <summary>Compares two error sequences element by element.</summary>
    internal static bool SequenceEquals(IReadOnlyList<Error> left, IReadOnlyList<Error> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Builds a hash code consistent with <see cref="SequenceEquals"/>.</summary>
    internal static int CombineHashCode(bool isValid, IReadOnlyList<Error> errors)
    {
        var hash = new HashCode();
        hash.Add(isValid);
        hash.Add(errors.Count);

        for (var i = 0; i < errors.Count; i++)
        {
            hash.Add(errors[i]);
        }

        return hash.ToHashCode();
    }
}
