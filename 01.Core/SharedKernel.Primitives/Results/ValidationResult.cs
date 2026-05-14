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
/// </remarks>
public sealed record ValidationResult
{
    private static readonly IReadOnlyList<Error> _noErrors = [];

    private ValidationResult(bool isValid, IReadOnlyList<Error> errors)
    {
        IsValid = isValid;
        Errors  = errors;
    }

    /// <summary>Gets a value indicating whether validation passed (no errors).</summary>
    public bool IsValid { get; }

    /// <summary>Gets the collection of errors found during validation. Empty when <see cref="IsValid"/> is <c>true</c>.</summary>
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>Creates a successful <see cref="ValidationResult"/> with no errors.</summary>
    public static ValidationResult Success() => new(true, _noErrors);

    /// <summary>
    /// Creates a failed <see cref="ValidationResult"/> containing the supplied <paramref name="errors"/>.
    /// </summary>
    /// <param name="errors">One or more validation errors. Must not be empty.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors"/> is empty.</exception>
    public static ValidationResult Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
            throw new ArgumentException("At least one error is required for a failed ValidationResult.", nameof(errors));

        return new(false, errors);
    }
}

/// <summary>
/// Represents the aggregate outcome of a multi-step validation operation that, on success,
/// produces a value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of the validated value.</typeparam>
/// <remarks>
/// <para>
/// Use <see cref="ValidationResult{T}"/> when validation produces a parsed or transformed value
/// (e.g., a validated command DTO). When validation fails, <see cref="Value"/> is the default
/// for <typeparamref name="T"/> — check <see cref="IsValid"/> before accessing it.
/// </para>
/// </remarks>
public sealed record ValidationResult<T>
{
    private static readonly IReadOnlyList<Error> _noErrors = [];

    private readonly T? _value;

    private ValidationResult(bool isValid, T? value, IReadOnlyList<Error> errors)
    {
        IsValid = isValid;
        _value  = value;
        Errors  = errors;
    }

    /// <summary>Gets a value indicating whether validation passed (no errors).</summary>
    public bool IsValid { get; }

    /// <summary>Gets the collection of errors found during validation. Empty when <see cref="IsValid"/> is <c>true</c>.</summary>
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
    public static ValidationResult<T> Success(T value) => new(true, value, _noErrors);

    /// <summary>
    /// Creates a failed <see cref="ValidationResult{T}"/> containing the supplied <paramref name="errors"/>.
    /// </summary>
    /// <param name="errors">One or more validation errors. Must not be empty.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors"/> is empty.</exception>
    public static ValidationResult<T> Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
            throw new ArgumentException("At least one error is required for a failed ValidationResult.", nameof(errors));

        return new(false, default, errors);
    }
}
