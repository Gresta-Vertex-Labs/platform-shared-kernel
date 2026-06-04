namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Abstraction for triggering field-level AES-256-GCM encryption key rotation.
/// </summary>
/// <remarks>
/// <para>
/// Registered as scoped only when <c>EfCorePersistenceBuilder.WithEncryption()</c> is called.
/// </para>
/// <para>
/// Trigger via a Hangfire job, Temporal activity, hosted service, or management endpoint.
/// </para>
/// <para>
/// <strong>Restriction (SK0303):</strong> Must NOT be injected in MediatR handlers, domain
/// services, application command/query handlers, or any type in <c>03.Domain</c> or
/// <c>05.Application</c>. Key rotation is an infrastructure operation only.
/// </para>
/// </remarks>
public interface IEncryptionRotationJob
{
    /// <summary>
    /// Rotates all rows that were encrypted with <paramref name="fromVersion"/> to
    /// <paramref name="toVersion"/>, using batched reads and writes.
    /// </summary>
    /// <param name="fromVersion">The source key version to rotate away from, e.g. <c>"v1"</c>.</param>
    /// <param name="toVersion">The target key version to rotate to, e.g. <c>"v2"</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="EncryptionRotationResult"/> summarising the number of rows processed, rotated,
    /// and failed, with any per-row error messages.
    /// </returns>
    Task<EncryptionRotationResult> RotateAsync(
        string fromVersion,
        string toVersion,
        CancellationToken ct = default);
}
