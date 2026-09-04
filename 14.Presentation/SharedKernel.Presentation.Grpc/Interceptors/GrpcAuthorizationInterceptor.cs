using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Presentation.Grpc.Interceptors;

/// <summary>
/// Global server interceptor that enforces
/// <see cref="RequireRoleAttribute"/>/<see cref="RequirePermissionAttribute"/>/
/// <see cref="RequireFreshAuthenticationAttribute"/>/<see cref="RequireAuthenticationMethodAttribute"/>
/// metadata attached to a gRPC service method — reused VERBATIM from
/// <c>SharedKernel.Presentation.WebApi.Authorization</c>, the platform's one declarative
/// authorization dialect across both HTTP and gRPC (D-73/D-74).
/// </summary>
/// <remarks>
/// <para>
/// Safe to register on every call — reads endpoint metadata via
/// <see cref="EndpointHttpContextExtensions.GetEndpoint"/> off the call's
/// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> (<see cref="ServerCallContextExtensions.GetHttpContext"/>)
/// and no-ops when none of the four attributes are present — ASP.NET Core's gRPC hosting attaches
/// a service method's declared attributes to the mapped endpoint's
/// <see cref="Microsoft.AspNetCore.Http.Endpoint.Metadata"/> the same way it does for MVC
/// controller actions, so the four attribute types can be applied directly to a gRPC service
/// implementation class or method with no gRPC-specific wiring.
/// </para>
/// <para>
/// When at least one attribute is present, resolves <see cref="IUserContext"/> from the call's
/// <see cref="Microsoft.AspNetCore.Http.HttpContext.RequestServices"/> and evaluates each attached
/// attribute in order — identical AND-across/OR-within composition and
/// <see cref="IUserContext.HasRole"/>/<see cref="IUserContext.HasPermission"/>/
/// <see cref="IUserContext.IsAuthenticationFresherThan"/>/<see cref="IUserContext.WasAuthenticatedWith"/>
/// evaluation as
/// <c>SharedKernel.Presentation.WebApi.Authorization.AuthorizationRequirementEndpointFilter</c>.
/// On the first failing attribute, throws an <see cref="RpcException"/> built from
/// <see cref="Error.Forbidden(string, string)"/> mapped through <see cref="GrpcStatusCodeMap"/>
/// (→ <see cref="StatusCode.PermissionDenied"/>) — the target service method is never invoked.
/// </para>
/// <para>
/// An anonymous/unauthenticated caller is correctly rejected through the ordinary
/// <see cref="IUserContext.HasRole"/>/<see cref="IUserContext.HasPermission"/>/absent-<c>AuthTime</c>/
/// empty-<c>AuthenticationMethods</c> false paths — mirrors
/// <c>AuthorizationRequirementEndpointFilter</c>'s documented rationale exactly.
/// </para>
/// <para>
/// Overrides all four server interceptor methods, since gRPC has three streaming call shapes in
/// addition to unary that equally need authorization enforcement — a streaming call is rejected
/// before either stream is touched.
/// </para>
/// </remarks>
public sealed partial class GrpcAuthorizationInterceptor : Interceptor
{
    private const string ForbiddenErrorCode = "Authorization.Forbidden";
    private const string AuthenticationNotFreshErrorCode = "Authorization.AuthenticationNotFresh";
    private const string AuthenticationMethodNotSatisfiedErrorCode = "Authorization.AuthenticationMethodNotSatisfied";

    private readonly ILogger<GrpcAuthorizationInterceptor> _logger;

    /// <summary>
    /// Initialises a new <see cref="GrpcAuthorizationInterceptor"/>.
    /// </summary>
    /// <param name="logger">
    /// The logger used to record rejection-path security audit events. When no
    /// <see cref="ILogger{TCategoryName}"/> is registered in the container, a no-op
    /// <see cref="NullLogger{T}"/> is used instead.
    /// </param>
    public GrpcAuthorizationInterceptor(ILogger<GrpcAuthorizationInterceptor>? logger = null)
    {
        _logger = logger ?? NullLogger<GrpcAuthorizationInterceptor>.Instance;
    }

    /// <inheritdoc />
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return continuation(request, context);
    }

    /// <inheritdoc />
    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return continuation(requestStream, context);
    }

    /// <inheritdoc />
    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return continuation(request, responseStream, context);
    }

    /// <inheritdoc />
    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return continuation(requestStream, responseStream, context);
    }

    private void EnsureAuthorized(ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        var metadata = httpContext?.GetEndpoint()?.Metadata;

        var roleRequirements = metadata?.GetOrderedMetadata<RequireRoleAttribute>() ?? [];
        var permissionRequirements = metadata?.GetOrderedMetadata<RequirePermissionAttribute>() ?? [];
        var freshAuthenticationRequirement = metadata?.GetMetadata<RequireFreshAuthenticationAttribute>();
        var authenticationMethodRequirement = metadata?.GetMetadata<RequireAuthenticationMethodAttribute>();

        if (roleRequirements.Count == 0
            && permissionRequirements.Count == 0
            && freshAuthenticationRequirement is null
            && authenticationMethodRequirement is null)
        {
            return;
        }

        // Attributes were declared, but there is no HttpContext to resolve IUserContext from —
        // this should not happen under ASP.NET Core gRPC hosting, but fail closed rather than
        // silently skipping enforcement.
        if (httpContext is null)
        {
            throw BuildForbiddenException(context, ForbiddenErrorCode, "No request context is available to evaluate authorization requirements.");
        }

        var userContext = httpContext.RequestServices.GetRequiredService<IUserContext>();

        foreach (var requirement in roleRequirements)
        {
            if (!requirement.Roles.Any(userContext.HasRole))
            {
                throw BuildForbiddenException(
                    context,
                    ForbiddenErrorCode,
                    $"The caller does not hold any of the required role(s): {string.Join(", ", requirement.Roles)}.");
            }
        }

        foreach (var requirement in permissionRequirements)
        {
            if (!requirement.Permissions.Any(userContext.HasPermission))
            {
                throw BuildForbiddenException(
                    context,
                    ForbiddenErrorCode,
                    $"The caller does not hold any of the required permission(s): {string.Join(", ", requirement.Permissions)}.");
            }
        }

        if (freshAuthenticationRequirement is not null)
        {
            var clock = httpContext.RequestServices.GetRequiredService<IClock>();

            if (!userContext.IsAuthenticationFresherThan(freshAuthenticationRequirement.MaxAge, clock.UtcNow))
            {
                throw BuildForbiddenException(
                    context,
                    AuthenticationNotFreshErrorCode,
                    $"The caller's authentication must be no older than {freshAuthenticationRequirement.MaxAge.TotalSeconds} seconds.");
            }
        }

        if (authenticationMethodRequirement is not null
            && !authenticationMethodRequirement.Methods.Any(userContext.WasAuthenticatedWith))
        {
            throw BuildForbiddenException(
                context,
                AuthenticationMethodNotSatisfiedErrorCode,
                $"The caller's authentication must have used one of the required method(s): {string.Join(", ", authenticationMethodRequirement.Methods)}.");
        }
    }

    private RpcException BuildForbiddenException(ServerCallContext context, string code, string message)
    {
        var error = Error.Forbidden(code, message);
        var statusCode = GrpcStatusCodeMap.Resolve(error.Type);

        Log.AuthorizationRequirementRejected(_logger, context.Method, code);

        return new RpcException(new Status(statusCode, error.Message));
    }

    /// <summary>
    /// Source-generated log messages for <see cref="GrpcAuthorizationInterceptor"/>.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 201,
            Level = LogLevel.Warning,
            Message = "Authorization requirement {RequirementCode} rejected the gRPC call to {GrpcMethod}.")]
        public static partial void AuthorizationRequirementRejected(ILogger logger, string grpcMethod, string requirementCode);
    }
}
