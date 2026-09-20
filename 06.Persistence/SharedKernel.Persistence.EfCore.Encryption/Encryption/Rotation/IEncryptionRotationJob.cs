namespace SharedKernel.Persistence.EfCore.Encryption.Rotation;

/// <summary>Abstraction for triggering field-level AES-256-GCM encryption key rotation.</summary>
/// <remarks>
/// <para>Registered as scoped only when <c>EfCorePersistenceBuilder.WithEncryption()</c> is called.</para>
/// <para>Trigger via a Hangfire job, Temporal activity, hosted service, or management endpoint.</para>
/// <para>
/// <strong>Restriction (SK0303):</strong> must NOT be injected in MediatR handlers, domain services, application
/// command/query handlers, or any type in <c>03.Domain</c> or <c>05.Application</c>. Key rotation is an
/// infrastructure operation only.
/// </para>
/// </remarks>
public interface IEncryptionRotationJob
{
    /// <summary>
    /// Re-encrypts every row not already on <paramref name="expectedCurrentKeyId"/>, across every
    /// <c>.Encrypt(...)</c>-annotated property in the model, using keyset paging over each table's primary key —
    /// bypassing every EF query filter (soft-delete, tenant), value converter, and platform interceptor.
    /// </summary>
    /// <param name="expectedCurrentKeyId">
    /// The key id the registered <c>IEncryptionKeyProvider</c>'s current key is expected to already have — checked
    /// upfront, and the call throws <see cref="InvalidOperationException"/> without rotating anything if it does
    /// not match. Rotation always targets whichever key is CURRENT at the provider; this parameter exists so an
    /// operator cannot rotate rows onto the wrong key because they forgot to flip which key is current first.
    /// </param>
    /// <param name="checkpointToken">
    /// A token from a previous call's <see cref="EncryptionRotationReport.CheckpointToken"/>, to resume from
    /// exactly where that call left off. <see langword="null"/> to start from the beginning of the model.
    /// </param>
    /// <param name="batchSize">Rows read per page per entity type. Must be positive. Defaults to 500.</param>
    /// <param name="cancellationToken">
    /// Checked between batches. A cancelled call returns the rows processed so far with
    /// <see cref="EncryptionRotationReport.Completed"/> <see langword="false"/> and a resumable
    /// <see cref="EncryptionRotationReport.CheckpointToken"/>, rather than throwing
    /// <see cref="OperationCanceledException"/> and losing that progress.
    /// </param>
    /// <returns>A report of what happened.</returns>
    Task<EncryptionRotationReport> RotateAsync(
        string expectedCurrentKeyId,
        string? checkpointToken = null,
        int batchSize = 500,
        CancellationToken cancellationToken = default);
}
