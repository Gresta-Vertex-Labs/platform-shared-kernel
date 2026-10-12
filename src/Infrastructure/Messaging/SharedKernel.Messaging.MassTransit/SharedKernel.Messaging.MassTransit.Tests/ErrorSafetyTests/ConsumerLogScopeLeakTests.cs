#pragma warning disable CS8602 // MassTransit harness nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Messaging.MassTransit.Tests.ErrorSafetyTests;

/// <summary>
/// SK.07.MessagingErrorSafety T-01: no actor, client, tenant or idempotency-key value reaches the log scope of a consume,
/// and the allow-list that decides what does is closed.
/// </summary>
public sealed class ConsumerLogScopeLeakTests
{
    private const string ActorIdSentinel = "sentinel-actor-id-7f3a";
    private const string ClientIdSentinel = "sentinel-client-id-91cc";
    private const string TenantSentinel = "sentinel-tenant-2b6e";
    private const string IdempotencyKeySentinel = "sentinel-idempotency-key-44d0";
    private const string CustomSentinel = "sentinel-custom-header-0a17";

    [Fact]
    public async Task Consume_WithIdentityHeaders_NoScopeEntryCarriesASentinel()
    {
        var logger = new InMemoryLogger();
        await using var provider = BuildProvider(logger);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ScopeLeakMessage("leak-check"), pipe =>
        {
            pipe.Headers.Set(WellKnownHeaders.ActorId, ActorIdSentinel);
            pipe.Headers.Set(WellKnownHeaders.ClientId, ClientIdSentinel);
            pipe.Headers.Set(WellKnownHeaders.TenantId, TenantSentinel);
            pipe.Headers.Set(WellKnownHeaders.IdempotencyKey, IdempotencyKeySentinel);
            pipe.Headers.Set(WellKnownHeaders.ActorKind, "User");
            pipe.Headers.Set("x-sk-custom", CustomSentinel);
        });

        (await harness.Consumed.Any<ScopeLeakMessage>()).Should().BeTrue();

        var scopes = ScopesOf(logger);
        scopes.Should().NotBeEmpty("the consumer logged inside the scope ConsumerBase opens");

        var entries = scopes.SelectMany(s => s).ToList();
        string[] sentinels =
            [ActorIdSentinel, ClientIdSentinel, TenantSentinel, IdempotencyKeySentinel, CustomSentinel];
        foreach (var entry in entries)
        {
            foreach (var sentinel in sentinels)
            {
                entry.Key.Should().NotContain(sentinel);
                (entry.Value?.ToString() ?? string.Empty).Should().NotContain(sentinel);
            }
        }

        var keys = entries.Select(e => e.Key).ToList();
        keys.Should().Contain(MessagingLogScope.CorrelationIdKey);
        keys.Should().Contain("MessageType");
        keys.Should().Contain("ActorKind");
        keys.Should().NotContain(k => k.StartsWith("x-sk-", StringComparison.OrdinalIgnoreCase),
            "no header name is a scope key any more");
        entries.Single(e => e.Key == "ActorKind").Value.Should().Be("User");

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_WithoutActorKindHeader_ScopeHasNoActorKind()
    {
        var logger = new InMemoryLogger();
        await using var provider = BuildProvider(logger);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ScopeLeakMessage("no-kind"));

        (await harness.Consumed.Any<ScopeLeakMessage>()).Should().BeTrue();

        ScopesOf(logger).SelectMany(s => s).Select(e => e.Key).Should().NotContain("ActorKind");

        await harness.Stop();
    }

    [Fact]
    public void AllowList_IsClosed_ActorKindOnly()
    {
        // Adding a header to the scope is a reviewed change: this test fails until it is made deliberately,
        // together with the L5 reasoning documented on MessagingLogScope.AllowedHeaders.
        MessagingLogScope.AllowedHeaders.Should().BeEquivalentTo(
            new Dictionary<string, string> { [WellKnownHeaders.ActorKind] = "ActorKind" });
    }

    [Fact]
    public void AddAllowedHeaders_CopiesOnlyAllowListed_CaseInsensitively()
    {
        var scope = MessagingLogScope.Create(correlationId: null);
        List<KeyValuePair<string, object>> headers =
        [
            new("X-SK-ACTOR-KIND", "Service"),
            new(WellKnownHeaders.ActorId, ActorIdSentinel),
            new(WellKnownHeaders.ClientId, ClientIdSentinel),
            new(WellKnownHeaders.TenantId, TenantSentinel),
            new(WellKnownHeaders.IdempotencyKey, IdempotencyKeySentinel),
            new(WellKnownHeaders.CorrelationId, "c"),
        ];

        MessagingLogScope.AddAllowedHeaders(scope, headers);

        scope.Keys.Should().BeEquivalentTo([MessagingLogScope.CorrelationIdKey, "ActorKind"]);
        scope["ActorKind"].Should().Be("Service");
    }

    private static List<IReadOnlyDictionary<string, object?>> ScopesOf(InMemoryLogger logger) =>
    [
        .. logger.Records
            .Where(r => r.EventId.Id == ScopeLeakConsumer.ProbeEventId)
            .SelectMany(r => r.Scopes)
            .OfType<IReadOnlyDictionary<string, object?>>(),
    ];

    private static ServiceProvider BuildProvider(InMemoryLogger logger) =>
        new ServiceCollection()
            .AddSingleton(logger)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<ScopeLeakConsumer>();
                cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
            })
            .BuildServiceProvider(true);
}

public sealed record ScopeLeakMessage(string Text);

public sealed class ScopeLeakConsumer(InMemoryLogger logger) : ConsumerBase<ScopeLeakMessage>(logger)
{
    public const int ProbeEventId = 99001;

    protected override Task ConsumeAsync(ScopeLeakMessage message, CancellationToken ct)
    {
        // A record logged inside ConsumerBase's BeginScope captures the scope stack for the assertions.
        Logger.Log(LogLevel.Information, new EventId(ProbeEventId), "inside the consume scope", null, (s, _) => s);
        return Task.CompletedTask;
    }
}
