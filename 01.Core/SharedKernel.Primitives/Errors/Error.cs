namespace SharedKernel.Primitives.Errors;

/// <summary>
/// Represents a structured error produced by an operation. Carries a string code, a human-readable
/// message, and an <see cref="ErrorType"/> discriminator.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="None"/> as the sentinel value meaning "no error occurred". Never use <c>null</c>
/// to represent the absence of an error.
/// </para>
/// <para>
/// All factory methods are static and return a new <see cref="Error"/> with the appropriate
/// <see cref="ErrorType"/>. <see cref="Error"/> is a sealed record — value equality is structural
/// (Code + Message + Type).
/// </para>
/// </remarks>
/// <param name="Code">A stable, machine-readable identifier for this error (e.g., <c>"validation.required"</c>).</param>
/// <param name="Message">A human-readable description of the error.</param>
/// <param name="Type">The category of this error.</param>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>
    /// The sentinel value representing "no error". Use this wherever an <see cref="Error"/> is
    /// required but no error has occurred — never use <c>null</c>.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>Creates an <see cref="ErrorType.Unexpected"/> error.</summary>
    /// <param name="code">A stable machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error Unexpected(string code, string message)
        => new(code, message, ErrorType.Unexpected);

    /// <summary>Creates an <see cref="ErrorType.Validation"/> error.</summary>
    /// <param name="code">A stable machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error Validation(string code, string message)
        => new(code, message, ErrorType.Validation);

    /// <summary>Creates an <see cref="ErrorType.NotFound"/> error.</summary>
    /// <param name="code">A stable machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error NotFound(string code, string message)
        => new(code, message, ErrorType.NotFound);

    /// <summary>Creates an <see cref="ErrorType.Conflict"/> error.</summary>
    /// <param name="code">A stable machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error Conflict(string code, string message)
        => new(code, message, ErrorType.Conflict);

    /// <summary>Creates an <see cref="ErrorType.Unauthorized"/> error.</summary>
    /// <param name="code">A stable machine-readable identifier.</param>
    /// <param name="message">A human-readable description.</param>
    public static Error Unauthorized(string code, string message)
        => new(code, message, ErrorType.Unauthorized);

    /// <summary>
    /// Creates an <see cref="ErrorType.BusinessRule"/> error representing a domain invariant
    /// or business rule violation.
    /// </summary>
    /// <param name="code">
    /// A stable machine-readable identifier for the violated rule (e.g.,
    /// <see cref="ErrorCodes.Domain.RuleViolated"/>).
    /// </param>
    /// <param name="message">A human-readable description of the violated rule.</param>
    /// <returns>
    /// An <see cref="Error"/> with <see cref="Error.Type"/> set to
    /// <see cref="ErrorType.BusinessRule"/>. Maps to HTTP 422 Unprocessable Entity at the
    /// presentation layer.
    /// </returns>
    public static Error BusinessRule(string code, string message)
        => new(code, message, ErrorType.BusinessRule);

    /// <summary>
    /// Creates an <see cref="ErrorType.Forbidden"/> error representing a caller who is generally
    /// permitted to attempt this kind of operation, but for whom this specific instance/condition
    /// is not satisfied.
    /// </summary>
    /// <param name="code">A stable machine-readable identifier for the violated gate.</param>
    /// <param name="message">A human-readable description of why the operation is forbidden.</param>
    /// <returns>
    /// An <see cref="Error"/> with <see cref="Error.Type"/> set to <see cref="ErrorType.Forbidden"/>.
    /// Maps to HTTP 403 Forbidden at the presentation layer. Distinct from
    /// <see cref="Unauthorized"/>, which means the caller is not permitted to attempt this at all.
    /// </returns>
    public static Error Forbidden(string code, string message)
        => new(code, message, ErrorType.Forbidden);
}
