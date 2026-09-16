using System.Diagnostics;
using System.Text.Json.Serialization;

namespace SharedKernel.Primitives.Errors;

/// <summary>
/// A structured, expected failure: a stable machine-readable <see cref="Code"/>, a human-readable
/// <see cref="Message"/>, and an <see cref="ErrorType"/> saying what kind of failure it is.
/// </summary>
/// <remarks>
/// <para>
/// The platform's most-depended-upon type. It travels from wherever a failure is detected, through
/// <see cref="Results.Result{T}"/>, out to an RFC 9457 <c>ProblemDetails</c> response at
/// <c>14.Presentation</c> — so its three fields are a cross-service contract, not local detail.
/// </para>
/// <para>
/// A <c>sealed record</c>, so equality is by value across all three fields. Two errors built from
/// the same code, message, and type are equal and hash equally.
/// </para>
/// <para>
/// <b>Never use <see langword="null"/> for "no error" — use <see cref="None"/>.</b> This is
/// enforced, not merely asked: the failure factories on
/// <see cref="Results.Result{T}"/>/<see cref="Results.Result"/> reject a null error, and
/// <c>Result.Error</c> throws rather than hand one back.
/// </para>
/// <para>
/// <b><see cref="Code"/> is the field that matters most, and the one most often written
/// carelessly.</b> It is the stable identity of the failure, and three separate things key off it:
/// a consuming service branches on it, log dashboards and alert rules filter on it, and
/// <c>01.Core/SharedKernel.Localization</c> looks up a translated message by it. Which gives the
/// rules:
/// </para>
/// <list type="bullet">
///   <item><description>
///   Dot-separated lowercase, from general to specific — <c>"order.not_found"</c>,
///   <c>"validation.required"</c>. Check <see cref="ErrorCodes"/> first; a suitable constant often
///   already exists.
///   </description></item>
///   <item><description>
///   Stable once shipped. Changing a code silently breaks every consumer branch, saved search, and
///   translation entry pointing at the old one — treat it like renaming a public API member.
///   </description></item>
///   <item><description>
///   Never interpolate variable data into it (no <c>$"order.{id}.not_found"</c>). A code with an
///   id in it is unaggregatable and untranslatable. Identifiers belong in
///   <see cref="Message"/>.
///   </description></item>
/// </list>
/// <para>
/// <b><see cref="Message"/> is for a human, and may reach one.</b> Write it so it could be shown
/// to a caller, and keep secrets, credentials, connection strings, and raw exception text out of
/// it. It is also the fallback shown when no translation is registered for
/// <see cref="Code"/>, so a message reading <c>"see logs"</c> becomes an end user's error text.
/// </para>
/// <para>
/// <b>There is no metadata bag, deliberately.</b> Adding a dictionary for extension members
/// (a field path, a retry-after hint) has been evaluated and declined: it breaks this type's
/// value-equality contract and raises cross-process-serialization questions no other change here
/// has had to answer. Both motivating needs are already solved at the <c>ProblemDetails</c>
/// construction boundary in <c>14.Presentation</c> — that is the sanctioned place to attach
/// response-shaped extras.
/// </para>
/// <para>
/// <b><see cref="Details"/> is not a metadata bag.</b> It is a typed list of child errors, filled
/// only by <see cref="Validation(IReadOnlyList{Error})"/>, so a single failed <c>Result</c> can
/// report every invalid field at once. It takes part in equality element by element.
/// </para>
/// </remarks>
/// <param name="Code">Stable machine-readable identifier, e.g. <c>"validation.required"</c>.</param>
/// <param name="Message">Human-readable description of what went wrong.</param>
/// <param name="Type">The category of failure, which decides the HTTP status at the boundary.</param>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>
    /// The sentinel meaning "no error occurred". Use this wherever an <see cref="Error"/> is
    /// required but nothing failed — never <see langword="null"/>.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>
    /// Creates an <see cref="ErrorType.Unexpected"/> error — an unclassified fault: an unhandled
    /// exception, or an external service failing in a way this code cannot interpret. Maps to
    /// HTTP 500.
    /// </summary>
    /// <param name="code">Stable machine-readable identifier. <see cref="ErrorCodes.Unexpected.Default"/> if you have nothing more specific.</param>
    /// <param name="message">Human-readable description. Do not paste raw exception text here — it reaches callers.</param>
    public static Error Unexpected(string code, string message)
        => new(code, message, ErrorType.Unexpected);

    /// <summary>
    /// Creates an <see cref="ErrorType.Validation"/> error — input was malformed, missing, or out
    /// of range, caught before any domain logic ran. Maps to HTTP 400.
    /// </summary>
    /// <param name="code">Stable machine-readable identifier, e.g. <see cref="ErrorCodes.Validation.Required"/>.</param>
    /// <param name="message">Human-readable description of what is wrong with the input.</param>
    /// <remarks>
    /// For several field failures at once, collect them in a
    /// <see cref="Results.ValidationResult"/> rather than returning only the first.
    /// </remarks>
    public static Error Validation(string code, string message)
        => new(code, message, ErrorType.Validation);

    /// <summary>
    /// Creates one <see cref="ErrorType.Validation"/> error that carries several field failures in
    /// <see cref="Details"/>. Maps to HTTP 400.
    /// </summary>
    /// <param name="errors">The individual failures. Must contain at least one error and no <see langword="null"/> entry.</param>
    /// <returns>
    /// An error with code <see cref="ErrorCodes.Validation.Failed"/>, a message stating how many
    /// failures occurred, and a snapshot of <paramref name="errors"/> in <see cref="Details"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty or contains a <see langword="null"/> entry.</exception>
    /// <remarks>
    /// Use this where a <see cref="Results.Result"/> or <see cref="Results.Result{T}"/> must report
    /// every invalid field instead of the first one. The list is copied, so later changes to the
    /// caller's collection do not change the error.
    /// </remarks>
    public static Error Validation(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var details = Results.ValidationErrors.Snapshot(errors);
        var message = details.Length == 1
            ? "One validation error occurred."
            : $"{details.Length} validation errors occurred.";

        return new Error(ErrorCodes.Validation.Failed, message, ErrorType.Validation) { Details = details };
    }

    /// <summary>
    /// Gets the child errors this error aggregates. Empty unless the error was created by
    /// <see cref="Validation(IReadOnlyList{Error})"/>.
    /// </summary>
    /// <remarks>
    /// Survives a round trip through <see cref="System.Text.Json.JsonSerializer"/> using the
    /// default reflection-based serializer — including a nested <see cref="Error"/> inside
    /// <see cref="Details"/>, which itself carries <see cref="Details"/>. An absent or explicit
    /// <see langword="null"/> <c>details</c> JSON member deserializes to an empty list, never
    /// <see langword="null"/>.
    /// </remarks>
    [JsonInclude]
    public IReadOnlyList<Error> Details
    {
        get => _details;
        private init => _details = value ?? [];
    }

    private readonly IReadOnlyList<Error> _details = [];

    /// <summary>
    /// Compares code, message, type, and <see cref="Details"/> element by element.
    /// </summary>
    /// <param name="other">The error to compare with.</param>
    /// <returns><see langword="true"/> when both errors have equal values.</returns>
    public bool Equals(Error? other) =>
        other is not null
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && string.Equals(Message, other.Message, StringComparison.Ordinal)
        && Type == other.Type
        && Results.ValidationErrors.SequenceEquals(Details, other.Details);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Code, StringComparer.Ordinal);
        hash.Add(Message, StringComparer.Ordinal);
        hash.Add(Type);
        hash.Add(Details.Count);

        foreach (var detail in Details)
        {
            hash.Add(detail);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Creates an <see cref="ErrorType.NotFound"/> error — the requested resource does not exist.
    /// Maps to HTTP 404.
    /// </summary>
    /// <param name="code">Stable machine-readable identifier, e.g. <see cref="ErrorCodes.NotFound.Default"/>.</param>
    /// <param name="message">Human-readable description of what was not found.</param>
    public static Error NotFound(string code, string message)
        => new(code, message, ErrorType.NotFound);

    /// <summary>
    /// Creates an <see cref="ErrorType.Conflict"/> error — the operation clashes with existing
    /// state: a duplicate key, or an optimistic-concurrency violation. Maps to HTTP 409.
    /// </summary>
    /// <param name="code">Stable machine-readable identifier, e.g. <see cref="ErrorCodes.Conflict.Duplicate"/>.</param>
    /// <param name="message">Human-readable description of the conflict.</param>
    public static Error Conflict(string code, string message)
        => new(code, message, ErrorType.Conflict);

    /// <summary>
    /// Creates an <see cref="ErrorType.Unauthorized"/> error — the caller may not attempt this at
    /// all, because credentials are missing, invalid, or expired. Maps to HTTP 401.
    /// </summary>
    /// <param name="code">Stable machine-readable identifier, e.g. <see cref="ErrorCodes.Unauthorized.Expired"/>.</param>
    /// <param name="message">Human-readable description. Do not reveal why authentication failed in detail.</param>
    /// <remarks>
    /// <b>If the caller IS authenticated and merely lacks permission, use
    /// <see cref="Forbidden"/>.</b> Confusing the two makes an authorization failure
    /// indistinguishable from a missing credential in logs, and tells the client to re-authenticate
    /// when re-authenticating cannot help.
    /// </remarks>
    public static Error Unauthorized(string code, string message)
        => new(code, message, ErrorType.Unauthorized);

    /// <summary>
    /// Creates an <see cref="ErrorType.BusinessRule"/> error — the request was well-formed but
    /// violates a domain invariant, e.g. an order cannot be cancelled after it has shipped. Maps to
    /// HTTP 422.
    /// </summary>
    /// <param name="code">Stable machine-readable identifier for the violated rule, e.g. <see cref="ErrorCodes.Domain.RuleViolated"/>.</param>
    /// <param name="message">Human-readable description of the rule that was violated.</param>
    /// <remarks>
    /// Distinct from <see cref="Validation(string, string)"/>: validation is about the SHAPE of the input and runs
    /// before the domain; this is the domain itself refusing. If the caller could fix it by
    /// correcting a field, it is validation.
    /// </remarks>
    public static Error BusinessRule(string code, string message)
        => new(code, message, ErrorType.BusinessRule);

    /// <summary>
    /// Creates an <see cref="ErrorType.Forbidden"/> error — the caller is authenticated and may
    /// generally attempt this kind of operation, but not this instance under these conditions.
    /// Maps to HTTP 403.
    /// </summary>
    /// <param name="code">Stable machine-readable identifier, e.g. <see cref="ErrorCodes.Forbidden.InsufficientPermission"/>.</param>
    /// <param name="message">Human-readable description of why it is refused.</param>
    /// <remarks>
    /// Typical cases: a role or permission gate refusing an authenticated but under-privileged
    /// caller, or a maker-checker rule refusing the same user who submitted the request. Use
    /// <see cref="Unauthorized"/> only when the caller may not attempt the operation at all.
    /// </remarks>
    public static Error Forbidden(string code, string message)
        => new(code, message, ErrorType.Forbidden);

    // Code plus type, not the message: the code is what a consumer branches on and greps logs
    // for, and a message is long enough to push everything useful out of a watch-window row.
    private string DebuggerDisplay => Type == ErrorType.None
        ? "None"
        : Details.Count == 0 ? $"{Type}: {Code}" : $"{Type}: {Code} ({Details.Count} details)";
}
