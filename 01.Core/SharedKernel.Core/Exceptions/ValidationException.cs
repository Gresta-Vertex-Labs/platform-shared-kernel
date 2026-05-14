using SharedKernel.Primitives.Errors;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when one or more input validation rules fail.
/// Bridges the <see cref="ValidationResult"/> multi-error model with the exception world.
/// </summary>
/// <remarks>
/// Map this exception to an HTTP 400 Bad Request (or equivalent) at the presentation layer.
/// When a single validation failure is represented as a <see cref="Results.Result{T}"/> failure,
/// prefer returning a failed result rather than throwing. Use this exception only at exception
/// boundaries (e.g., a validation pipeline behavior converting a failed
/// <see cref="Primitives.Results.ValidationResult"/> to an exception for callers that do not
/// consume the result railway).
/// </remarks>
public sealed class ValidationException : SharedKernelException
{
    /// <summary>
    /// Initialises a new <see cref="ValidationException"/> from a collection of validation errors.
    /// </summary>
    /// <param name="errors">
    /// All validation errors that were found. Must contain at least one entry.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors"/> is empty.</exception>
    public ValidationException(IReadOnlyList<Error> errors)
        : base(BuildMessage(errors), BuildPrimaryError(errors))
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
            throw new ArgumentException("At least one error is required.", nameof(errors));

        Errors = errors;
    }

    /// <summary>Gets all validation errors that caused this exception.</summary>
    public IReadOnlyList<Error> Errors { get; }

    private static string BuildMessage(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 1
            ? errors[0].Message
            : $"Multiple validation errors occurred: {string.Join("; ", errors.Select(e => e.Message))}";
    }

    private static Error BuildPrimaryError(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count > 0 ? errors[0] : Error.None;
    }
}
