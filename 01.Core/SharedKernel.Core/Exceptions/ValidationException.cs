using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// Thrown when input fails one or more validation rules. Carries every failing <see cref="Error"/>, not just
/// the first.
/// </summary>
/// <remarks>
/// <para>
/// Prefer returning a failed <see cref="Result{T}"/> or <see cref="ValidationResult"/>. Throw this at an
/// exception boundary, such as a pipeline behavior serving callers that do not consume results.
/// </para>
/// <para>
/// <see cref="SharedKernelException.Error"/> is the first error, and <see cref="Exception.Message"/> is its
/// message when there is one error, or all messages joined with <c>"; "</c> when there are several. A
/// presentation layer typically renders <see cref="Errors"/> as per-field details in an HTTP 400 response.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// ValidationResult validation = Guard.Collect(
///     Guard.Against.NullOrWhiteSpace(command.Name),
///     Guard.Against.Email(command.Email));
///
/// if (!validation.IsValid)
///     throw new ValidationException(validation.Errors);
/// </code>
/// </example>
public sealed class ValidationException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="ValidationException"/> for a single failing rule.</summary>
    /// <param name="error">The validation error.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public ValidationException(Error error)
        : this(new[] { error ?? throw new ArgumentNullException(nameof(error)) })
    {
    }

    /// <summary>Initialises a new <see cref="ValidationException"/> for several failing rules.</summary>
    /// <remarks>The errors are copied, so changing the supplied list afterwards does not change the exception.</remarks>
    /// <param name="errors">Every validation error, in the order to report them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty or contains a <see langword="null"/> entry.</exception>
    public ValidationException(IReadOnlyList<Error> errors)
        : this(Snapshot(errors))
    {
    }

    private ValidationException(Error[] errors)
        : base(BuildMessage(errors), errors[0])
    {
        Errors = errors;
    }

    /// <summary>Gets every validation error, in the order supplied. Never empty.</summary>
    public IReadOnlyList<Error> Errors { get; }

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
