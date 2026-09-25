using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.Grpc.Interceptors;

/// <summary>
/// Global server interceptor that restores (or creates) the inbound correlation id of every gRPC call — the
/// receiving half of what <c>SharedKernel.Communication.Grpc</c>'s client interceptor sends.
/// </summary>
/// <remarks>
/// <para>
/// The correlation id is, in order: the one already on the ambient request context (when
/// <c>SharedKernel.ServiceDefaults.Security</c>'s <c>UseSharedKernelRequestContext()</c> ran for the underlying
/// HTTP/2 request, which reads the same header), else the <see cref="WellKnownHeaders.CorrelationId"/> metadata
/// entry when <see cref="CorrelationIds.IsValid"/> accepts it, else a new id. A rejected value is never stored.
/// </para>
/// <para>
/// Stores the id in <see cref="ServerCallContext.UserState"/> under <see cref="ItemsKey"/> and in the current
/// <see cref="Activity"/>'s baggage under <see cref="WellKnownBaggageKeys.CorrelationId"/>, for log enrichment.
/// <see cref="GrpcTenantContextInterceptor"/>, registered after this one, puts it on the call's request context.
/// </para>
/// <para>
/// Overrides all four server interceptor methods, since gRPC has three streaming call shapes in
/// addition to unary that equally need correlation propagation.
/// </para>
/// </remarks>
public sealed class GrpcCorrelationInterceptor : Interceptor
{
    /// <summary>The gRPC metadata key carrying the correlation identifier (<c>"X-Correlation-Id"</c>).</summary>
    public const string MetadataKey = WellKnownHeaders.CorrelationId;

    /// <summary>The <see cref="ServerCallContext.UserState"/> key the resolved correlation identifier is stored under.</summary>
    public const string ItemsKey = "CorrelationId";

    /// <inheritdoc />
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(request, context);
    }

    /// <inheritdoc />
    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(requestStream, context);
    }

    /// <inheritdoc />
    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(request, responseStream, context);
    }

    /// <inheritdoc />
    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(requestStream, responseStream, context);
    }

    private static void Enrich(ServerCallContext context)
    {
        var correlationId = Resolve(context);

        Activity.Current?.SetBaggage(WellKnownBaggageKeys.CorrelationId, correlationId);
    }

    /// <summary>
    /// Returns the call's correlation id, resolving and storing it in <see cref="ServerCallContext.UserState"/> on
    /// first use.
    /// </summary>
    /// <param name="context">The call.</param>
    /// <returns>The call's correlation id.</returns>
    internal static string Resolve(ServerCallContext context)
    {
        if (context.UserState.TryGetValue(ItemsKey, out var stored) && stored is string existing)
            return existing;

        var correlationId = CorrelationIds.Current(RequestContextScope.Current) is { } ambient
            ? ambient
            : CorrelationIds.AcceptOrCreate(context.RequestHeaders.GetValue(MetadataKey));

        context.UserState[ItemsKey] = correlationId;
        return correlationId;
    }
}
