namespace SharedKernel.Application.Behaviors.Authorization;

/// <summary>
/// Marks a command or query as requiring an authorization check before its handler runs.
/// </summary>
/// <remarks>
/// <para>
/// Requests that do not implement this marker skip
/// <see cref="Authorization.AuthorizationBehavior{TRequest,TResponse}"/> entirely — a DI/runtime
/// fact (the behavior never resolves into that request's pipeline), not a config flag or an
/// if-check inside the behavior. Can be implemented by both commands and queries — unlike
/// Transaction/Idempotency, authorization is not commands-only; queries can require permission
/// checks too (e.g. "view another tenant's data").
/// </para>
/// <para>
/// <b>Multi-requirement shapes (WO-038, P-232):</b>
/// <list type="bullet">
///   <item>
///     <term><see cref="AllOfRequirements"/></term>
///     <description>All listed requirements must pass (AND composition). Evaluated first by
///     <see cref="AuthorizationBehavior{TRequest,TResponse}"/>; short-circuits on the first
///     failure.</description>
///   </item>
///   <item>
///     <term><see cref="AnyOfRequirements"/></term>
///     <description>At least one listed requirement must pass (OR composition). Evaluated after
///     <see cref="AllOfRequirements"/> passes; short-circuits on the first passing
///     requirement.</description>
///   </item>
/// </list>
/// Both collections default to empty — only non-empty collections trigger evaluation.
/// The single-string <see cref="Requirement"/> convenience property remains available via a
/// default interface member; new implementations should prefer
/// <see cref="AllOfRequirements"/>/<see cref="AnyOfRequirements"/> directly.
/// </para>
/// </remarks>
public interface IAuthorizeRequest
{
    /// <summary>
    /// Gets the set of authorization requirements where <b>all</b> must be satisfied for the
    /// request to proceed.
    /// </summary>
    /// <remarks>
    /// An empty collection means no AllOf requirements are declared for this request.
    /// <see cref="AuthorizationBehavior{TRequest,TResponse}"/> skips AllOf evaluation when this
    /// collection is empty.
    /// </remarks>
    IReadOnlyCollection<string> AllOfRequirements => [];

    /// <summary>
    /// Gets the set of authorization requirements where <b>at least one</b> must be satisfied
    /// for the request to proceed.
    /// </summary>
    /// <remarks>
    /// An empty collection means no AnyOf requirements are declared for this request.
    /// <see cref="AuthorizationBehavior{TRequest,TResponse}"/> skips AnyOf evaluation when this
    /// collection is empty.
    /// </remarks>
    IReadOnlyCollection<string> AnyOfRequirements => [];

    /// <summary>
    /// Gets the single authorization requirement for this request (convenience property).
    /// </summary>
    /// <remarks>
    /// Equivalent to declaring <see cref="AllOfRequirements"/> = <c>[ Requirement ]</c>. Provided
    /// for backward compatibility and single-requirement convenience. New implementations should
    /// prefer <see cref="AllOfRequirements"/> / <see cref="AnyOfRequirements"/> directly.
    /// Returns an empty string by default, which means no single-requirement check is active
    /// unless the implementing type overrides this property.
    /// </remarks>
    string Requirement => string.Empty;
}
