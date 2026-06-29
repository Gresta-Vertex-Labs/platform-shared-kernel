namespace SharedKernel.Application.Behaviors.Authorization;

/// <summary>
/// A minimal seam for evaluating whether the current caller satisfies an authorization requirement.
/// </summary>
/// <remarks>
/// This is <b>not</b> <c>SharedKernel.Security.Abstractions.IUserContext</c>, and this package
/// carries no project reference to <c>12.Security</c> — the layering ceiling for
/// <c>05.Application</c> is <c>01–04</c>. Exposes only the minimal shape
/// <see cref="Authorization.AuthorizationBehavior{TRequest,TResponse}"/> needs ("does the current
/// caller satisfy this requirement"). The consuming service bridges this seam to its real
/// <c>IUserContext</c>/<c>ITenantProvider</c> at the composition root — the exact same bridging
/// pattern <c>TransactionBehavior</c>'s local <c>IUnitOfWork</c> already established for
/// <c>06.Persistence.Abstractions.IUnitOfWork</c>.
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
    Task<bool> IsAuthorizedAsync(string requirement, CancellationToken cancellationToken);
}
