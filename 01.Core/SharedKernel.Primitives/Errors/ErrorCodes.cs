namespace SharedKernel.Primitives.Errors;

/// <summary>
/// The platform's shared <see cref="Error.Code"/> constants, grouped by the kind of failure they
/// describe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Check here before inventing a code.</b> These cover the failures that recur across every
/// service, and reusing one means a dashboard or alert rule written against it works for your
/// service too — which is the whole point of a code being stable and shared.
/// </para>
/// <example>
/// <code>
/// return Error.Validation(ErrorCodes.Validation.Required, "Customer name is required.");
/// return Error.NotFound(ErrorCodes.NotFound.Default, $"Order {id} does not exist.");
/// </code>
/// </example>
/// <para>
/// <b>Your own codes belong in your own package.</b> These are deliberately <c>const string</c>
/// rather than an enum, so a consuming package declares its domain-specific codes in a local
/// constants class without needing a change here. Follow the same convention —
/// dot-separated lowercase, general to specific, e.g. <c>"order.already_shipped"</c> — and never
/// interpolate variable data into a code.
/// </para>
/// <para>
/// <b>These values are effectively permanent.</b> Consumers branch on them, log searches filter on
/// them, and <c>SharedKernel.Localization</c> keys translations off them, so changing one is a
/// breaking change across all three. Adding a code is safe.
/// </para>
/// <para>
/// Coverage is not uniform, and that is intentional rather than an oversight: a category gets a
/// constant when some package actually needed a shared one. <see cref="Error.BusinessRule"/> has a
/// single canonical code (<see cref="Domain.RuleViolated"/>) because domain rules are
/// service-specific by nature.
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

        /// <summary>
        /// One or more validation failures, carried in <see cref="Error.Details"/>. Used by
        /// <see cref="Error.Validation(System.Collections.Generic.IReadOnlyList{Error})"/>.
        /// </summary>
        public const string Failed = "validation.failed";
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

    /// <summary>
    /// Codes for failures where the caller is authenticated and generally permitted to attempt the
    /// operation, but a specific per-instance condition is not met.
    /// </summary>
    /// <remarks>
    /// Use these constants as the <see cref="Error.Code"/> value when raising
    /// <see cref="ErrorType.Forbidden"/> errors, which map to HTTP 403 at the presentation layer.
    /// Never reuse an <see cref="Unauthorized"/> code for a <see cref="ErrorType.Forbidden"/>
    /// error: the two answer different questions (401 "who are you?" versus 403 "you may not do
    /// this particular thing"), and conflating them makes an authorization failure
    /// indistinguishable from a missing-credentials failure in logs and dashboards.
    /// </remarks>
    public static class Forbidden
    {
        /// <summary>The operation is forbidden for this caller under the current conditions.</summary>
        public const string Default = "forbidden.default";

        /// <summary>
        /// The caller holds a valid identity but lacks the role or permission this operation
        /// requires — the code behind a failed declarative role/permission gate.
        /// </summary>
        public const string InsufficientPermission = "forbidden.insufficient_permission";
    }

    /// <summary>Codes for unexpected / unclassified failures.</summary>
    public static class Unexpected
    {
        /// <summary>
        /// The default code used by <c>SharedKernel.Core</c>'s <c>ResultTry</c> exception-boundary
        /// helpers (<c>Try</c>/<c>TryAsync</c>) when the caller supplies no custom exception-to-
        /// <see cref="Error"/> mapper. Also suitable as the general-purpose "an unclassified or
        /// unexpected failure occurred" code for any other <see cref="Error.Unexpected(string, string)"/>
        /// call site that has no more specific code of its own.
        /// </summary>
        public const string Default = "unexpected.exception";
    }

    /// <summary>
    /// Codes for failures where a dependency, or the service itself, is temporarily unable to serve
    /// the request.
    /// </summary>
    /// <remarks>
    /// Use these constants as the <see cref="Error.Code"/> value when raising
    /// <see cref="ErrorType.Unavailable"/> errors, which map to HTTP 503 at the presentation layer.
    /// A capability package usually has its own, more specific code — <c>storage.unavailable</c>,
    /// <c>messaging.unavailable</c> — and this is the general-purpose fallback.
    /// </remarks>
    public static class Unavailable
    {
        /// <summary>
        /// A dependency, or the service itself, is temporarily unable to serve the request; retrying
        /// later may succeed. The general-purpose code for any
        /// <see cref="Error.Unavailable(string, string)"/> call site that has no more specific code of
        /// its own.
        /// </summary>
        public const string Default = "unavailable.default";
    }

    /// <summary>Codes for failures where an operation exceeded its time budget.</summary>
    /// <remarks>
    /// Use these constants as the <see cref="Error.Code"/> value when raising
    /// <see cref="ErrorType.Timeout"/> errors, which map to HTTP 504 at the presentation layer.
    /// </remarks>
    public static class Timeout
    {
        /// <summary>
        /// The operation exceeded its time budget and its outcome may be unknown. The
        /// general-purpose code for any <see cref="Error.Timeout(string, string)"/> call site that
        /// has no more specific code of its own.
        /// </summary>
        public const string Default = "timeout.default";
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
