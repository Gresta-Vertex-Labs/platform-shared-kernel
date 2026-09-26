using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Communication.Grpc.Interceptors;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Communication.Grpc.Tests.Interceptors;

public sealed class TenantIdInterceptorTests
{
    private static readonly Method<string, string> TestMethod = new(
        MethodType.Unary,
        "TestService",
        "TestMethod",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    private static ClientInterceptorContext<string, string> BuildContext(Metadata? headers)
    {
        var callOptions = headers is not null
            ? new CallOptions(headers)
            : new CallOptions();
        return new ClientInterceptorContext<string, string>(TestMethod, "localhost", callOptions);
    }

    private static Metadata.Entry? GetEntry(Metadata metadata, string key) =>
        metadata.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    private static AsyncUnaryCall<string> FakeUnaryCall() =>
        new(
            Task.FromResult("response"),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    [Fact]
    public void AsyncUnaryCall_WithTenantId_InjectsTenantIdMetadata()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var interceptor = CreateInterceptorWithTenant(tenantId);
        Metadata? capturedMetadata = null;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncUnaryCall("request", context,
            (_, ctx) =>
            {
                capturedMetadata = ctx.Options.Headers;
                return FakeUnaryCall();
            });

        // Assert
        capturedMetadata.Should().NotBeNull();
        GetEntry(capturedMetadata!, TenantIdInterceptor.TenantIdKey)!.Value
            .Should().Be(tenantId.ToString());
    }

    [Fact]
    public void AsyncUnaryCall_WithEmptyTenantId_NoOpSilently()
    {
        // Arrange — TenantId is Guid.Empty; interceptor passes context through unchanged
        var interceptor = CreateInterceptorWithTenant(Guid.Empty);
        var continuationCalled = false;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncUnaryCall("request", context,
            (_, ctx) =>
            {
                continuationCalled = true;
                // When interceptor is no-op, headers remain null (original context)
                var tenantEntry = ctx.Options.Headers is not null
                    ? GetEntry(ctx.Options.Headers, TenantIdInterceptor.TenantIdKey)
                    : null;
                tenantEntry.Should().BeNull("no x-tenant-id when TenantId is Guid.Empty");
                return FakeUnaryCall();
            });

        // Assert
        continuationCalled.Should().BeTrue("continuation must always be called");
    }

    [Fact]
    public void AsyncUnaryCall_WithNoAmbientCaller_NoOpSilently()
    {
        // Arrange — no request context is open (startup, or a caller outside any inbound adapter)
        var interceptor = new TenantIdInterceptor(
            new RequestContextAccessor(),
            NullLogger<TenantIdInterceptor>.Instance);

        var continuationCalled = false;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncUnaryCall("request", context,
            (_, ctx) =>
            {
                continuationCalled = true;
                var tenantEntry = ctx.Options.Headers is not null
                    ? GetEntry(ctx.Options.Headers, TenantIdInterceptor.TenantIdKey)
                    : null;
                tenantEntry.Should().BeNull("no x-tenant-id when no caller is ambient");
                return FakeUnaryCall();
            });

        // Assert
        continuationCalled.Should().BeTrue("continuation must always be called");
    }

    [Fact]
    public void AsyncUnaryCall_WhenCallerAlreadySetTenantId_DoesNotOverwrite()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var interceptor = CreateInterceptorWithTenant(tenantId);

        const string callerTenantId = "caller-tenant-id";
        var existingHeaders = new Metadata { { TenantIdInterceptor.TenantIdKey, callerTenantId } };
        Metadata? capturedMetadata = null;
        var context = BuildContext(existingHeaders);

        // Act
        interceptor.AsyncUnaryCall("request", context,
            (_, ctx) =>
            {
                capturedMetadata = ctx.Options.Headers;
                return FakeUnaryCall();
            });

        // Assert
        capturedMetadata.Should().NotBeNull();
        GetEntry(capturedMetadata!, TenantIdInterceptor.TenantIdKey)!.Value
            .Should().Be(callerTenantId, "interceptor must not overwrite caller-supplied x-tenant-id");
    }

    [Fact]
    public void AsyncUnaryCall_WhenExceptionThrown_DoesNotPropagate()
    {
        // Arrange — accessor throws when accessed
        var accessor = Substitute.For<IRequestContextAccessor>();
        accessor.Current.Returns(_ => throw new InvalidOperationException("simulated fault"));
        var interceptor = new TenantIdInterceptor(
            accessor,
            NullLogger<TenantIdInterceptor>.Instance);

        var context = BuildContext(null);
        var continuationCalled = false;

        // Act
        Action act = () => interceptor.AsyncUnaryCall("request", context,
            (_, _) =>
            {
                continuationCalled = true;
                return FakeUnaryCall();
            });

        // Assert
        act.Should().NotThrow("interceptor exceptions must be swallowed, not propagated");
        continuationCalled.Should().BeTrue();
    }

    [Fact]
    public void AsyncServerStreamingCall_WithTenantId_InjectsTenantIdMetadata()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var interceptor = CreateInterceptorWithTenant(tenantId);
        Metadata? capturedMetadata = null;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncServerStreamingCall("request", context,
            (_, ctx) =>
            {
                capturedMetadata = ctx.Options.Headers;
                return new AsyncServerStreamingCall<string>(
                    Substitute.For<IAsyncStreamReader<string>>(),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            });

        // Assert
        GetEntry(capturedMetadata!, TenantIdInterceptor.TenantIdKey)!.Value
            .Should().Be(tenantId.ToString());
    }

    [Fact]
    public void AsyncClientStreamingCall_WithTenantId_InjectsTenantIdMetadata()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var interceptor = CreateInterceptorWithTenant(tenantId);
        Metadata? capturedMetadata = null;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncClientStreamingCall(context,
            ctx =>
            {
                capturedMetadata = ctx.Options.Headers;
                return new AsyncClientStreamingCall<string, string>(
                    Substitute.For<IClientStreamWriter<string>>(),
                    Task.FromResult("response"),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            });

        // Assert
        GetEntry(capturedMetadata!, TenantIdInterceptor.TenantIdKey)!.Value
            .Should().Be(tenantId.ToString());
    }

    [Fact]
    public void AsyncDuplexStreamingCall_WithTenantId_InjectsTenantIdMetadata()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var interceptor = CreateInterceptorWithTenant(tenantId);
        Metadata? capturedMetadata = null;
        var context = BuildContext(null);

        // Act
        interceptor.AsyncDuplexStreamingCall(context,
            ctx =>
            {
                capturedMetadata = ctx.Options.Headers;
                return new AsyncDuplexStreamingCall<string, string>(
                    Substitute.For<IClientStreamWriter<string>>(),
                    Substitute.For<IAsyncStreamReader<string>>(),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            });

        // Assert
        GetEntry(capturedMetadata!, TenantIdInterceptor.TenantIdKey)!.Value
            .Should().Be(tenantId.ToString());
    }

    private static TenantIdInterceptor CreateInterceptorWithTenant(Guid tenantId) =>
        new(
            new FixedRequestContextAccessor(new SystemRequestContext([], "test", TenantId.FromNullable(tenantId))),
            NullLogger<TenantIdInterceptor>.Instance);

    [Fact]
    public void AsyncUnaryCall_WithAmbientCaller_InjectsActorAndClientMetadata()
    {
        var interceptor = new TenantIdInterceptor(
            new FixedRequestContextAccessor(new PropagatedRequestContext(null, "user-3", ActorKind.User, "cli")),
            NullLogger<TenantIdInterceptor>.Instance);
        Metadata? capturedMetadata = null;

        interceptor.AsyncUnaryCall("request", BuildContext(null),
            (_, ctx) =>
            {
                capturedMetadata = ctx.Options.Headers;
                return FakeUnaryCall();
            });

        GetEntry(capturedMetadata!, SharedKernel.Primitives.Propagation.WellKnownHeaders.ActorId)!.Value.Should().Be("user-3");
        GetEntry(capturedMetadata!, SharedKernel.Primitives.Propagation.WellKnownHeaders.ActorKind)!.Value.Should().Be("User");
        GetEntry(capturedMetadata!, SharedKernel.Primitives.Propagation.WellKnownHeaders.ClientId)!.Value.Should().Be("cli");
    }
}

internal sealed class FixedRequestContextAccessor(IRequestContext? current) : IRequestContextAccessor
{
    public IRequestContext? Current => current;
}
