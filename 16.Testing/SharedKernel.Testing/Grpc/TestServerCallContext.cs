using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.Grpc.Interceptors;

namespace SharedKernel.Testing.Grpc;

/// <summary>
/// Static factory producing a <see cref="ServerCallContext"/> pre-populated with controllable
/// inbound metadata/services, so a consuming service can unit-test its own
/// <c>SharedKernel.Presentation.Grpc</c> interceptor composition (or authorization attributes)
/// without standing up a real <c>Grpc.AspNetCore</c> host.
/// </summary>
/// <remarks>
/// <para>
/// Wraps <see cref="Grpc.Core.Testing.TestServerCallContext"/> (the official <c>Grpc.Core.Testing</c>
/// stub, already a transitive necessity for this project — see
/// <c>Communication/TestServerCallContext.cs</c>, which wraps the same low-level factory for gRPC
/// CLIENT interceptor testing). This is a DELIBERATE, ACCEPTED duplication of a small amount of
/// wrapping boilerplate rather than a cross-reference to that sibling type — the sibling-capability-
/// folder isolation hard rule forbids <c>Grpc/</c> from referencing <c>Communication/</c>, mirroring
/// the already-established <c>ApplicationPipelineTestHarness</c>/<c>Communication/ActivityRecorder</c>
/// precedent for this exact class of duplication.
/// </para>
/// <para>
/// <b>The <c>HttpContext</c> bridge, empirically verified, not merely assumed:</b>
/// <see cref="ServerCallContextExtensions.GetHttpContext"/> (the mechanism
/// <c>GrpcTenantContextInterceptor</c>/<c>GrpcAuthorizationInterceptor</c> both use to reach
/// <see cref="Microsoft.AspNetCore.Http.HttpContext.RequestServices"/>) reads
/// <c>ServerCallContext.UserState["__HttpContext"]</c> — an ASP.NET Core gRPC hosting internal
/// convention, confirmed by direct reflection/execution against the real
/// <c>Grpc.AspNetCore.Server</c>/<c>Grpc.Core.Testing</c> assemblies before writing this file, not
/// assumed from documentation. Every <see cref="Create"/> overload populates this key so both
/// HttpContext-dependent interceptors resolve correctly.
/// </para>
/// <para>
/// <b>DESIGN CORRECTION vs. the original design draft:</b> the draft's "authorization metadata
/// entries" phrasing was inaccurate — <c>GrpcAuthorizationInterceptor</c> does not read gRPC
/// metadata for role/permission/step-up requirements at all; it reads endpoint metadata
/// (<c>RequireRoleAttribute</c> etc., attached to <see cref="HttpContext.GetEndpoint"/>) plus
/// <c>IUserContext</c> resolved from <see cref="HttpContext.RequestServices"/>.
/// <c>GrpcTenantContextInterceptor</c> similarly resolves <c>ITenantProvider</c> from
/// <c>RequestServices</c>, never from metadata. Only the correlation-id is genuinely metadata-driven
/// (<c>GrpcCorrelationInterceptor</c> reads <see cref="ServerCallContext.RequestHeaders"/> directly).
/// <see cref="Create"/> is corrected to match: <paramref name="configureServices"/>/
/// <paramref name="endpointMetadata"/> are how a test injects tenant/authorization inputs, never a
/// metadata entry.
/// </para>
/// <para>
/// <b>SCOPE LOCK:</b> deliberately does NOT default <c>IUserContext</c>/<c>ITenantProvider</c> to
/// <c>Security/FakeUserContext</c>/<c>FakeTenantProvider</c> — unlike <c>Clocks/FakeClock</c> (an
/// established, repo-wide cross-folder exception since the original P-035/WO-008 phase),
/// <c>Security/</c> fakes have no such precedent anywhere else in this package. A consuming test
/// supplies its own <c>Security/</c> fake via <paramref name="configureServices"/> — the CONSUMING
/// TEST, not this type, composes the two independently, mirroring the D-211 audit-finding
/// precedent's exact rationale.
/// </para>
/// <para>
/// After running an interceptor's <c>UnaryServerHandler</c> (or any of the other three streaming
/// call shapes — the returned <see cref="ServerCallContext"/> is generic-shape-agnostic, so a test
/// drives whichever method it needs) against the constructed context, inspect
/// <see cref="ServerCallContext.ResponseTrailers"/>/<see cref="ServerCallContext.Status"/> for
/// post-execution metadata, or catch the thrown <see cref="RpcException"/> directly (every one of
/// the four shipped interceptors signals rejection via a thrown <see cref="RpcException"/>, never
/// by setting <see cref="ServerCallContext.Status"/> in place).
/// </para>
/// </remarks>
public static class TestServerCallContext
{
    /// <summary>
    /// Creates a <see cref="ServerCallContext"/> for use in gRPC server interceptor tests.
    /// </summary>
    /// <param name="correlationId">
    /// When supplied, added to <paramref name="requestHeaders"/> under
    /// <see cref="GrpcCorrelationInterceptor.MetadataKey"/> — the inbound header
    /// <c>GrpcCorrelationInterceptor</c> reads.
    /// </param>
    /// <param name="requestHeaders">Additional inbound request metadata. Defaults to empty metadata.</param>
    /// <param name="configureServices">
    /// Populates the <see cref="IServiceCollection"/> backing the constructed
    /// <see cref="HttpContext.RequestServices"/> — e.g.
    /// <c>services =&gt; services.AddSingleton&lt;IUserContext&gt;(new FakeUserContext())</c>. Ignored
    /// when <paramref name="httpContext"/> is supplied directly. Omit entirely for interceptors that
    /// never call <see cref="ServerCallContextExtensions.GetHttpContext"/>
    /// (<c>GrpcExceptionInterceptor</c>/<c>GrpcCorrelationInterceptor</c>).
    /// </param>
    /// <param name="endpointMetadata">
    /// Attached to the constructed <see cref="HttpContext"/>'s <see cref="Endpoint"/> — e.g.
    /// <c>[new RequireRoleAttribute("Admin")]</c> — for <c>GrpcAuthorizationInterceptor</c> tests.
    /// Ignored when <paramref name="httpContext"/> is supplied directly.
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
    /// <returns>A new <see cref="ServerCallContext"/> ready to pass to an interceptor's server handler method.</returns>
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
            headers.Add(GrpcCorrelationInterceptor.MetadataKey, correlationId);
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
