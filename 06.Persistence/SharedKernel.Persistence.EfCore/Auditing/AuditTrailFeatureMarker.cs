namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Marker service whose mere presence in DI signals that
/// <c>EfCorePersistenceBuilder{TContext}.WithAuditTrail()</c> was called.
/// </summary>
/// <remarks>
/// <para>
/// WO-071/P-457. <see cref="SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext"/> accepts
/// an optional <see cref="AuditTrailFeatureMarker"/> constructor parameter, resolved automatically
/// by DI (present only when registered — a singleton, stateless class), to decide whether
/// <c>OnModelCreating</c> applies <see cref="AuditRecordEntityConfiguration"/> to the model. A plain
/// <see langword="bool"/> flag could not serve this purpose: DI cannot resolve a raw primitive
/// parameter automatically, so a marker TYPE — resolvable the same way
/// <c>ISymmetricEncryptionService?</c>/<c>IEncryptionKeyProvider?</c> already are for
/// <c>.WithEncryption()</c> — is the mechanism this codebase already uses for "was this optional
/// feature turned on" signals that must reach a DbContext's own constructor.
/// </para>
/// <para>
/// A downstream context that wants the audit trail must declare this parameter in its own
/// constructor and forward it to <c>base(...)</c> — exactly like the encryption services already
/// require.
/// </para>
/// </remarks>
public sealed class AuditTrailFeatureMarker;
