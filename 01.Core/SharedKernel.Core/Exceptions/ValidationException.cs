using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when one or more input validation rules fail. Carries every failing <see cref="Error"/>.
/// </summary>
/// <remarks>
/// Prefer returning a failed <see cref="Result{T}"/> or <see cref="ValidationResult"/>. Throw this only at
/// an exception boundary, such as a pipeline behavior serving callers that do not consume results. A
/// presentation layer renders <see cref="Errors"/> as per-field validation details (HTTP 400).
/// </remarks>
public sealed class ValidationException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="ValidationException"/> from a single validation error.</summary>
    /// <param name="error">The validation error.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public ValidationException(Error error)
        : this(new[] { error ?? throw new ArgumentNullException(nameof(error)) })
    {
    }

    /// <summary>Initialises a new <see cref="ValidationException"/> from a collection of validation errors.</summary>
    /// <param name="errors">Every validation error found. Must contain at least one entry and no <see langword="null"/> entries.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="errors"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors"/> is empty or contains a <see langword="null"/> entry.</exception>
    public ValidationException(IReadOnlyList<Error> errors)
        : this(Snapshot(errors))
    {
    }

    private ValidationException(Error[] errors)
        : base(BuildMessage(errors), errors[0])
    {
        Errors = errors;
    }

    /// <summary>Gets every validation error that caused this exception, in the order supplied.</summary>
    public IReadOnlyList<Error> Errors { get; }

    // Copied so a caller mutating its own list afterwards cannot change this exception.
    private static Error[] Snapshot(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
            throw new ArgumentException("At least one error is required.", nameof(errors));

        var snapshot = new Error[errors.Count];
        for (var i = 0; i < snapshot.Length; i++)
            snapshot[i] = errors[i] ?? throw new ArgumentException("Errors must not contain null entries.", nameof(errors));

        return snapshot;
    }

    private static string BuildMessage(Error[] errors)
        => errors.Length == 1
            ? errors[0].Message
            : $"Multiple validation errors occurred: {string.Join("; ", errors.Select(e => e.Message))}";
}
