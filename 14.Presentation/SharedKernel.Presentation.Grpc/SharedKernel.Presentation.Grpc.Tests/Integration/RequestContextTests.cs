using FluentAssertions;
using Grpc.Core;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// P-579 (the intent of WO-086's deleted <c>GrpcCorrelationTenantRoundTripTests</c>): a gRPC service method sees the
/// call's <see cref="RequestContextScope"/> — correlation id, caller and tenant — with no gRPC interceptor, because
/// gRPC calls run through the HTTP pipeline and <c>UseSharedKernelRequestContext()</c> opens the scope for them.
/// </summary>
public sealed class RequestContextTests
{
    [Fact]
    public async Task SignedInCall_TheMethodSeesTheScope_WithCorrelationIdCallerAndTenant()
    {
        await using var app = await GrpcTestHost.StartAsync();
        var tenant = new TenantId(Guid.NewGuid());
        var headers = TestAuthentication.SignedIn(user: "user-42", tenant: tenant);
        headers.Add(WellKnownHeaders.CorrelationId, "flow-scope-1");

        var reply = await app.CreateClient().GetContextAsync(new EchoRequest(), headers);

        reply.ScopeCorrelationId.Should().Be("flow-scope-1");
        reply.CorrelationId.Should().Be("flow-scope-1", "the injected IRequestContext is the ambient scope");
        reply.UserId.Should().Be("user-42");
        reply.ActorKind.Should().Be(nameof(ActorKind.User));
        reply.TenantId.Should().Be(tenant.ToString());
    }

    [Fact]
    public async Task AnonymousCall_StillRunsInAScope_WithANewCorrelationId()
    {
        await using var app = await GrpcTestHost.StartAsync();

        var reply = await app.CreateClient().GetContextAsync(new EchoRequest());

        reply.ScopeCorrelationId.Should().MatchRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
        reply.ActorKind.Should().Be(nameof(ActorKind.Anonymous));
        reply.UserId.Should().BeEmpty();
        reply.TenantId.Should().BeEmpty();
    }

    [Fact]
    public async Task GrpcOnlyHost_WithoutTheWebApiPipeline_TheMethodStillSeesTheScope()
    {
        await using var app = await GrpcTestHost.StartAsync(useWebApi: false);
        var headers = TestAuthentication.SignedIn(user: "user-7");
        headers.Add(WellKnownHeaders.CorrelationId, "flow-scope-2");

        var reply = await app.CreateClient().GetContextAsync(new EchoRequest(), headers);

        reply.ScopeCorrelationId.Should().Be("flow-scope-2");
        reply.UserId.Should().Be("user-7");
    }

    [Fact]
    public async Task EveryCall_HasItsOwnScope()
    {
        await using var app = await GrpcTestHost.StartAsync();
        var client = app.CreateClient();

        var first = await client.GetContextAsync(new EchoRequest(), new Metadata { { WellKnownHeaders.CorrelationId, "flow-a" } });
        var second = await client.GetContextAsync(new EchoRequest(), new Metadata { { WellKnownHeaders.CorrelationId, "flow-b" } });

        first.ScopeCorrelationId.Should().Be("flow-a");
        second.ScopeCorrelationId.Should().Be("flow-b");
    }
}
