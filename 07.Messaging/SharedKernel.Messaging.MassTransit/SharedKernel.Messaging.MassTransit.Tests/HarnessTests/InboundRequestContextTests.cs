#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using System.Collections.ObjectModel;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Messaging.Abstractions.Context;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.MassTransit.Context;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// P-561: the publisher's tenant and actor travel on the message and are rebuilt on the consumer,
/// so a consumer's <c>IRequestContext</c> answers for the caller that caused the work.
/// </summary>
/// <remarks>
/// Exercised end to end through the real builder API and the MassTransit in-memory harness: the
/// value of this feature is entirely in the round trip, and asserting the publish half and the
/// consume half separately would not prove they agree on the header names.
/// </remarks>
public sealed class InboundRequestContextTests
{
    private static ServiceProvider BuildHarness(IRequestContext? hostContext)
    {
        var services = new ServiceCollection();

        // Registered BEFORE the messaging builder, which is the documented order: the container
        // resolves the last IRequestContext registered, so the message-aware one must come second.
        if (hostContext is not null)
            services.AddScoped(_ => hostContext);

        services
            .AddSharedKernelMessaging(o => o.ServiceName = "inbound-ctx-service")
            .WithInboundRequestContext();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<IrcCapturingConsumer>();
            cfg.UsingInMemory((ctx, busCfg) =>
            {
                busCfg.UseConsumeFilter(typeof(InboundRequestContextFilter<>), ctx);
                busCfg.ConfigureEndpoints(ctx);
            });
        });

        // MassTransitMessageBus's own collaborators, which Build() would otherwise register —
        // the harness replaces the transport wiring, not the bus abstraction.
        services.AddSingleton<IReadOnlyDictionary<Type, string>>(
            new ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()));
        services.AddScoped<ConventionSendEndpointResolver>();
        services.AddScoped<IMessageBus, MassTransitMessageBus>();

        return services.BuildServiceProvider(true);
    }

    // -------------------------------------------------------------------------
    // The round trip
    // -------------------------------------------------------------------------

    /// <summary>
    /// The whole point: an authenticated, tenanted publisher's identity reaches the consumer.
    /// </summary>
    [Fact]
    public async Task PublishFromTenantedCaller_ConsumerResolvesSameTenantAndActor()
    {
        IrcCaptureStore.Reset();
        var tenantId = Guid.NewGuid();
        var host = new IrcFakeRequestContext(tenantId, "user-77", ActorKind.User, "checkout-spa");

        await using var provider = BuildHarness(host);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            (await bus.PublishAsync(new IrcTestMessage("payload"), CancellationToken.None))
                .IsSuccess.Should().BeTrue();
        }

        (await harness.Consumed.Any<IrcTestMessage>()).Should().BeTrue();

        IrcCaptureStore.TenantId.Should().Be(tenantId,
            "the tenant is what lets a consumer write tenant-scoped rows at all");
        IrcCaptureStore.UserId.Should().Be("user-77");
        IrcCaptureStore.ActorKind.Should().Be(ActorKind.User);
        IrcCaptureStore.ClientId.Should().Be("checkout-spa");
        IrcCaptureStore.IsAuthenticated.Should().BeTrue();

        await harness.Stop();
    }

    /// <summary>
    /// An unauthenticated publisher produces a consumer context with nothing in it, rather than a
    /// context that inherits the consuming service's own host identity.
    /// </summary>
    [Fact]
    public async Task PublishFromAnonymousCaller_ConsumerSeesNoTenantAndNoActor()
    {
        IrcCaptureStore.Reset();

        await using var provider = BuildHarness(AnonymousRequestContext.Instance);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new IrcTestMessage("payload"), CancellationToken.None);
        }

        (await harness.Consumed.Any<IrcTestMessage>()).Should().BeTrue();

        IrcCaptureStore.TenantId.Should().BeNull("persistence must keep failing closed");
        IrcCaptureStore.UserId.Should().BeNull();
        IrcCaptureStore.ActorKind.Should().Be(ActorKind.Anonymous);
        IrcCaptureStore.IsAuthenticated.Should().BeFalse();

        await harness.Stop();
    }

    /// <summary>
    /// A machine caller stays a machine caller across the bus. Actor kind is written as its name,
    /// so the consumer does not have to agree with the publisher on enum member ordering.
    /// </summary>
    [Fact]
    public async Task PublishFromServiceActor_ActorKindSurvivesAsName()
    {
        IrcCaptureStore.Reset();
        var host = new IrcFakeRequestContext(Guid.NewGuid(), "svc-billing", ActorKind.Service);

        await using var provider = BuildHarness(host);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IMessageBus>()
                .PublishAsync(new IrcTestMessage("payload"), CancellationToken.None);
        }

        (await harness.Consumed.Any<IrcTestMessage>()).Should().BeTrue();

        IrcCaptureStore.ActorKind.Should().Be(ActorKind.Service);
        IrcCaptureStore.RawActorKindHeader.Should().Be("Service",
            "the member name, not its numeric value, so a dead-lettered message stays readable");

        await harness.Stop();
    }

    /// <summary>
    /// An explicit per-publish tenant overrides the ambient one — how a background job publishes on
    /// behalf of a tenant it is not itself scoped to.
    /// </summary>
    [Fact]
    public async Task ExplicitTenantOnPublish_WinsOverAmbientCallerTenant()
    {
        IrcCaptureStore.Reset();
        var ambientTenant = Guid.NewGuid();
        var explicitTenant = Guid.NewGuid();
        var host = new IrcFakeRequestContext(ambientTenant, "user-77", ActorKind.User);

        await using var provider = BuildHarness(host);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IMessageBus>()
                .PublishAsync(
                    new IrcTestMessage("payload"),
                    ctx => ctx.WithTenantId(explicitTenant),
                    CancellationToken.None);
        }

        (await harness.Consumed.Any<IrcTestMessage>()).Should().BeTrue();

        IrcCaptureStore.TenantId.Should().Be(explicitTenant,
            "propagators run first so the explicit callback wins on any key both set");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // Malformed input
    // -------------------------------------------------------------------------

    /// <summary>
    /// A tenant header that is not a GUID yields no tenant rather than a faulted message: the
    /// payload is fine, only the attribution is not, and retrying cannot fix it.
    /// </summary>
    [Fact]
    public async Task MalformedTenantHeader_ConsumesWithNoTenantRatherThanFaulting()
    {
        IrcCaptureStore.Reset();

        await using var provider = BuildHarness(hostContext: null);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new IrcTestMessage("payload"), p =>
        {
            p.Headers.Set(WellKnownHeaders.TenantId, "not-a-guid");
            p.Headers.Set(MessageContextHeaders.ActorKind, "Sovereign");
        });

        (await harness.Consumed.Any<IrcTestMessage>()).Should().BeTrue();
        IrcCaptureStore.ConsumeCount.Should().Be(1, "the message must not be retried");

        IrcCaptureStore.TenantId.Should().BeNull();
        IrcCaptureStore.ActorKind.Should().Be(ActorKind.Anonymous,
            "an unrecognised actor kind degrades to the least-privileged member");

        await harness.Stop();
    }

    /// <summary>
    /// A numeric actor-kind header is rejected rather than cast: <c>Enum.TryParse</c> would happily
    /// return an undefined value that nothing downstream can render.
    /// </summary>
    [Fact]
    public async Task NumericActorKindHeader_IsRejectedNotCast()
    {
        IrcCaptureStore.Reset();

        await using var provider = BuildHarness(hostContext: null);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new IrcTestMessage("payload"), p =>
            p.Headers.Set(MessageContextHeaders.ActorKind, "7"));

        (await harness.Consumed.Any<IrcTestMessage>()).Should().BeTrue();

        IrcCaptureStore.ActorKind.Should().Be(ActorKind.Anonymous);

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // The composite outside a consume
    // -------------------------------------------------------------------------

    /// <summary>
    /// Outside a consume, <c>IRequestContext</c> is still the service's own — one handler works on
    /// both paths without branching.
    /// </summary>
    [Fact]
    public async Task OutsideConsume_RequestContextFallsBackToTheServiceOwnContext()
    {
        var tenantId = Guid.NewGuid();
        var host = new IrcFakeRequestContext(tenantId, "user-77", ActorKind.User);

        await using var provider = BuildHarness(host);
        await using var scope = provider.CreateAsyncScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IRequestContext>();

        resolved.Should().BeOfType<MessageAwareRequestContext>();
        resolved.TenantId.Should().Be(tenantId);
        resolved.UserId.Should().Be("user-77");
        resolved.ActorKind.Should().Be(ActorKind.User);
    }

    /// <summary>
    /// With no host registration at all, the non-consume answer is anonymous — the same fail-closed
    /// default persistence already applies, never an invented identity.
    /// </summary>
    [Fact]
    public async Task OutsideConsume_WithNoHostRegistration_IsAnonymous()
    {
        await using var provider = BuildHarness(hostContext: null);
        await using var scope = provider.CreateAsyncScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IRequestContext>();

        resolved.TenantId.Should().BeNull();
        resolved.IsAuthenticated.Should().BeFalse();
        resolved.ActorKind.Should().Be(ActorKind.Anonymous);
    }

    /// <summary>
    /// The inbound accessor reports "not a consume" outside one, which is what lets code tell a
    /// message-driven path from an HTTP one.
    /// </summary>
    [Fact]
    public async Task OutsideConsume_InboundAccessorCurrentIsNull()
    {
        await using var provider = BuildHarness(hostContext: null);
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider
            .GetRequiredService<IInboundMessageContextAccessor>()
            .Current.Should().BeNull();
    }

    /// <summary>
    /// Calling the builder method twice registers one set of services, so a composition root that
    /// enables it defensively does not end up with two propagators writing the same headers.
    /// </summary>
    [Fact]
    public void WithInboundRequestContext_CalledTwice_RegistersOnce()
    {
        var services = new ServiceCollection();

        services
            .AddSharedKernelMessaging(o => o.ServiceName = "idempotent-registration")
            .WithInboundRequestContext()
            .WithInboundRequestContext();

        services.Count(d => d.ServiceType == typeof(IRequestContext))
            .Should().Be(1);
        services.Count(d => d.ImplementationType == typeof(
                HeaderPropagation.RequestContextHeaderPropagator))
            .Should().Be(1);
    }
}

// ---------------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------------

/// <summary>Message type for these tests.</summary>
public sealed record IrcTestMessage(string Text);

/// <summary>A stand-in for a service's own HTTP-backed request context.</summary>
internal sealed class IrcFakeRequestContext : IRequestContext
{
    public IrcFakeRequestContext(Guid? tenantId, string? userId, ActorKind actorKind, string? clientId = null)
    {
        TenantId = tenantId;
        UserId = userId;
        ActorKind = actorKind;
        ClientId = clientId;
    }

    public bool IsAuthenticated => UserId is not null;

    public string? UserId { get; }

    public Guid? TenantId { get; }

    public ActorKind ActorKind { get; }

    public string? ClientId { get; }

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(true);
}

/// <summary>Records what the consumer's request context reported, for the assertions above.</summary>
internal static class IrcCaptureStore
{
    public static Guid? TenantId { get; set; }

    public static string? UserId { get; set; }

    public static ActorKind ActorKind { get; set; }

    public static string? ClientId { get; set; }

    public static bool IsAuthenticated { get; set; }

    public static string? RawActorKindHeader { get; set; }

    public static int ConsumeCount { get; set; }

    public static void Reset()
    {
        TenantId = null;
        UserId = null;
        ActorKind = ActorKind.User;
        ClientId = null;
        IsAuthenticated = false;
        RawActorKindHeader = null;
        ConsumeCount = 0;
    }
}

/// <summary>
/// Consumer that reads the ambient <c>IRequestContext</c> exactly as production code would — it
/// never touches the messaging-specific accessor, which is the behaviour under test.
/// </summary>
internal sealed class IrcCapturingConsumer : IConsumer<IrcTestMessage>
{
    private readonly IRequestContext _requestContext;

    public IrcCapturingConsumer(IRequestContext requestContext)
    {
        _requestContext = requestContext;
    }

    public Task Consume(ConsumeContext<IrcTestMessage> context)
    {
        IrcCaptureStore.ConsumeCount++;
        IrcCaptureStore.TenantId = _requestContext.TenantId;
        IrcCaptureStore.UserId = _requestContext.UserId;
        IrcCaptureStore.ActorKind = _requestContext.ActorKind;
        IrcCaptureStore.ClientId = _requestContext.ClientId;
        IrcCaptureStore.IsAuthenticated = _requestContext.IsAuthenticated;
        IrcCaptureStore.RawActorKindHeader =
            context.Headers.Get<string>(MessageContextHeaders.ActorKind);

        return Task.CompletedTask;
    }
}
