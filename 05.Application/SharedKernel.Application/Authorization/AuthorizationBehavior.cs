using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Context;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Short-circuits the pipeline with an unauthorized or forbidden failure when the current caller
/// does not hold the permissions a request declares with <see cref="RequirePermissionAttribute"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Always registered by <c>AddSharedKernelApplication</c>: there is no opt-in to forget, so a
/// request carrying the attribute is never handled unchecked.
/// </para>
/// <para>
/// Runs before <c>ValidationBehavior</c> in the canonical pipeline — an unauthorized caller must
/// never learn a request's validation rules, and a validator may itself hit the database, which an
/// unauthorized caller should never trigger.
/// </para>
/// <para>
/// A request without the attribute passes unchecked, and <see cref="IRequestContext"/> is not
/// resolved for it: a service with no caller identity works as long as it declares no permission.
/// For a request with the attribute the context is resolved when the request is sent; when none is
/// registered the behavior throws <see cref="InvalidOperationException"/> instead of running the
/// handler. That is a host misconfiguration, which the start-time seam check normally reports
/// first; the throw covers a request type from an assembly the registration did not scan.
/// </para>
/// <para>
/// Otherwise: not authenticated (<see cref="IRequestContext.IsAuthenticated"/> is
/// <see langword="false"/>) → <c>Error.Unauthorized(ErrorCodes.Unauthorized.Default, ...)</c>; one
/// attribute whose values the caller holds none of →
/// <c>Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission, ...)</c>, with a message that
/// never names the permission. The values of one attribute are alternatives, several attributes
/// all apply.
/// </para>
/// <para>
/// The attributes are read once per request type (<see cref="PermissionRequirements{TRequest}"/>),
/// never per call. Every caller-facing denial is returned as a failed <c>Result</c>/<c>Result&lt;T&gt;</c>
/// — never thrown.
/// </para>
/// </remarks>
internal sealed class AuthorizationBehavior<TRequest, TResponse>(IServiceProvider services)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>The error code for an unauthenticated caller of a protected request.</summary>
    internal const string UnauthenticatedCode = ErrorCodes.Unauthorized.Default;

    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requirements = PermissionRequirements<TRequest>.AllOf;
        if (requirements.Length == 0)
            return await next().ConfigureAwait(false);

        var requestContext = services.GetService<IRequestContext>()
            ?? throw new InvalidOperationException(MissingRequestContextMessage(typeof(TRequest)));

        if (!requestContext.IsAuthenticated)
        {
            return FailureResponse.Create<TResponse>(
                Error.Unauthorized(UnauthenticatedCode, "Authentication is required."));
        }

        foreach (var anyOf in requirements)
        {
            if (!await HoldsAnyAsync(requestContext, anyOf, cancellationToken).ConfigureAwait(false))
            {
                return FailureResponse.Create<TResponse>(
                    Error.Forbidden(
                        ErrorCodes.Forbidden.InsufficientPermission,
                        "The caller does not have the required permission."));
            }
        }

        return await next().ConfigureAwait(false);
    }

    /// <summary>
    /// The message thrown when a request declares a permission and no <see cref="IRequestContext"/>
    /// is registered.
    /// </summary>
    /// <param name="requestType">The request type that declares the permission.</param>
    /// <returns>The message, naming the request type and the fix.</returns>
    internal static string MissingRequestContextMessage(Type requestType)
        => $"'{requestType.FullName}' declares [RequirePermission], but no {nameof(IRequestContext)} is " +
           "registered, so the caller's permissions cannot be checked; the request was not handled. " +
           PermissionRequirementScan.Fix;

    private static async Task<bool> HoldsAnyAsync(
        IRequestContext requestContext,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        foreach (var permission in permissions)
        {
            if (await requestContext.HasPermissionAsync(permission, cancellationToken).ConfigureAwait(false))
                return true;
        }

        return false;
    }
}

/// <summary>
/// The <see cref="RequirePermissionAttribute"/> declarations of <typeparamref name="TRequest"/>, read
/// once per closed request type.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <remarks>
/// Each element is one attribute's values (any of them satisfies it); every element must be
/// satisfied. Empty when the request declares none. An invalid attribute (no value, or a blank one)
/// makes the type initializer throw, so every send of that request fails instead of running
/// unchecked.
/// </remarks>
internal static class PermissionRequirements<TRequest>
{
    /// <summary>Gets the requirements; every one must be met.</summary>
    internal static readonly IReadOnlyList<string>[] AllOf =
    [
        .. typeof(TRequest)
            .GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
            .Select(attribute => attribute.Permissions),
    ];
}

/// <summary>
/// Finds the request types of the registered assemblies that declare
/// <see cref="RequirePermissionAttribute"/>, so the host start can demand an
/// <see cref="IRequestContext"/> when any exists.
/// </summary>
internal static class PermissionRequirementScan
{
    /// <summary>How many request types the start-time message names before summarizing the rest.</summary>
    internal const int NamedTypeLimit = 3;

    /// <summary>The fix, as the start-time check and the dispatch-time throw both state it.</summary>
    internal const string Fix =
        "Register the caller identity: AddSharedKernelRequestContext() (SharedKernel.ServiceDefaults.Security) " +
        "in a service with authenticated callers, or an IRequestContext of your own (a SystemRequestContext " +
        "for a worker).";

    /// <summary>
    /// Returns the concrete request types (<see cref="IBaseRequest"/>) of <paramref name="assemblies"/>
    /// that carry the attribute, on the type or a base type — as the behavior reads it — ordered by
    /// name.
    /// </summary>
    /// <param name="assemblies">The assemblies passed to the registration call.</param>
    /// <returns>The marked request types; empty when there are none.</returns>
    /// <remarks>
    /// Checks <see cref="MemberInfo.IsDefined(Type, bool)"/> only, so no attribute is constructed and
    /// an invalid one cannot throw here. A type that cannot be loaded is skipped.
    /// </remarks>
    internal static IReadOnlyList<Type> FindMarkedRequestTypes(IEnumerable<Assembly> assemblies)
    {
        var marked = new List<Type>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in LoadableTypes(assembly))
            {
                try
                {
                    if (type is { IsClass: false, IsValueType: false } || type.IsAbstract)
                        continue;

                    if (!typeof(IBaseRequest).IsAssignableFrom(type))
                        continue;

                    if (type.IsDefined(typeof(RequirePermissionAttribute), inherit: true))
                        marked.Add(type);
                }
                catch (Exception exception) when (exception is TypeLoadException or FileNotFoundException or FileLoadException)
                {
                    // A type whose base or attributes cannot be resolved cannot be sent either.
                }
            }
        }

        return [.. marked.OrderBy(type => type.FullName, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The feature text of the start-time requirement: up to <see cref="NamedTypeLimit"/> marked
    /// request types, how many more there are, and the fix.
    /// </summary>
    /// <param name="markedTypes">The marked request types; at least one.</param>
    /// <returns>The text the seam check puts after "needed by".</returns>
    internal static string Describe(IReadOnlyList<Type> markedTypes)
    {
        var named = string.Join(", ", markedTypes.Take(NamedTypeLimit).Select(type => type.FullName));
        var more = markedTypes.Count > NamedTypeLimit
            ? $" and {markedTypes.Count - NamedTypeLimit} more"
            : string.Empty;

        return $"[RequirePermission] on {named}{more}, checked against the caller on every send. {Fix}";
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
