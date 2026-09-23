namespace SharedKernel.Primitives.Errors;

/// <summary>
/// The category of an <see cref="Error"/>, which decides the HTTP status code it becomes at the
/// API boundary.
/// </summary>
/// <remarks>
/// <para>
/// <b>Choose by meaning, not by convenience.</b> <c>14.Presentation</c> maps this to a status code
/// mechanically, so picking the nearest-looking member is how a service ends up telling a client to
/// re-authenticate when re-authenticating cannot help, or returning 500 for a business rule.
/// </para>
/// <list type="table">
///   <listheader><term>Member</term><description>Means / HTTP</description></listheader>
///   <item><term><see cref="Validation"/></term><description>Input was malformed or missing, caught before the domain ran — 400</description></item>
///   <item><term><see cref="Unauthorized"/></term><description>Caller may not attempt this at all; no or invalid credentials — 401</description></item>
///   <item><term><see cref="Forbidden"/></term><description>Caller is authenticated but not permitted this instance — 403</description></item>
///   <item><term><see cref="NotFound"/></term><description>Resource does not exist — 404</description></item>
///   <item><term><see cref="Conflict"/></term><description>Clashes with existing state; duplicate or concurrency — 409</description></item>
///   <item><term><see cref="BusinessRule"/></term><description>Well-formed but violates a domain invariant — 422</description></item>
///   <item><term><see cref="Unexpected"/></term><description>Unclassified fault — 500</description></item>
///   <item><term><see cref="Unavailable"/></term><description>A dependency or the service is temporarily unable to serve; retry later — 503</description></item>
///   <item><term><see cref="Timeout"/></term><description>The operation ran out of time; its outcome may be unknown — 504</description></item>
/// </list>
/// <para>
/// <b>Numeric values are a wire contract.</b> They are explicit, and must never be renumbered or
/// reassigned: this enum is serialized and persisted across the platform, so changing a value
/// reinterprets already-stored data. Adding a member is additive and safe.
/// </para>
/// <para>
/// <b>Adding a member is a source-compatible break for exhaustive switches.</b> A consumer with a
/// <c>switch</c> over this enum and no discard arm stops compiling when a member is added. That is
/// a compile-time signal rather than a runtime failure — add a <c>_ =&gt;</c> arm to stay
/// future-proof — but it means new members ship in a MAJOR release note, as
/// <see cref="Forbidden"/>, <see cref="Unavailable"/> and <see cref="Timeout"/> did.
/// </para>
/// </remarks>
public enum ErrorType
{
    /// <summary>No error. Used only by <see cref="Error.None"/>.</summary>
    None = 0,

    /// <summary>
    /// An unclassified or unexpected fault — an unhandled exception, or an external dependency
    /// failing in a way the caller cannot act on. Maps to HTTP 500.
    /// </summary>
    /// <remarks>
    /// The fallback when nothing else fits, so an <see cref="Error"/> is never left uncategorised.
    /// A high rate of these in production usually means a real failure class is missing its own
    /// member, not that the system is merely unlucky. A dependency that is down, throttling, or too
    /// slow is not unclassified: that is <see cref="Unavailable"/> or <see cref="Timeout"/>.
    /// </remarks>
    Unexpected = 1,

    /// <summary>
    /// Input did not pass validation — a missing required field, a bad format, a value out of
    /// range. Maps to HTTP 400.
    /// </summary>
    /// <remarks>
    /// About the SHAPE of the request, detected before domain logic runs. If the caller could fix
    /// it by correcting a field, it belongs here; if the request is well-formed and the domain is
    /// refusing, use <see cref="BusinessRule"/>.
    /// </remarks>
    Validation = 2,

    /// <summary>A requested resource could not be located. Maps to HTTP 404.</summary>
    NotFound = 3,

    /// <summary>
    /// The operation conflicts with existing state — a duplicate key, or an optimistic-concurrency
    /// violation. Maps to HTTP 409.
    /// </summary>
    /// <remarks>
    /// Retryable in principle, unlike <see cref="Validation"/>: the same request may succeed once
    /// the conflicting state changes.
    /// </remarks>
    Conflict = 4,

    /// <summary>
    /// The caller may not attempt the operation at all — credentials are absent, invalid, or
    /// expired. Maps to HTTP 401.
    /// </summary>
    /// <remarks>
    /// Answers "who are you?". If the caller's identity is established and the refusal is about
    /// what that identity may do, the member you want is <see cref="Forbidden"/>. Substituting one
    /// for the other makes the two indistinguishable in logs and misdirects the client.
    /// </remarks>
    Unauthorized = 5,

    /// <summary>
    /// A domain invariant or business rule was violated — the request was syntactically valid but
    /// the domain refuses it, e.g. an order cannot be cancelled after it has shipped. Maps to
    /// HTTP 422.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Validation"/> (input shape, checked before the domain) and from
    /// <see cref="Unexpected"/> (a fault rather than a decision). This one is the domain working
    /// correctly and saying no.
    /// </remarks>
    BusinessRule = 6,

    /// <summary>
    /// The caller is authenticated and may generally attempt this kind of operation, but this
    /// specific instance or condition is not permitted. Maps to HTTP 403.
    /// </summary>
    /// <remarks>
    /// Answers "may you do THIS?" rather than "who are you?". Typical cases: a role or permission
    /// gate refusing an under-privileged caller, or a maker-checker rule refusing the same user who
    /// submitted the request. Not a domain-invariant violation — that is
    /// <see cref="BusinessRule"/>.
    /// </remarks>
    Forbidden = 7,

    /// <summary>
    /// A dependency, or the service itself, is temporarily unable to serve the request — a broker
    /// or engine that cannot be reached, a provider that is throttling or failing. Retrying later
    /// may succeed. Maps to HTTP 503 and gRPC <c>Unavailable</c>.
    /// </summary>
    /// <remarks>
    /// An operational condition, not a defect: the request itself was fine, and the same call can
    /// succeed unchanged once the dependency recovers. That is what separates it from
    /// <see cref="Unexpected"/>. If the operation ran out of time rather than being refused, so its
    /// outcome may be unknown, the member you want is <see cref="Timeout"/>.
    /// </remarks>
    Unavailable = 8,

    /// <summary>
    /// The operation exceeded its time budget, and its outcome may be unknown — the work may still
    /// complete after the caller stopped waiting. Maps to HTTP 504 and gRPC
    /// <c>DeadlineExceeded</c>.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Unavailable"/>, where the dependency refused the call or could not
    /// be reached, so nothing happened. After a timeout a write may have landed: retry only an
    /// idempotent operation, or check the outcome first.
    /// </remarks>
    Timeout = 9,
}
