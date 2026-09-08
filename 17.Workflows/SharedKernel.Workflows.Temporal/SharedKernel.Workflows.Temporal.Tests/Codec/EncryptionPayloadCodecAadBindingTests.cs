using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Cryptography;
using SharedKernel.Workflows.Temporal.Codec;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;

namespace SharedKernel.Workflows.Temporal.Tests.Codec;

/// <summary>
/// T-17–T-20 (P-501/WO-081) — <see cref="EncryptionPayloadCodec"/>'s async migration and
/// associated-data (AAD) binding to the resolved Temporal <c>WorkflowId</c>.
/// </summary>
public sealed class EncryptionPayloadCodecAadBindingTests
{
    private const string Namespace = "test-namespace";

    /// <summary>
    /// A decorator whose synchronous <see cref="ISymmetricEncryptionService"/> members throw
    /// unconditionally — simulating the post-P-492 state once a KMS-backed
    /// <c>IEncryptionKeyProvider</c> is registered, where <c>AesGcmEncryptionService</c>'s retained
    /// sync members throw <see cref="NotSupportedException"/> rather than silently blocking a
    /// thread. Every async member delegates to the real inner service unchanged.
    /// </summary>
    private sealed class AsyncOnlyEncryptionService(ISymmetricEncryptionService inner) : ISymmetricEncryptionService
    {
        private static NotSupportedException SyncMemberCalled() =>
            new("EncryptionPayloadCodec must never call a synchronous ISymmetricEncryptionService member.");

        public EncryptedPayload Encrypt(byte[] plaintext, byte[] associatedData) => throw SyncMemberCalled();

        public Result<byte[]> Decrypt(EncryptedPayload payload, byte[] associatedData) => throw SyncMemberCalled();

        public string EncryptToString(string plaintext, byte[] associatedData) => throw SyncMemberCalled();

        public Result<string> DecryptToString(string encoded, byte[] associatedData) => throw SyncMemberCalled();

        public ValueTask<EncryptedPayload> EncryptAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct = default) =>
            inner.EncryptAsync(plaintext, associatedData, ct);

        public ValueTask<Result<byte[]>> DecryptAsync(EncryptedPayload payload, byte[] associatedData, CancellationToken ct = default) =>
            inner.DecryptAsync(payload, associatedData, ct);

        public ValueTask<string> EncryptToStringAsync(string plaintext, byte[] associatedData, CancellationToken ct = default) =>
            inner.EncryptToStringAsync(plaintext, associatedData, ct);

        public ValueTask<Result<string>> DecryptToStringAsync(string encoded, byte[] associatedData, CancellationToken ct = default) =>
            inner.DecryptToStringAsync(encoded, associatedData, ct);
    }

    private static Payload PlaintextPayload(string text)
    {
        var payload = new Payload();
        payload.Metadata["encoding"] = ByteString.CopyFromUtf8("json/plain");
        payload.Data = ByteString.CopyFromUtf8($"\"{text}\"");
        return payload;
    }

    // T-17 — async-migration proof: a double whose sync Encrypt/Decrypt throw unconditionally must
    // still round-trip successfully through EncodeAsync/DecodeAsync, proving the codec calls only the
    // *Async members.
    [Fact]
    public async Task EncodeThenDecode_AgainstSyncThrowingEncryptionService_StillRoundTrips()
    {
        var syncThrowing = new AsyncOnlyEncryptionService(new FakeSymmetricEncryptionService());
        var codec = new EncryptionPayloadCodec(syncThrowing, NullLogger<EncryptionPayloadCodec>.Instance);

        Payload original = PlaintextPayload("async-only-round-trip");
        IReadOnlyCollection<Payload> encoded = await codec.EncodeAsync([original]);
        IReadOnlyCollection<Payload> decoded = await codec.DecodeAsync(encoded);

        decoded.Single().Should().Be(original);
    }

    // T-18 (headline) — a payload encoded under one WorkflowId context must fail to decode under a
    // different WorkflowId context: a captured ciphertext can never be replayed against a different
    // workflow execution.
    [Fact]
    public async Task Decode_UnderDifferentWorkflowIdContext_ThrowsRatherThanSucceeding()
    {
        var service = new FakeSymmetricEncryptionService();
        var baseCodec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        IPayloadCodec codecForWfA = baseCodec.WithSerializationContext(new ISerializationContext.Workflow(Namespace, "wf-a"));
        IPayloadCodec codecForWfB = baseCodec.WithSerializationContext(new ISerializationContext.Workflow(Namespace, "wf-b"));

        Payload original = PlaintextPayload("bound-to-wf-a");
        IReadOnlyCollection<Payload> encoded = await codecForWfA.EncodeAsync([original]);

        Func<Task> act = () => codecForWfB.DecodeAsync(encoded);

        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "a ciphertext captured for one WorkflowId must never decode under a different WorkflowId's context");
    }

    // T-18 (companion, positive case) — encode and decode under the identical WorkflowId context
    // must succeed.
    [Fact]
    public async Task Decode_UnderTheSameWorkflowIdContext_Succeeds()
    {
        var service = new FakeSymmetricEncryptionService();
        var baseCodec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        IPayloadCodec codecForWfA = baseCodec.WithSerializationContext(new ISerializationContext.Workflow(Namespace, "wf-a"));

        Payload original = PlaintextPayload("bound-to-wf-a");
        IReadOnlyCollection<Payload> encoded = await codecForWfA.EncodeAsync([original]);
        IReadOnlyCollection<Payload> decoded = await codecForWfA.DecodeAsync(encoded);

        decoded.Single().Should().Be(original);
    }

    // T-19 — the base, unbound codec instance (WithSerializationContext never called) must encode
    // and decode successfully using Array.Empty<byte>() AAD.
    [Fact]
    public async Task EncodeThenDecode_OnTheBaseUnboundCodec_Succeeds()
    {
        var service = new FakeSymmetricEncryptionService();
        var codec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        Payload original = PlaintextPayload("no-context-ever-supplied");
        IReadOnlyCollection<Payload> encoded = await codec.EncodeAsync([original]);
        IReadOnlyCollection<Payload> decoded = await codec.DecodeAsync(encoded);

        decoded.Single().Should().Be(original);
    }

    // T-19 — an ISerializationContext.Activity with a null WorkflowId (the SDK's own documented
    // "standalone activity" case) must fall back to the same empty-AAD behavior rather than throwing,
    // and must interoperate with the base unbound codec (both resolve to the same empty AAD).
    [Fact]
    public async Task EncodeUnderActivityContextWithNullWorkflowId_DecodesUnderTheBaseUnboundCodec()
    {
        var service = new FakeSymmetricEncryptionService();
        var baseCodec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        var standaloneActivityContext = new ISerializationContext.Activity(
            Namespace, "act-1", null!, "wfType", "actType", "queue", IsLocal: false);
        IPayloadCodec codecForStandaloneActivity = baseCodec.WithSerializationContext(standaloneActivityContext);

        Payload original = PlaintextPayload("standalone-activity-no-workflow-id");
        IReadOnlyCollection<Payload> encoded = await codecForStandaloneActivity.EncodeAsync([original]);

        // A null-WorkflowId Activity context must derive the identical empty AAD the base, unbound
        // codec uses — decoding via the base codec directly must succeed.
        IReadOnlyCollection<Payload> decoded = await baseCodec.DecodeAsync(encoded);

        decoded.Single().Should().Be(original);
    }

    [Fact]
    public void DeriveAssociatedData_NullOrEmptyWorkflowId_ReturnsEmptyArray()
    {
        EncryptionPayloadCodec.DeriveAssociatedData(null).Should().BeEmpty();
        EncryptionPayloadCodec.DeriveAssociatedData(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void DeriveAssociatedData_GivenAWorkflowId_ReturnsItsUtf8Bytes()
    {
        byte[] aad = EncryptionPayloadCodec.DeriveAssociatedData("order-42:workflow:business-key");

        aad.Should().Equal(System.Text.Encoding.UTF8.GetBytes("order-42:workflow:business-key"));
    }

    // T-20 — regression: the WorkflowFailureMapper/WorkflowErrors.PayloadCodecFailure mapping for a
    // decrypt failure is byte-for-byte unchanged by this migration, including under an AAD mismatch.
    [Fact]
    public async Task Decode_UnderAMismatchedWorkflowIdContext_SurfacesThePayloadCodecFailureMessageShape()
    {
        var service = new FakeSymmetricEncryptionService();
        var baseCodec = new EncryptionPayloadCodec(service, NullLogger<EncryptionPayloadCodec>.Instance);

        IPayloadCodec codecForWfA = baseCodec.WithSerializationContext(new ISerializationContext.Workflow(Namespace, "wf-a"));
        IPayloadCodec codecForWfB = baseCodec.WithSerializationContext(new ISerializationContext.Workflow(Namespace, "wf-b"));

        Payload original = PlaintextPayload("mismatch-message-shape");
        IReadOnlyCollection<Payload> encoded = await codecForWfA.EncodeAsync([original]);

        Func<Task> act = () => codecForWfB.DecodeAsync(encoded);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().StartWith("The payload codec failed:");
    }
}
