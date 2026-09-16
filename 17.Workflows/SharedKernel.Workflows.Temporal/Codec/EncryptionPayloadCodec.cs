using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Errors;
using SharedKernel.Workflows.Temporal.Logging;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;

namespace SharedKernel.Workflows.Temporal.Codec;

/// <summary>
/// Wraps <c>01.Core</c>'s <see cref="ISymmetricEncryptionService"/> (AES-256-GCM) as a Temporal
/// <see cref="IPayloadCodec"/>, enabled by <c>.WithPayloadEncryption()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Temporal persists every workflow input, output, signal payload, and activity argument in the
/// server's event history in full, retained for the namespace's whole retention period and readable
/// by anyone with namespace access — including the Temporal Web UI. On a shared or managed cluster
/// that is a permanent plaintext copy of the domain data the workflow touched. This codec is the only
/// thing standing between that data and permanent plaintext storage.
/// </para>
/// <para>
/// Each encoded payload carries the entire original <see cref="Payload"/> (data and metadata) as
/// ciphertext, so decoding restores the original payload byte-for-byte — including whatever encoding
/// metadata the underlying <see cref="IPayloadConverter"/> had set. The encoded payload's
/// <see cref="Payload.Data"/> is <c>01.Core</c>'s canonical <see cref="EncryptedPayload.ToBytes"/> layout
/// (format version, <see cref="EncryptedPayload.KeyId"/>, nonce, tag, ciphertext), and its only metadata
/// entry is the <c>encoding</c> marker. The key id therefore travels with every payload, so an old
/// key can remain configured for as long as any history encrypted under it might still replay — which
/// for workflows can be months longer than the equivalent database-column case, because a workflow
/// started under key v1 will still replay under key v1 on its final day.
/// </para>
/// <para>
/// A payload not carrying this codec's encoding marker is passed through unchanged on decode — the
/// standard Temporal payload-codec-chain convention, letting multiple codecs coexist. A payload that
/// <em>does</em> carry the marker but is malformed or fails to decrypt (tamper, wrong key, unknown key id,
/// or a mismatched associated-data binding — see below) throws rather than silently passing the
/// ciphertext through as plaintext.
/// </para>
/// <para>
/// <b>Associated-data (AAD) binding (P-501/WO-081).</b> Every encrypt/decrypt call binds AES-GCM's
/// associated-data parameter to the resolved Temporal <c>WorkflowId</c> — the UTF-8 bytes of the id
/// when one is resolvable, <see cref="Array.Empty{T}"/> otherwise (see <see cref="DeriveAssociatedData"/>).
/// A ciphertext captured for one workflow execution can never be replayed to decode successfully
/// under a different <c>WorkflowId</c>'s context — a mismatch fails authentication exactly like a
/// tampered ciphertext or wrong key, surfacing as <see cref="WorkflowErrors.PayloadCodecFailure"/>.
/// </para>
/// <para>
/// <b>Why <c>RunId</c> is deliberately excluded, corrected from the commissioning brief's original
/// "workflow id + run id" acceptance criterion.</b> <c>RunId</c> is absent from every
/// <see cref="ISerializationContext"/> shape the real, compiled <c>Temporalio</c> 1.17.0 assembly
/// exposes — confirmed by reflection, not assumed. Even if it were available, binding to it would be
/// architecturally wrong: Temporal's continue-as-new mechanism and workflow retries assign a
/// <em>new</em> <c>RunId</c> to the <em>same</em> <c>WorkflowId</c> while carrying payload data
/// forward across that boundary, so a <c>RunId</c>-bound AAD would make a continued/retried
/// execution's carried-forward payloads fail to decrypt — a correctness bug wearing a security
/// feature's clothes. <c>WorkflowId</c> is this domain's own idempotency/addressing unit (see
/// <c>IWorkflowIdFactory</c>) and is the correct, and only, binding granularity.
/// </para>
/// <para>
/// <b>Degrading correctly outside a workflow execution context.</b> <see cref="IPayloadCodec"/> is
/// also invoked in contexts detached from any running workflow — CLI/<c>tctl</c> payload inspection,
/// Temporal Web UI payload display, and standalone data-converter operations. In every one of those
/// cases the SDK either never calls <see cref="WithSerializationContext"/> at all, or calls it with a
/// context whose <c>WorkflowId</c> is <see langword="null"/> (the SDK's own documented
/// "standalone activity" shape). Either way this codec falls back to an empty AAD rather than
/// throwing or rendering the payload undecodable — see <see cref="DeriveAssociatedData"/>.
/// </para>
/// </remarks>
internal sealed class EncryptionPayloadCodec : IPayloadCodec, IWithSerializationContext<IPayloadCodec>
{
    private const string EncodingMetadataKey = "encoding";

    /// <summary>
    /// The encoding marker. Versioned: <c>-v2</c> payloads hold <see cref="EncryptedPayload.ToBytes"/> in
    /// <see cref="Payload.Data"/>. The earlier unversioned <c>binary/encrypted-sk</c> layout (key id, nonce
    /// and tag in separate metadata entries) was never published and is not read.
    /// </summary>
    private const string EncodingMetadataValue = "binary/encrypted-sk-v2";

    private readonly ISymmetricEncryptionService _encryptionService;
    private readonly byte[] _associatedData;

    /// <summary>
    /// Initialises the base, unbound codec instance — registered once in DI and resolved into
    /// <c>DataConverter.PayloadCodec</c> by <c>.WithPayloadEncryption()</c>.
    /// </summary>
    /// <param name="encryptionService">The AES-256-GCM encryption service to encrypt/decrypt payloads with.</param>
    /// <param name="logger">The replay-safe logger this codec logs its configuration through.</param>
    public EncryptionPayloadCodec(ISymmetricEncryptionService encryptionService, ILogger<EncryptionPayloadCodec> logger)
    {
        _encryptionService = encryptionService;
        _associatedData = [];
        WorkflowLog.PayloadEncryptionConfigured(logger);
    }

    /// <summary>
    /// Constructs a lightweight clone carrying a resolved associated-data binding, reusing the same
    /// singleton <see cref="ISymmetricEncryptionService"/> instance the base codec was constructed
    /// with (no new allocation, no fresh DI resolution). Never logs — the SDK may call
    /// <see cref="WithSerializationContext"/> many times per process lifetime, and re-logging
    /// "payload encryption configured" on every call would be pure noise.
    /// </summary>
    private EncryptionPayloadCodec(EncryptionPayloadCodec source, byte[] associatedData)
    {
        _encryptionService = source._encryptionService;
        _associatedData = associatedData;
    }

    /// <summary>
    /// Called by the Temporal client SDK before workflow start/signal/query/schedule-create-or-describe
    /// calls (with an <see cref="ISerializationContext.Workflow"/>) and by the worker SDK before
    /// activity invocation (with an <see cref="ISerializationContext.Activity"/>) — both exposing a
    /// <see cref="ISerializationContext.IHasWorkflow.WorkflowId"/>. Returns a new clone bound to the
    /// resolved <c>WorkflowId</c>'s associated data, or <see langword="this"/> unchanged when
    /// <paramref name="context"/> carries no resolvable workflow identity at all — never throws, and
    /// never renders a payload encoded outside a workflow context (e.g. via <c>tctl</c>/Web UI
    /// inspection or a standalone data-converter operation) undecodable.
    /// </summary>
    /// <param name="context">The serialization context the SDK resolved for the pending call.</param>
    /// <returns>
    /// A codec instance whose subsequent <see cref="EncodeAsync"/>/<see cref="DecodeAsync"/> calls bind
    /// AES-GCM's associated data to <paramref name="context"/>'s resolved <c>WorkflowId</c>.
    /// </returns>
    public IPayloadCodec WithSerializationContext(ISerializationContext context)
    {
        if (context is not ISerializationContext.IHasWorkflow hasWorkflow
            || string.IsNullOrEmpty(hasWorkflow.WorkflowId))
        {
            // No resolvable WorkflowId (a standalone activity, or a context shape this codec does not
            // recognise) — fall back to the base, unbound instance rather than fabricating a binding.
            return this;
        }

        return new EncryptionPayloadCodec(this, DeriveAssociatedData(hasWorkflow.WorkflowId));
    }

    /// <summary>
    /// Derives the AES-GCM associated-data bytes for a resolved <c>WorkflowId</c> — the UTF-8 encoding
    /// of <paramref name="workflowId"/> when non-null/non-empty, <see cref="Array.Empty{T}"/> otherwise.
    /// Deterministic and reproducible across the encode/decode boundary: the exact same
    /// <c>WorkflowId</c> string always yields the exact same AAD bytes.
    /// </summary>
    /// <param name="workflowId">The resolved <c>WorkflowId</c>, or <see langword="null"/> when none is available.</param>
    /// <returns>The associated-data bytes to bind an encrypt/decrypt call to.</returns>
    internal static byte[] DeriveAssociatedData(string? workflowId) =>
        string.IsNullOrEmpty(workflowId) ? [] : Encoding.UTF8.GetBytes(workflowId);

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Payload>> EncodeAsync(IReadOnlyCollection<Payload> payloads)
    {
        Payload[] materialized = payloads as Payload[] ?? [.. payloads];
        Payload[] encoded = await Task.WhenAll(materialized.Select(EncodeOneAsync)).ConfigureAwait(false);
        return encoded;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Payload>> DecodeAsync(IReadOnlyCollection<Payload> payloads)
    {
        Payload[] materialized = payloads as Payload[] ?? [.. payloads];
        Payload[] decoded = await Task.WhenAll(materialized.Select(DecodeOneAsync)).ConfigureAwait(false);
        return decoded;
    }

    private async Task<Payload> EncodeOneAsync(Payload original)
    {
        byte[] plaintext = original.ToByteArray();
        EncryptedPayload encrypted = await _encryptionService
            .EncryptAsync(plaintext, _associatedData, CancellationToken.None)
            .ConfigureAwait(false);

        var result = new Payload
        {
            Data = UnsafeByteOperations.UnsafeWrap(encrypted.ToBytes()),
        };
        result.Metadata[EncodingMetadataKey] = ByteString.CopyFromUtf8(EncodingMetadataValue);
        return result;
    }

    private async Task<Payload> DecodeOneAsync(Payload encoded)
    {
        if (!encoded.Metadata.TryGetValue(EncodingMetadataKey, out ByteString? encodingMarker)
            || encodingMarker.ToStringUtf8() != EncodingMetadataValue)
        {
            // Not encoded by this codec — pass through unchanged (the standard codec-chain convention).
            return encoded;
        }

        if (!EncryptedPayload.TryParse(encoded.Data.Span, out EncryptedPayload? encryptedPayload))
        {
            throw new InvalidOperationException(
                WorkflowErrors.PayloadCodecFailure("encoded payload is not a well-formed encrypted payload").Message);
        }

        Result<byte[]> decryptResult = await _encryptionService
            .DecryptAsync(encryptedPayload, _associatedData, CancellationToken.None)
            .ConfigureAwait(false);
        if (decryptResult.IsFailure)
        {
            throw new InvalidOperationException(
                WorkflowErrors.PayloadCodecFailure(decryptResult.Error.Message).Message);
        }

        return Payload.Parser.ParseFrom(decryptResult.Value);
    }
}
