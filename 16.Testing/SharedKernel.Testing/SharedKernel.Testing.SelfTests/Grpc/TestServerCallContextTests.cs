using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Security;
using CallContext = SharedKernel.Testing.Grpc.TestServerCallContext;

namespace SharedKernel.Testing.SelfTests.Grpc;

/// <summary>
/// Proves <see cref="CallContext"/> gives a service method or a service's own interceptor, run without a
/// <c>Grpc.AspNetCore</c> host, what ASP.NET Core hosting would: inbound metadata, the <see cref="HttpContext"/>
/// through <c>GetHttpContext()</c> with its services and endpoint metadata, and the call options — and that a service
/// method ending its results with <c>SharedKernel.Core</c>'s <c>GetValueOrThrow()</c> can be unit-tested with it. Since
/// P-562 <c>SharedKernel.Presentation.Grpc</c> has no public interceptors to drive (correlation, tenant and
/// authorization run in the HTTP pipeline) and no result extensions of its own (R32), so these self-tests are the
/// behavioral proof of the harness itself, per the SelfTests routing rule.
/// </summary>
public sealed class TestServerCallContextTests
{
    [Fact]
    public void CorrelationId_IsSentAsTheInboundCorrelationHeader()
    {
        var context = CallContext.Create(correlationId: "corr-123");

        Assert.Equal("corr-123", context.RequestHeaders.GetValue(WellKnownHeaders.CorrelationId));
    }

    [Fact]
    public void WithoutACorrelationId_NoCorrelationHeaderIsSent()
    {
        var context = CallContext.Create(requestHeaders: new Metadata { { "x-custom", "value" } });

        Assert.Null(context.RequestHeaders.Get(WellKnownHeaders.CorrelationId));
        Assert.Equal("value", context.RequestHeaders.GetValue("x-custom"));
    }

    [Fact]
    public void ConfiguredServices_AreTheRequestServicesOfTheHttpContext()
    {
        var tenantId = Guid.NewGuid();
        var context = CallContext.Create(
            configureServices: services => services.AddSingleton<ITenantProvider>(new FakeTenantProvider(tenantId)));

        var tenantProvider = context.GetHttpContext().RequestServices.GetRequiredService<ITenantProvider>();

        Assert.Equal(tenantId, tenantProvider.TenantId);
    }

    [Fact]
    public void EndpointMetadata_IsReadableFromTheHttpContext()
    {
        var marker = new EndpointMarker("orders");
        var context = CallContext.Create(endpointMetadata: [marker]);

        var endpoint = context.GetHttpContext().GetEndpoint();

        Assert.NotNull(endpoint);
        Assert.Same(marker, endpoint.Metadata.GetMetadata<EndpointMarker>());
    }

    [Fact]
    public void SuppliedHttpContext_IsUsedAsIs()
    {
        var httpContext = new DefaultHttpContext();

        var context = CallContext.Create(
            httpContext: httpContext,
            configureServices: services => services.AddSingleton<ITenantProvider>(new FakeTenantProvider(Guid.NewGuid())));

        Assert.Same(httpContext, context.GetHttpContext());
    }

    [Fact]
    public void CallOptions_AreApplied()
    {
        using var cancellation = new CancellationTokenSource();
        var deadline = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var context = CallContext.Create(
            method: "/orders.Orders/Get",
            host: "orders.internal",
            deadline: deadline,
            cancellationToken: cancellation.Token);

        Assert.Equal("/orders.Orders/Get", context.Method);
        Assert.Equal("orders.internal", context.Host);
        Assert.Equal(deadline, context.Deadline);
        Assert.Equal(cancellation.Token, context.CancellationToken);
    }

    [Fact]
    public async Task ServiceMethod_UsingGetValueOrThrow_ThrowsTheExceptionOfItsError()
    {
        // Unit-tested without a host, a failed result is the exception of its error (Error.ToException()); in a host,
        // the interceptor of SharedKernel.Presentation.Grpc turns that same exception into the rich google.rpc.Status.
        var context = CallContext.Create(correlationId: "corr-7");

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => OrdersService.GetAsync("42", context));

        Assert.Equal("order.not_found", exception.Error.Code);
        Assert.Equal(ErrorType.NotFound, exception.Error.Type);
    }

    [Fact]
    public async Task ServiceMethod_UsingGetValueOrThrow_ReturnsTheValue()
    {
        var context = CallContext.Create(
            configureServices: services => services.AddSingleton<ITenantProvider>(new FakeTenantProvider(Guid.NewGuid())));

        var order = await OrdersService.GetAsync("7", context);

        Assert.Equal("order-7", order);
    }

    [Fact]
    public async Task ServerStreamingServiceMethod_WritesTrailersThroughTheContext()
    {
        var context = CallContext.Create(correlationId: "corr-9");
        var writer = new RecordingStreamWriter<string>();

        await OrdersService.StreamAsync(writer, context);

        Assert.Equal(new[] { "a", "b" }, writer.Messages);
        Assert.Equal("corr-9", context.ResponseTrailers.GetValue("x-echo-correlation"));
    }

    private sealed record EndpointMarker(string Name);

    /// <summary>A consumer-style gRPC service written against the platform's public surface.</summary>
    private static class OrdersService
    {
        public static Task<string> GetAsync(string id, ServerCallContext context)
        {
            var result = id == "7"
                && context.GetHttpContext().RequestServices.GetService<ITenantProvider>() is not null
                    ? Result<string>.Success($"order-{id}")
                    : Result<string>.Failure(Error.NotFound("order.not_found", $"Order {id} was not found."));

            return Task.FromResult(result.GetValueOrThrow());
        }

        public static async Task StreamAsync(IServerStreamWriter<string> writer, ServerCallContext context)
        {
            await writer.WriteAsync("a");
            await writer.WriteAsync("b");
            context.ResponseTrailers.Add("x-echo-correlation", context.RequestHeaders.GetValue(WellKnownHeaders.CorrelationId) ?? string.Empty);
        }
    }

    private sealed class RecordingStreamWriter<T> : IServerStreamWriter<T>
    {
        public List<T> Messages { get; } = [];

        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
