#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using System.Collections.Concurrent;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Compression;
using SharedKernel.Compression.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// PT-10: Round-trip test — compression and encryption both enabled; the consumer receives the
///        correctly deserialized original payload.
/// PT-12: Default-disabled regression — publish/consume behaves identically when
///        <c>WithPayloadTransform</c> is never wired into the bus configuration.
/// </summary>
/// <remarks>
/// Exercises <see cref="MessagingBusBuilder.ConfigurePayloadTransform"/> directly against a
/// <c>UsingInMemory</c> test bus (mirroring the pattern established for idempotency/header-propagation
/// tests) with a genuinely real <see cref="BrotliPayloadCompressor"/> and AES-GCM
/// <see cref="SynchronousAesGcmEncryptionService"/> — proving an end-to-end compress-then-encrypt /
/// decrypt-then-decompress round trip through MassTransit's actual serialization pipeline, not merely
/// that the wiring delegates somewhere.
/// </remarks>
public sealed class PayloadTransformRoundTripTests
{
    [Fact]
    public async Task RoundTrip_CompressionAndEncryptionEnabled_ConsumerReceivesCorrectPayload()
    {
        PayloadTransformRoundTripTracker.Reset();

        var compressor = new BrotliPayloadCompressor(Microsoft.Extensions.Options.Options.Create(new CompressionOptions()));
        var encryptionService = new SynchronousAesGcmEncryptionService(new FakeEncryptionKeyProvider());

        await using var provider = new ServiceCollection()
            .AddSingleton<IPayloadCompressor>(compressor)
            .AddSingleton<ISynchronousSymmetricEncryptionService>(encryptionService)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<PayloadTransformRecordingConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    var options = new PayloadTransformOptions { EnableCompression = true, EnableEncryption = true };
                    MessagingBusBuilder.ConfigurePayloadTransform(busCfg, ctx, options);
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new PayloadTransformTestMessage(
            "the quick brown fox jumps over the lazy dog — repeated text compresses well. "
            + "the quick brown fox jumps over the lazy dog — repeated text compresses well.",
            42);

        await harness.Bus.Publish(message);

        (await harness.Consumed.Any<PayloadTransformTestMessage>()).Should().BeTrue(
            "the consumer must receive the message after the compress-then-encrypt / " +
            "decrypt-then-decompress round trip");

        PayloadTransformRoundTripTracker.Received.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(message,
                "the deserialized payload must exactly match what was published");

        await harness.Stop();
    }

    [Fact]
    public async Task DefaultDisabled_WithPayloadTransformNeverWired_PublishConsumeUnchanged()
    {
        PayloadTransformRoundTripTracker.Reset();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<PayloadTransformRecordingConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    // No ConfigurePayloadTransform call — default STJ serializer, unmodified.
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new PayloadTransformTestMessage("plain, untransformed payload", 7);

        await harness.Bus.Publish(message);

        (await harness.Consumed.Any<PayloadTransformTestMessage>()).Should().BeTrue();

        PayloadTransformRoundTripTracker.Received.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(message);

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Message type — internal, no 'file' modifier (MassTransit type matching)
// ---------------------------------------------------------------------------

internal sealed record PayloadTransformTestMessage(string Text, int Number);

// ---------------------------------------------------------------------------
// Static tracker for cross-scope consumer invocation
// ---------------------------------------------------------------------------

internal static class PayloadTransformRoundTripTracker
{
    private static ConcurrentBag<PayloadTransformTestMessage> _received = [];

    public static ConcurrentBag<PayloadTransformTestMessage> Received => _received;

    public static void Reset() => _received = [];
}

// ---------------------------------------------------------------------------
// Consumer — internal, no 'file' modifier
// ---------------------------------------------------------------------------

internal sealed class PayloadTransformRecordingConsumer : ConsumerBase<PayloadTransformTestMessage>
{
    public PayloadTransformRecordingConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(PayloadTransformTestMessage message, CancellationToken ct)
    {
        PayloadTransformRoundTripTracker.Received.Add(message);
        return Task.CompletedTask;
    }
}
