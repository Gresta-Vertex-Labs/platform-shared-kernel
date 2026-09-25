using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Presentation.Grpc.Interceptors;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;

namespace SharedKernel.Testing.SelfTests.Grpc;

/// <summary>
/// Proves <see cref="SharedKernel.Testing.Grpc.TestServerCallContext"/> genuinely exercises all
/// four shipped <c>SharedKernel.Presentation.Grpc</c> interceptors end to end, entirely in-process
/// — no consuming domain has adopted this harness yet, so this self-test is the only behavioral
/// proof today, per the SelfTests routing rule.
/// </summary>
public sealed class TestServerCallContextTests
{
    private static readonly Microsoft.Extensions.Logging.Abstractions.NullLogger<GrpcExceptionInterceptor> ExceptionLogger =
        Microsoft.Extensions.Logging.Abstractions.NullLogger<GrpcExceptionInterceptor>.Instance;

    [Fact]
    public async Task GrpcCorrelationInterceptor_ReadsCorrelationIdFromMetadata_StoresInUserState()
    {
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create(correlationId: "corr-123");
        var interceptor = new GrpcCorrelationInterceptor();

        await interceptor.UnaryServerHandler<string, string>("req", context, (_, c) => Task.FromResult("ok"));

        Assert.Equal("corr-123", context.UserState[GrpcCorrelationInterceptor.ItemsKey]);
    }

    [Fact]
    public async Task GrpcCorrelationInterceptor_NoCorrelationId_GeneratesOne()
    {
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create();
        var interceptor = new GrpcCorrelationInterceptor();

        await interceptor.UnaryServerHandler<string, string>("req", context, (_, c) => Task.FromResult("ok"));

        var generated = Assert.IsType<string>(context.UserState[GrpcCorrelationInterceptor.ItemsKey]);
        Assert.False(string.IsNullOrWhiteSpace(generated));
    }

    [Fact]
    public async Task GrpcTenantContextInterceptor_ResolvesTenantIdFromRequestServices()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create(
            configureServices: services => services.AddSingleton<IRequestContext>(new SystemRequestContext([], "caller", tenantId)));
        var interceptor = new GrpcTenantContextInterceptor(new RequestContextAccessor());

        TenantId? observed = null;
        await interceptor.UnaryServerHandler<string, string>("req", context, (_, c) =>
        {
            observed = RequestContextScope.Current?.TenantId;
            return Task.FromResult("ok");
        });

        Assert.Equal(tenantId, observed);
    }

    [Fact]
    public async Task GrpcTenantContextInterceptor_NoRequestContextRegistered_RunsAnonymousWithACorrelationId_NeverRejects()
    {
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create();
        var interceptor = new GrpcTenantContextInterceptor(new RequestContextAccessor());

        IRequestContext? observed = null;
        var result = await interceptor.UnaryServerHandler<string, string>("req", context, (_, c) =>
        {
            observed = RequestContextScope.Current;
            return Task.FromResult("ok");
        });

        // P-566: the call always runs inside a scope, anonymous here, so it still carries a correlation id.
        Assert.NotNull(observed);
        Assert.False(observed!.IsAuthenticated);
        Assert.Null(observed.TenantId);
        Assert.True(CorrelationIds.IsValid(observed.CorrelationId));
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task GrpcAuthorizationInterceptor_NoEndpointMetadata_AllowsUnconditionally()
    {
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create();
        var interceptor = new GrpcAuthorizationInterceptor();

        var result = await interceptor.UnaryServerHandler<string, string>("req", context, (_, c) => Task.FromResult("ok"));

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task GrpcAuthorizationInterceptor_RequireRole_MissingRole_ThrowsPermissionDenied()
    {
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create(
            configureServices: services => services.AddSingleton<IUserContext>(new FakeUserContext { Roles = [] }),
            endpointMetadata: [new RequireRoleAttribute("Admin")]);
        var interceptor = new GrpcAuthorizationInterceptor();

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await interceptor.UnaryServerHandler<string, string>("req", context, (_, c) => Task.FromResult("ok")));

        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
    }

    [Fact]
    public async Task GrpcAuthorizationInterceptor_RequireRole_HasRole_Succeeds()
    {
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create(
            configureServices: services => services.AddSingleton<IUserContext>(new FakeUserContext { Roles = ["Admin"] }),
            endpointMetadata: [new RequireRoleAttribute("Admin")]);
        var interceptor = new GrpcAuthorizationInterceptor();

        var result = await interceptor.UnaryServerHandler<string, string>("req", context, (_, c) => Task.FromResult("ok"));

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task GrpcAuthorizationInterceptor_RequireFreshAuthentication_StaleAuth_Rejected()
    {
        var clock = new FakeClock();
        var userContext = new FakeUserContext();
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create(
            configureServices: services =>
            {
                services.AddSingleton<IUserContext>(userContext);
                services.AddSingleton<IClock>(clock);
            },
            endpointMetadata: [new RequireFreshAuthenticationAttribute(30)]);
        var interceptor = new GrpcAuthorizationInterceptor();

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await interceptor.UnaryServerHandler<string, string>("req", context, (_, c) => Task.FromResult("ok")));

        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
    }

    [Fact]
    public async Task GrpcExceptionInterceptor_SharedKernelException_MapsToRpcException()
    {
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create();
        var interceptor = new GrpcExceptionInterceptor(ExceptionLogger, new FakeHostEnvironment());

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await interceptor.UnaryServerHandler<string, string>(
                "req",
                context,
                (_, c) => throw new ValidationException([Error.Validation("field.invalid", "Field is invalid.")])));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task GrpcExceptionInterceptor_UnknownException_MapsToInternal()
    {
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create();
        var interceptor = new GrpcExceptionInterceptor(ExceptionLogger, new FakeHostEnvironment());

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await interceptor.UnaryServerHandler<string, string>(
                "req",
                context,
                (_, c) => throw new InvalidOperationException("boom")));

        Assert.Equal(StatusCode.Internal, exception.StatusCode);
    }

    [Fact]
    public async Task GrpcExceptionInterceptor_ServerStreamingShape_AlsoMapsExceptions()
    {
        // Proves the harness supports non-unary call shapes too (D-228) — the constructed
        // ServerCallContext is generic-shape-agnostic.
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create();
        var interceptor = new GrpcExceptionInterceptor(ExceptionLogger, new FakeHostEnvironment());

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await interceptor.ServerStreamingServerHandler<string, string>(
                "req",
                NullServerStreamWriter<string>.Instance,
                context,
                (_, _, c) => throw new InvalidOperationException("boom")));

        Assert.Equal(StatusCode.Internal, exception.StatusCode);
    }

    [Fact]
    public async Task Create_CorrelationIdAndTenantAndAuthorization_AllComposeTogether()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var context = SharedKernel.Testing.Grpc.TestServerCallContext.Create(
            correlationId: "corr-xyz",
            configureServices: services =>
            {
                services.AddSingleton<IRequestContext>(new SystemRequestContext([], "caller", tenantId));
                services.AddSingleton<IUserContext>(new FakeUserContext { Roles = ["Admin"] });
            },
            endpointMetadata: [new RequireRoleAttribute("Admin")]);

        await new GrpcCorrelationInterceptor().UnaryServerHandler<string, string>("req", context, (_, c) => Task.FromResult("ok"));
        TenantId? observedTenant = null;
        await new GrpcTenantContextInterceptor(new RequestContextAccessor()).UnaryServerHandler<string, string>("req", context, (_, c) =>
        {
            observedTenant = RequestContextScope.Current?.TenantId;
            return Task.FromResult("ok");
        });
        var result = await new GrpcAuthorizationInterceptor().UnaryServerHandler<string, string>("req", context, (_, c) => Task.FromResult("ok"));

        Assert.Equal("corr-xyz", context.UserState[GrpcCorrelationInterceptor.ItemsKey]);
        Assert.Equal(tenantId, observedTenant);
        Assert.Equal("ok", result);
    }

    private sealed class FakeHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Microsoft.Extensions.Hosting.Environments.Production;
        public string ApplicationName { get; set; } = "SelfTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class NullServerStreamWriter<T> : IServerStreamWriter<T>
    {
        public static readonly NullServerStreamWriter<T> Instance = new();

        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message) => Task.CompletedTask;
    }
}
