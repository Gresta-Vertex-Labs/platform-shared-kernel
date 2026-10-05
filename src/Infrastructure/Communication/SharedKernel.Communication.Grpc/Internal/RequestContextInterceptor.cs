using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;

namespace SharedKernel.Communication.Grpc.Internal;

/// <summary>
/// Writes the current caller onto every outgoing call as metadata: the correlation id, the tenant, the actor and the
/// client, under <c>01.Core</c>'s <c>WellKnownHeaders</c> names — the same mapping as REST, MassTransit and Temporal.
/// </summary>
/// <remarks>
/// <para>
/// The caller is <see cref="IRequestContextAccessor.Current"/>, read when the call starts. The correlation id is the
/// caller's, never <c>Activity.Id</c>; a call that starts a new operation gets a new one. Trace context
/// (<c>traceparent</c>) is not written here: the HTTP handler writes it from the gRPC client's own span.
/// </para>
/// <para>
/// Metadata the call already carries is kept, the caller's <see cref="Metadata"/> is never changed, and a failure is
/// logged (11100) without failing the call. It runs before the client's retries, so every attempt carries the same values.
/// </para>
/// </remarks>
internal sealed partial class RequestContextInterceptor(
    IRequestContextAccessor accessor,
    ILogger<RequestContextInterceptor> logger) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation) =>
        continuation(request, WithCaller(context));

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation) =>
        continuation(request, WithCaller(context));

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation) =>
        continuation(WithCaller(context));

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation) =>
        continuation(WithCaller(context));

    private ClientInterceptorContext<TRequest, TResponse> WithCaller<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        try
        {
            var headers = new Metadata();
            if (context.Options.Headers is { } existing)
            {
                foreach (Metadata.Entry entry in existing)
                {
                    headers.Add(entry);
                }
            }

            var added = false;
            IRequestContext? caller = accessor.Current;
            RequestContextPropagation.WriteHeaders(
                caller,
                headers,
                (carrier, name, value) =>
                {
                    if (carrier.Get(name) is null)
                    {
                        carrier.Add(name, value);
                        added = true;
                    }
                },
                CorrelationIds.Current(caller) ?? CorrelationIds.New());

            return added
                ? new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, context.Options.WithHeaders(headers))
                : context;
        }
        catch (Exception exception)
        {
            LogPropagationFailed(logger, context.Method.FullName, exception);
            return context;
        }
    }

    [LoggerMessage(
        EventId = 11100,
        Level = LogLevel.Error,
        Message = "The caller could not be written onto the gRPC call {GrpcMethod}; it goes out without the caller's metadata.")]
    private static partial void LogPropagationFailed(ILogger logger, string grpcMethod, Exception exception);
}
