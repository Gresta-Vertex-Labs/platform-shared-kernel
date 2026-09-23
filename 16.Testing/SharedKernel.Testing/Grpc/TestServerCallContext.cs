using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Testing.Grpc;

/// <summary>
/// Static factory producing a <see cref="ServerCallContext"/> backed by a controllable <see cref="HttpContext"/>, so a
/// consuming service can unit-test its own gRPC service methods and interceptors without standing up a real
/// <c>Grpc.AspNetCore</c> host.
/// </summary>
/// <remarks>
/// <para>
/// Wraps <see cref="global::Grpc.Core.Testing.TestServerCallContext"/> (the official <c>Grpc.Core.Testing</c> stub). This is a
/// DELIBERATE, ACCEPTED duplication of a small amount of wrapping boilerplate with <c>Communication/TestServerCallContext.cs</c>
/// (which wraps the same factory for gRPC CLIENT interceptor testing) rather than a cross-reference: the sibling-
/// capability-folder isolation rule forbids <c>Grpc/</c> from referencing <c>Communication/</c>.
/// </para>
/// <para>
/// <b>The <c>HttpContext</c> bridge.</b> Hosted by ASP.NET Core, a service reaches the request through
/// <c>context.GetHttpContext()</c> (<c>Grpc.AspNetCore.Server</c>): its <see cref="HttpContext.RequestServices"/>,
/// its <see cref="Endpoint"/> metadata and, via <c>SharedKernel.Presentation.WebApi</c>, <c>GetCorrelationId()</c>.
/// Outside a host, <c>GetHttpContext()</c> reads <c>ServerCallContext.UserState["__HttpContext"]</c> — the convention
/// <c>Grpc.AspNetCore.Server</c> reserves for exactly this kind of test context. Every <see cref="Create"/> call
/// populates that key, so code under test resolves services and endpoint metadata as it would in a host.
/// </para>
/// <para>
/// <b>What this context does not simulate.</b> Since P-562 <c>SharedKernel.Presentation.Grpc</c> has no correlation,
/// tenant or authorization interceptors: those concerns run in the HTTP pipeline (<c>UseSharedKernelWebApi()</c>),
/// which a hand-built context never passes through. In particular <c>GetCorrelationId()</c> returns
/// <see langword="null"/> here, because only the pipeline's correlation middleware stores the id — a test that needs
/// the inbound value reads it from <see cref="ServerCallContext.RequestHeaders"/>. <c>[RequirePermission]</c> and its
/// siblings are enforced by ASP.NET Core authorization before a gRPC method runs; test them against a real in-process
/// host, never through this context.
/// </para>
/// <para>
/// <b>SCOPE LOCK:</b> deliberately does NOT default <c>IUserContext</c>/<c>ITenantProvider</c> to
/// <c>Security/FakeUserContext</c>/<c>FakeTenantProvider</c> — unlike <c>Clocks/FakeClock</c> (an established,
/// repo-wide cross-folder exception since the original P-035/WO-008 phase), <c>Security/</c> fakes have no such
/// precedent anywhere else in this package. A consuming test supplies its own <c>Security/</c> fake via
/// <paramref name="configureServices"/> — the CONSUMING TEST, not this type, composes the two independently.
/// </para>
/// <para>
/// After running a service method or an interceptor handler (any of the four call shapes — the returned
/// <see cref="ServerCallContext"/> is shape-agnostic) against the constructed context, inspect
/// <see cref="ServerCallContext.ResponseTrailers"/>/<see cref="ServerCallContext.Status"/>, or catch the thrown
/// <see cref="RpcException"/> directly — <c>GetValueOrThrow()</c>/<c>ThrowIfFailure()</c> of
/// <c>SharedKernel.Presentation.Grpc</c> throw one carrying a <c>google.rpc.Status</c>, readable with
/// <c>GetRpcStatus()</c>.
/// </para>
/// </remarks>
public static class TestServerCallContext
{
    /// <summary>
    /// Creates a <see cref="ServerCallContext"/> for use in gRPC service and server interceptor tests.
    /// </summary>
    /// <param name="correlationId">
    /// When supplied, added to <paramref name="requestHeaders"/> under <see cref="WellKnownHeaders.CorrelationId"/>
    /// (<c>X-Correlation-Id</c>) — the inbound header the platform's correlation middleware reads in a real host.
    /// </param>
    /// <param name="requestHeaders">Additional inbound request metadata. Defaults to empty metadata.</param>
    /// <param name="configureServices">
    /// Populates the <see cref="IServiceCollection"/> backing the constructed
    /// <see cref="HttpContext.RequestServices"/> — e.g.
    /// <c>services =&gt; services.AddSingleton&lt;ITenantProvider&gt;(new FakeTenantProvider(tenantId))</c>. Ignored
    /// when <paramref name="httpContext"/> is supplied directly.
    /// </param>
    /// <param name="endpointMetadata">
    /// Attached to the constructed <see cref="HttpContext"/>'s <see cref="Endpoint"/>, for code under test that reads
    /// endpoint metadata itself. Ignored when <paramref name="httpContext"/> is supplied directly. Authorization
    /// attributes placed here are not enforced: ASP.NET Core authorization enforces them in a real host.
    /// </param>
    /// <param name="httpContext">
    /// Supplies the <see cref="HttpContext"/> directly, overriding <paramref name="configureServices"/>/
    /// <paramref name="endpointMetadata"/>, for a test that needs full control. Defaults to a
    /// <see cref="DefaultHttpContext"/> built from <paramref name="configureServices"/> when omitted.
    /// </param>
    /// <param name="method">The gRPC method name. Defaults to <c>"test-method"</c>.</param>
    /// <param name="host">The host string. Defaults to <c>"localhost"</c>.</param>
    /// <param name="deadline">The call deadline. Defaults to <see cref="DateTime.MaxValue"/> (no deadline).</param>
    /// <param name="cancellationToken">The cancellation token observed by the call. Defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A new <see cref="ServerCallContext"/> ready to pass to a service method or an interceptor's server handler method.</returns>
    public static ServerCallContext Create(
        string? correlationId = null,
        Metadata? requestHeaders = null,
        Action<IServiceCollection>? configureServices = null,
        IEnumerable<object>? endpointMetadata = null,
        HttpContext? httpContext = null,
        string method = "test-method",
        string host = "localhost",
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var headers = requestHeaders ?? new Metadata();
        if (correlationId is not null)
        {
            headers.Add(WellKnownHeaders.CorrelationId, correlationId);
        }

        var resolvedHttpContext = httpContext ?? BuildHttpContext(configureServices, endpointMetadata);

        var context = global::Grpc.Core.Testing.TestServerCallContext.Create(
            method: method,
            host: host,
            deadline: deadline ?? DateTime.MaxValue,
            requestHeaders: headers,
            cancellationToken: cancellationToken,
            peer: "test-peer",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => null,
            writeOptionsSetter: _ => { });

        // The mechanism ServerCallContextExtensions.GetHttpContext() relies on — see class remarks.
        context.UserState["__HttpContext"] = resolvedHttpContext;

        return context;
    }

    private static HttpContext BuildHttpContext(Action<IServiceCollection>? configureServices, IEnumerable<object>? endpointMetadata)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        if (endpointMetadata is not null)
        {
            httpContext.SetEndpoint(new Endpoint(requestDelegate: null, new EndpointMetadataCollection(endpointMetadata), displayName: "test-endpoint"));
        }

        return httpContext;
    }
}
