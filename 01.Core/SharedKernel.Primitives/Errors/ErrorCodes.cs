namespace SharedKernel.Primitives.Errors;

/// <summary>
/// Well-known, stable error code constants organized by category.
/// </summary>
/// <remarks>
/// <para>
/// Each constant is a string that serves as the <see cref="Error.Code"/> value. Using string
/// constants (rather than an enum) allows consuming packages to define additional local constants
/// in their own namespaces without forking SharedKernel or causing versioning problems.
/// </para>
/// <para>
/// Convention: codes use dot-separated lowercase segments, e.g. <c>"validation.required"</c>.
/// </para>
/// </remarks>
public static class ErrorCodes
{
    /// <summary>Codes for input validation failures.</summary>
    public static class Validation
    {
        /// <summary>A required field or parameter was not provided.</summary>
        public const string Required = "validation.required";

        /// <summary>A value falls outside the permitted range.</summary>
        public const string OutOfRange = "validation.out_of_range";

        /// <summary>A value does not conform to the expected format.</summary>
        public const string InvalidFormat = "validation.invalid_format";

        /// <summary>A value exceeds the maximum permitted length.</summary>
        public const string MaxLength = "validation.max_length";

        /// <summary>A value is below the minimum permitted length.</summary>
        public const string MinLength = "validation.min_length";
    }

    /// <summary>Codes for resource-not-found failures.</summary>
    public static class NotFound
    {
        /// <summary>The requested resource could not be found.</summary>
        public const string Default = "not_found.default";
    }

    /// <summary>Codes for conflict failures.</summary>
    public static class Conflict
    {
        /// <summary>The operation conflicts with existing state.</summary>
        public const string Default = "conflict.default";

        /// <summary>A duplicate entry was detected.</summary>
        public const string Duplicate = "conflict.duplicate";
    }

    /// <summary>Codes for authorization failures.</summary>
    public static class Unauthorized
    {
        /// <summary>The caller does not have permission to perform the operation.</summary>
        public const string Default = "unauthorized.default";

        /// <summary>The caller's credentials have expired.</summary>
        public const string Expired = "unauthorized.expired";
    }

    /// <summary>Codes for unexpected / unclassified failures.</summary>
    public static class Unexpected
    {
        /// <summary>An unclassified or unexpected failure occurred.</summary>
        public const string Default = "unexpected.default";
    }

    /// <summary>
    /// Codes for domain invariant and business rule violations.
    /// </summary>
    /// <remarks>
    /// Use these constants as the <see cref="Error.Code"/> value when raising
    /// <see cref="ErrorType.BusinessRule"/> errors. Consuming packages may define additional
    /// domain-specific rule codes in their own namespaces; this class provides the canonical
    /// well-known constant that bridges <c>SharedKernel.Primitives</c> and
    /// <c>SharedKernel.Domain</c> without introducing a package dependency.
    /// </remarks>
    public static class Domain
    {
        /// <summary>
        /// A domain business rule or invariant was violated.
        /// Canonical code for <c>BusinessRuleViolationException</c> in
        /// <c>SharedKernel.Domain</c>.
        /// </summary>
        public const string RuleViolated = "domain.rule.violated";
    }
}
