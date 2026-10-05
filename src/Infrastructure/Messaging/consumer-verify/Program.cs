// consumer-verify — composes 07.Messaging the way a downstream service does: a real generic host,
// a configuration section, IHost.StartAsync() (which runs every ValidateOnStart check), and
// resolution through DI. No broker I/O: transport behaviour is covered by the RabbitMQ suites and
// by samples/ShippingApi. What is proved here is that the packages compose, that the contracts a
// consuming service depends on resolve, and that a misconfigured host fails at startup.
//   1. The whole surface a service injects resolves from one AddSharedKernelMessaging chain.
//   2. Configuration binding reads ServiceName from SharedKernel:Messaging.
//   3. A service name that is not a lowercase slug fails IHost.StartAsync().
//   4. WithInboundRequestContext() makes IRequestContext message-aware without breaking the host's own.
//   5. WithIdempotency() refuses to build without a registered store.
//   6. A transport is mandatory.

using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Messaging.Abstractions.Context;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Primitives.Health;

// Aliased: MassTransit declares its own IMessageScheduler, and a consuming service that wires
// delayed delivery has both in scope.
using SkMessageScheduler = SharedKernel.Messaging.Abstractions.Scheduling.IMessageScheduler;

await Surface1_TheInjectableSurfaceResolves();
await Surface2_ConfigurationBindingReadsServiceName();
await Surface3_InvalidServiceNameFailsAtStartup();
await Surface4_InboundRequestContextIsMessageAware();
Surface5_IdempotencyRequiresAStore();
Surface6_TransportIsMandatory();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");
return;

// -------------------------------------------------------------------------------------------
// Surface 1 — everything a consuming service injects resolves from one registration chain.
// -------------------------------------------------------------------------------------------
static async Task Surface1_TheInjectableSurfaceResolves()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();

    builder.Services
        .AddSharedKernelMessaging(o => o.ServiceName = "consumer-verify-service")
        .UseRabbitMq("rabbitmq://localhost")
        .WithRetry()
        .WithDelayedDelivery()
        .WithAmbientCorrelationPropagation()
        .AddConsumer<VerifyConsumer>()
        .Build();

    using IHost host = builder.Build();

    // Deliberately NOT StartAsync: that would open a connection to a broker this harness has no
    // reason to require. Everything below is resolved from the built container instead.
    using IServiceScope scope = host.Services.CreateScope();

    Require(scope.ServiceProvider.GetService<IMessageBus>() is not null, "IMessageBus resolves");
    Require(scope.ServiceProvider.GetService<IEventPublisher>() is not null, "IEventPublisher resolves");
    Require(scope.ServiceProvider.GetService<SkMessageScheduler>() is not null, "IMessageScheduler resolves with delayed delivery");
    Require(
        host.Services.GetServices<IReadinessProbe>().Any(p => p.Name == MessagingReadinessProbeNames.Bus),
        "the bus readiness probe is registered, with no opt-in");
    Require(host.Services.GetService<IBus>() is not null, "MassTransit's own IBus is registered");

    Console.WriteLine("Surface 1 PASSED — IMessageBus, IEventPublisher, IMessageScheduler and the bus readiness probe resolve");
    await Task.CompletedTask;
}

// -------------------------------------------------------------------------------------------
// Surface 2 — the configuration entry point binds the section the options type declares.
// -------------------------------------------------------------------------------------------
static async Task Surface2_ConfigurationBindingReadsServiceName()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SharedKernel:Messaging:ServiceName"] = "orders-api",
    });

    builder.Services
        .AddSharedKernelMessaging(builder.Configuration)
        .UseRabbitMq("rabbitmq://localhost")
        .Build();

    using IHost host = builder.Build();

    MessagingOptions options = host.Services.GetRequiredService<IOptions<MessagingOptions>>().Value;
    Require(options.ServiceName == "orders-api", "ServiceName is bound from SharedKernel:Messaging");

    Console.WriteLine("Surface 2 PASSED — ServiceName binds from configuration with no call site naming the section");
    await Task.CompletedTask;
}

// -------------------------------------------------------------------------------------------
// Surface 3 — a bad service name is a startup failure, not a malformed queue name later.
// -------------------------------------------------------------------------------------------
static async Task Surface3_InvalidServiceNameFailsAtStartup()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        // Legal-looking, and wrong: this becomes a queue-name prefix and a CloudEvents source.
        ["SharedKernel:Messaging:ServiceName"] = "Orders API",
    });

    builder.Services.AddSharedKernelMessaging(builder.Configuration);

    using IHost host = builder.Build();

    OptionsValidationException? failure = null;
    try
    {
        await host.StartAsync();
    }
    catch (OptionsValidationException ex)
    {
        failure = ex;
    }

    Require(failure is not null, "IHost.StartAsync() fails on a service name that is not a lowercase slug");
    Require(failure!.Message.Contains("ServiceName", StringComparison.Ordinal), "the failure names the offending option");

    Console.WriteLine("Surface 3 PASSED — an invalid ServiceName fails IHost.StartAsync()");
}

// -------------------------------------------------------------------------------------------
// Surface 4 — the inbound-context feature replaces IRequestContext without losing the host's own.
// -------------------------------------------------------------------------------------------
static async Task Surface4_InboundRequestContextIsMessageAware()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    var tenantId = new TenantId(Guid.NewGuid());

    // The service's own request context, registered first — the documented order.
    builder.Services.AddScoped<IRequestContext>(_ => new VerifyHostRequestContext(tenantId));

    builder.Services
        .AddSharedKernelMessaging(o => o.ServiceName = "consumer-verify-service")
        .UseRabbitMq("rabbitmq://localhost")
        .WithInboundRequestContext()
        .Build();

    using IHost host = builder.Build();
    using IServiceScope scope = host.Services.CreateScope();

    var requestContext = scope.ServiceProvider.GetRequiredService<IRequestContext>();
    var inbound = scope.ServiceProvider.GetRequiredService<IInboundMessageContextAccessor>();

    Require(inbound.Current is null, "outside a consume there is no inbound message context");
    Require(requestContext.TenantId == tenantId, "outside a consume IRequestContext still answers for the host's caller");
    Require(requestContext.UserId == "host-user", "the host's identity is preserved, not replaced");

    Console.WriteLine("Surface 4 PASSED — IRequestContext stays the host's outside a consume");
    await Task.CompletedTask;
}

// -------------------------------------------------------------------------------------------
// Surface 5 — a feature with an unmet prerequisite refuses to build, with the fix in the message.
// -------------------------------------------------------------------------------------------
static void Surface5_IdempotencyRequiresAStore()
{
    var services = new ServiceCollection();
    var builder = services
        .AddSharedKernelMessaging(o => o.ServiceName = "consumer-verify-service")
        .UseRabbitMq("rabbitmq://localhost")
        .WithIdempotency();

    InvalidOperationException? failure = Catch<InvalidOperationException>(() => builder.Build());

    Require(failure is not null, "WithIdempotency() without a store fails at Build()");
    Require(failure!.Message.Contains("IIdempotencyStore", StringComparison.Ordinal), "the failure names the missing contract");

    Console.WriteLine("Surface 5 PASSED — WithIdempotency() refuses to build without a registered store");
}

// -------------------------------------------------------------------------------------------
// Surface 6 — a bus with no transport is a configuration error, not a silently inert bus.
// -------------------------------------------------------------------------------------------
static void Surface6_TransportIsMandatory()
{
    var services = new ServiceCollection();
    var builder = services.AddSharedKernelMessaging(o => o.ServiceName = "consumer-verify-service");

    InvalidOperationException? failure = Catch<InvalidOperationException>(() => builder.Build());

    Require(failure is not null, "Build() without a transport throws");
    Require(failure!.Message.Contains("UseRabbitMq", StringComparison.Ordinal), "the failure names the call that fixes it");

    Console.WriteLine("Surface 6 PASSED — a transport is mandatory");
}

// -------------------------------------------------------------------------------------------
// Helpers
// -------------------------------------------------------------------------------------------
static void Require(bool condition, string what)
{
    if (!condition)
    {
        Console.Error.WriteLine($"FAILED: {what}");
        Environment.Exit(1);
    }
}

static TException? Catch<TException>(Action act)
    where TException : Exception
{
    try
    {
        act();
        return null;
    }
    catch (TException ex)
    {
        return ex;
    }
}

/// <summary>A consumer, present so the endpoint-naming and registration path is exercised.</summary>
internal sealed class VerifyConsumer : ConsumerBase<VerifyMessage>
{
    public VerifyConsumer(ILogger<VerifyConsumer> logger)
        : base(logger)
    {
    }

    protected override Task ConsumeAsync(VerifyMessage message, CancellationToken ct)
        => Task.CompletedTask;
}

/// <summary>The message <see cref="VerifyConsumer"/> consumes.</summary>
internal sealed record VerifyMessage(string Text);

/// <summary>Stands in for the request context a real service registers at its composition root.</summary>
internal sealed class VerifyHostRequestContext : IRequestContext
{
    public VerifyHostRequestContext(TenantId tenantId) => TenantId = tenantId;

    public bool IsAuthenticated => true;

    public string? UserId => "host-user";

    public TenantId? TenantId { get; }

    public ActorKind ActorKind => ActorKind.User;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(true);
}
