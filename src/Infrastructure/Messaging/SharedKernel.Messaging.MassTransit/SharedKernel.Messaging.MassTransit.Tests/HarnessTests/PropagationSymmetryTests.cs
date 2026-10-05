using System.Collections.ObjectModel;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.MessageBus;

// Alias to avoid ambiguity with MassTransit.PublishContext in test types.
using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// SK.07.PropagationSymmetry / P-341: registered <see cref="IMessageHeaderPropagator"/>s must run
/// identically before <c>IMessageBus.SendAsync</c> and <c>IMessageBus.RequestAsync</c>, not just
/// <c>PublishAsync</c> — this was a confirmed silent asymmetry, now fixed in
/// <see cref="MassTransitMessageBus"/>.
/// </summary>
public sealed class PropagationSymmetryTests
{
    // -------------------------------------------------------------------------
    // PS-04: SendAsync — registered propagator header present on consumed message
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_WithPropagator_HeaderPresentOnConsumedMessage()
    {
        // Arrange
        PsSendHeaderCaptureStore.Reset();

        const string queueName = "ps-send-symmetry-queue";
        var routeMap = new Dictionary<Type, string> { [typeof(PsSendTestCommand)] = queueName };

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<PsSendCapturingConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.ReceiveEndpoint(queueName, e => e.ConfigureConsumer<PsSendCapturingConsumer>(ctx));
                });
            })
            .AddScoped<IMessageHeaderPropagator, PsSendHeaderPropagator>()
            .AddSingleton<IReadOnlyDictionary<Type, string>>(new ReadOnlyDictionary<Type, string>(routeMap))
            .AddScoped<ConventionSendEndpointResolver>()
            .AddScoped<IMessageBus, MassTransitMessageBus>()
            .Configure<MessagingOptions>(o => o.ServiceName = "ps-test-service")
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Resolve IMessageBus from a scope so the propagator is also scoped correctly.
        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // Act
        await bus.SendAsync(new PsSendTestCommand("payload"), CancellationToken.None);

        // Wait for the consumer to process the message.
        (await harness.Consumed.Any<PsSendTestCommand>()).Should().BeTrue();

        // Assert: the header set by the registered propagator reached the consumed message —
        // SendAsync must apply propagators identically to PublishAsync (P-341).
        PsSendHeaderCaptureStore.CapturedHeaders.Should().ContainKey("x-sk-ps-send",
            "SendAsync must run registered IMessageHeaderPropagators before dispatch, identically to PublishAsync");
        PsSendHeaderCaptureStore.CapturedHeaders["x-sk-ps-send"].Should().Be("from-send-propagator");

        await harness.Stop();
    }

}

// ---------------------------------------------------------------------------
// Message types — internal, no 'file' modifier (breaks MassTransit type matching)
// ---------------------------------------------------------------------------

internal sealed record PsSendTestCommand(string Text);

// ---------------------------------------------------------------------------
// Static stores for cross-scope state capture
// ---------------------------------------------------------------------------

internal static class PsSendHeaderCaptureStore
{
    private static Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyDictionary<string, string> CapturedHeaders => _headers;
    public static void Capture(string key, string value) => _headers[key] = value;
    public static void Reset() => _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}


// ---------------------------------------------------------------------------
// Propagators
// ---------------------------------------------------------------------------

internal sealed class PsSendHeaderPropagator : IMessageHeaderPropagator
{
    public void Propagate(MessagingPublishContext context)
        => context.WithHeader("x-sk-ps-send", "from-send-propagator");
}


// ---------------------------------------------------------------------------
// Consumers
// ---------------------------------------------------------------------------

/// Captures headers from ConsumeContext into PsSendHeaderCaptureStore for assertion.
internal sealed class PsSendCapturingConsumer : IConsumer<PsSendTestCommand>
{
    public Task Consume(ConsumeContext<PsSendTestCommand> context)
    {
        foreach (var header in context.Headers.GetAll())
        {
            if (header.Value is not null)
                PsSendHeaderCaptureStore.Capture(header.Key, header.Value.ToString() ?? string.Empty);
        }
        return Task.CompletedTask;
    }
}

/// Captures the incoming request's CorrelationId, then responds so RequestAsync completes.
