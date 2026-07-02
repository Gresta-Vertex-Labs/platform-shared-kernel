namespace SharedKernel.Application.Behaviors.Authorization;

/// <summary>
/// A minimal seam for evaluating whether the current caller satisfies authorization requirements.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> <c>SharedKernel.Security.Abstractions.IUserContext</c>, and this package
/// carries no project reference to <c>12.Security</c> — the layering ceiling for
/// <c>05.Application</c> is <c>01–04</c>. Exposes only the minimal shape
/// <see cref="Authorization.AuthorizationBehavior{TRequest,TResponse}"/> needs. The consuming
/// service bridges this seam to its real <c>IUserContext</c>/<c>ITenantProvider</c> at the
/// composition root — the exact same bridging pattern <c>TransactionBehavior</c>'s local
/// <c>IUnitOfWork</c> already established for <c>06.Persistence.Abstractions.IUnitOfWork</c>.
/// </para>
/// <para>
/// <b>Multi-requirement evolution (WO-038, P-232):</b> <see cref="AllOf"/> and <see cref="AnyOf"/>
/// are the primary evaluation methods for multi-requirement composition. <see cref="IsAuthorizedAsync"/>
/// remains as the single-requirement convenience (equivalent to <c>AllOf([ requirement ])</c>).
/// Requirement strings are opaque (permission/policy names) whose meaning is entirely owned by the
/// consuming service's bridge implementation — this package never interprets them.
/// </para>
/// </remarks>
public interface IAuthorizationContext
{
    /// <summary>
    /// Determines whether the current caller satisfies <paramref name="requirement"/>.
    /// </summary>
    /// <param name="requirement">
    /// An opaque string (permission/policy name) whose meaning is entirely owned by the consuming
    /// service's bridge implementation — this package never interprets it.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <remarks>
    /// Convenience method equivalent to <c>AllOf([ requirement ], ct)</c>. Prefer
    /// <see cref="AllOf"/> and <see cref="AnyOf"/> for multi-requirement evaluation.
    /// </remarks>
    Task<bool> IsAuthorizedAsync(string requirement, CancellationToken cancellationToken);

    /// <summary>
    /// Evaluates whether the current caller satisfies <b>all</b> of the given requirements.
    /// </summary>
    /// <param name="requirements">
    /// A sequence of opaque requirement strings. All must pass for the method to return
    /// <see langword="true"/>. An empty sequence returns <see langword="true"/> immediately
    /// (vacuous truth — no requirements to fail).
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// <see langword="true"/> when every requirement passes; <see langword="false"/> on the first
    /// failure (short-circuit evaluation — remaining requirements are not evaluated).
    /// </returns>
    Task<bool> AllOf(IEnumerable<string> requirements, CancellationToken cancellationToken);

    /// <summary>
    /// Evaluates whether the current caller satisfies <b>at least one</b> of the given requirements.
    /// </summary>
    /// <param name="requirements">
    /// A sequence of opaque requirement strings. At least one must pass for the method to return
    /// <see langword="true"/>. An empty sequence returns <see langword="false"/> (nothing to satisfy).
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// <see langword="true"/> as soon as the first requirement passes (short-circuit evaluation —
    /// remaining requirements are not evaluated); <see langword="false"/> when none pass.
    /// </returns>
    Task<bool> AnyOf(IEnumerable<string> requirements, CancellationToken cancellationToken);
}
