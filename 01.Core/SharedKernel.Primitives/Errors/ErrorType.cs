namespace SharedKernel.Primitives.Errors;

/// <summary>
/// Classifies the kind of error represented by an <see cref="Error"/>.
/// </summary>
public enum ErrorType
{
    /// <summary>No error. Used exclusively by <see cref="Error.None"/>.</summary>
    None = 0,

    /// <summary>An unexpected or unclassified failure (e.g., unhandled exception, external service fault).</summary>
    Unexpected = 1,

    /// <summary>Input did not pass validation rules.</summary>
    Validation = 2,

    /// <summary>A requested resource could not be located.</summary>
    NotFound = 3,

    /// <summary>The operation conflicts with existing state (e.g., duplicate key, optimistic concurrency violation).</summary>
    Conflict = 4,

    /// <summary>The caller is not authorized to perform the operation.</summary>
    Unauthorized = 5,

    /// <summary>
    /// A domain invariant or business rule was violated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Maps to HTTP 422 Unprocessable Entity at the presentation layer — the request was
    /// syntactically valid but could not be processed because it violates a domain rule
    /// (e.g., an order cannot be cancelled after it has shipped).
    /// </para>
    /// <para>
    /// Semantically distinct from <see cref="Validation"/>, which represents input
    /// format/presence errors caught before the domain layer executes, and from
    /// <see cref="Unexpected"/>, which represents unclassified system faults.
    /// </para>
    /// </remarks>
    BusinessRule = 6,

    /// <summary>
    /// The caller is generally permitted to attempt this kind of operation, but this specific
    /// instance/condition is not satisfied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Maps to HTTP 403 Forbidden at the presentation layer — e.g., a maker-checker dual-approval
    /// gate rejecting the same user who submitted the request, or a role/permission attribute
    /// rejecting an authenticated-but-under-privileged caller.
    /// </para>
    /// <para>
    /// Semantically distinct from <see cref="Unauthorized"/>, which means the caller is not
    /// permitted to attempt this at all (HTTP 401 — no/invalid credentials), and from
    /// <see cref="BusinessRule"/>, which represents a domain invariant violation rather than an
    /// authorization/permission gate.
    /// </para>
    /// </remarks>
    Forbidden = 7,
}
