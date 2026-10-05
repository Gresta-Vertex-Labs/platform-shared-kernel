#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// PA-14 (P-499): the core structural proof the payload-transform AAD design depends on — the
/// <see cref="Serialization.PayloadTransformHeaders.MessageTypeAad"/> header set inside
/// <see cref="Serialization.PayloadTransformMessageSerializer.GetMessageBody{T}"/> at publish time
/// is genuinely observed by <see cref="Serialization.PayloadTransformMessageDeserializer"/> on
/// consume, through a real <see cref="MessagingBusBuilder"/>/<c>TestHarness</c> round trip — never
/// a hand-constructed <c>SendContext</c>/<c>Headers</c> pair (that unit-level proof is
/// <c>PayloadTransformAadTests</c>, PA-11/PA-12/PA-13).
/// </summary>
/// <remarks>
/// Proven via successful end-to-end decryption rather than by a consumer independently reading
/// <c>ConsumeContext.Headers</c> — see the "MassTransit 9.x API notes" entry in
/// `src/Infrastructure/Messaging/CLAUDE.md` on why <c>ConsumeContext.Headers</c> reflects the JSON envelope's own
/// embedded header snapshot (captured when the inner STJ serializer builds the envelope, BEFORE
/// this decorator's own <c>context.Headers.Set(...)</c> call runs) and therefore does NOT surface
/// this specific header, even though the raw transport <c>Headers</c> parameter
/// <see cref="Serialization.PayloadTransformMessageDeserializer.Deserialize(MassTransit.MessageBody, MassTransit.Headers, System.Uri?)"/>
/// receives genuinely does. AES-GCM's authentication tag makes a successful decrypt here
/// cryptographically impossible unless the consume-side deserializer reproduced the exact AAD
/// bytes the publish-side serializer used — which is only possible if the header crossed the real
/// transport intact. That is the structural proof this test exists to establish.
/// </remarks>
public sealed class PayloadTransformAadHarnessTests
{
    [Fact]
    public async Task Publish_EncryptionEnabled_RealHarnessRoundTrip_ConsumerDecryptsSuccessfully()
    {
        PayloadTransformAadHarnessTracker.Reset();

        var encryptionService = new SynchronousAesGcmEncryptionService(new FakeEncryptionKeyProvider());

        await using var provider = new ServiceCollection()
            .AddSingleton<ISynchronousSymmetricEncryptionService>(encryptionService)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<PayloadTransformAadHarnessConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    var options = new PayloadTransformOptions { EnableEncryption = true };
                    MessagingBusBuilder.ConfigurePayloadTransform(busCfg, ctx, options);
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new PayloadTransformAadHarnessTestMessage("aad-over-the-wire", 99);

        await harness.Bus.Publish(message);

        (await harness.Consumed.Any<PayloadTransformAadHarnessTestMessage>()).Should().BeTrue(
            "the consumer can only receive a successfully-decrypted message, which is only " +
            "cryptographically possible (AES-GCM authenticated encryption) if the consume-side " +
            "deserializer reproduced the exact type-derived AAD the publisher used — proving the " +
            "AAD transport header genuinely crossed the real in-memory transport intact");

        (await harness.Consumed.Any<Fault<PayloadTransformAadHarnessTestMessage>>()).Should().BeFalse(
            "a mismatched/missing AAD would fail AES-GCM authentication and fault the message " +
            "instead of delivering it");

        PayloadTransformAadHarnessTracker.Received.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(message);

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Message type — internal, no 'file' modifier (MassTransit type matching)
// ---------------------------------------------------------------------------

internal sealed record PayloadTransformAadHarnessTestMessage(string Text, int Number);

// ---------------------------------------------------------------------------
// Static tracker for cross-scope consumer invocation
// ---------------------------------------------------------------------------

internal static class PayloadTransformAadHarnessTracker
{
    private static System.Collections.Concurrent.ConcurrentBag<PayloadTransformAadHarnessTestMessage> _received = [];

    public static System.Collections.Concurrent.ConcurrentBag<PayloadTransformAadHarnessTestMessage> Received => _received;

    public static void Reset() => _received = [];
}

// ---------------------------------------------------------------------------
// Consumer — internal, no 'file' modifier
// ---------------------------------------------------------------------------

internal sealed class PayloadTransformAadHarnessConsumer : ConsumerBase<PayloadTransformAadHarnessTestMessage>
{
    public PayloadTransformAadHarnessConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(PayloadTransformAadHarnessTestMessage message, CancellationToken ct)
    {
        PayloadTransformAadHarnessTracker.Received.Add(message);
        return Task.CompletedTask;
    }
}
