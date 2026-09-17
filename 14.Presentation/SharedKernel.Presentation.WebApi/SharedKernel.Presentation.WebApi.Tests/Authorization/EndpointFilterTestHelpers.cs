using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.WebApi.Tests.Authorization;

/// <summary>
/// Shared helpers for building an <see cref="EndpointFilterInvocationContext"/> against a
/// synthetic endpoint, for testing <see cref="Authorization.AuthorizationRequirementEndpointFilter"/>
/// without a full <c>WebApplicationFactory</c> host.
/// </summary>
internal static class EndpointFilterTestHelpers
{
    private static readonly RequestDelegate NoOpRequestDelegate = _ => Task.CompletedTask;

    /// <summary>
    /// Builds an <see cref="EndpointFilterInvocationContext"/> whose <see cref="HttpContext"/>
    /// carries the given endpoint <paramref name="metadata"/> and, when <paramref name="userContext"/>
    /// is non-null, a <see cref="ServiceCollection"/> registering it as <see cref="IUserContext"/>.
    /// </summary>
    /// <param name="userContext">
    /// The <see cref="IUserContext"/> to register in <c>HttpContext.RequestServices</c>, or
    /// <see langword="null"/> to leave it unregistered entirely — used to prove a no-op code path
    /// never attempts resolution (a resolution attempt against an empty container throws).
    /// </param>
    /// <param name="metadata">Endpoint metadata objects (e.g. attribute instances) to attach.</param>
    public static EndpointFilterInvocationContext CreateContext(IUserContext? userContext, params object[] metadata)
    {
        var services = new ServiceCollection();
        if (userContext is not null)
        {
            services.AddSingleton(userContext);
        }

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.SetEndpoint(new Endpoint(NoOpRequestDelegate, new EndpointMetadataCollection(metadata), displayName: "test"));

        return EndpointFilterInvocationContext.Create(httpContext);
    }

    /// <summary>
    /// A trivial <see cref="EndpointFilterDelegate"/> that records every invocation and returns a
    /// fixed sentinel result.
    /// </summary>
    public sealed class RecordingNext
    {
        public int CallCount { get; private set; }

        public static readonly object SentinelResult = new();

        public ValueTask<object?> Invoke(EndpointFilterInvocationContext context)
        {
            CallCount++;
            return ValueTask.FromResult<object?>(SentinelResult);
        }
    }
}
