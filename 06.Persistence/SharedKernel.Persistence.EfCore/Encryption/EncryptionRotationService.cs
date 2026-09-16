using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Abstract base class that implements <see cref="IEncryptionRotationJob"/> for a specific
/// <typeparamref name="TContext"/>.
/// </summary>
/// <typeparam name="TContext">The concrete <see cref="DbContext"/> subclass to operate on.</typeparam>
/// <remarks>
/// <para>
/// Uses <see cref="IDbContextFactory{TContext}"/> to open a fresh <see cref="DbContext"/> per batch,
/// avoiding long-lived context lifetimes during potentially long-running rotation operations.
/// </para>
/// <para>
/// <strong>Algorithm:</strong>
/// <list type="number">
///   <item>Enumerate all entity types in the EF model that have at least one
///   <c>"SharedKernel:Encrypt"</c>-annotated property.</item>
///   <item>For each such entity type, load rows in batches of <see cref="BatchSize"/>.</item>
///   <item>For each row, mark every encrypted property as modified (the converter decrypts the
///   existing value transparently on read, using whatever key id the stored payload records).</item>
///   <item>Save the batch with <see cref="IEncryptionVersionOverride"/> directing the converter to
///   re-encrypt with <paramref name="toVersion"/>, then update counters.</item>
/// </list>
/// </para>
/// <para>
/// <strong>Idempotent:</strong> Safe to run repeatedly. Re-running re-encrypts already-rotated
/// rows again with <paramref name="toVersion"/> (a no-op data change), and never mutates
/// <see cref="EncryptionOptions.CurrentVersion"/>.
/// </para>
/// <para>
/// Registered as scoped via <c>EfCorePersistenceBuilder.Build()</c> only when
/// <c>.WithEncryption()</c> was called.
/// </para>
/// </remarks>
public abstract class EncryptionRotationService<TContext> : IEncryptionRotationJob
    where TContext : SharedKernelDbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly IOptionsMonitor<EncryptionOptions> _optionsMonitor;
    private readonly EncryptedEntityBatchProcessorRegistry<TContext> _registry;
    private readonly ILogger<EncryptionRotationService<TContext>> _logger;

    /// <summary>
    /// Number of rows loaded and processed per batch. Default is <c>500</c>.
    /// Override to tune for memory vs. throughput trade-offs.
    /// </summary>
    protected virtual int BatchSize => 500;

    /// <summary>
    /// Initialises a new <see cref="EncryptionRotationService{TContext}"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for creating fresh DbContext instances per batch.</param>
    /// <param name="optionsMonitor">Live encryption options for key lookup at call time.</param>
    /// <param name="registry">
    /// Registry of <see cref="IEncryptedEntityBatchProcessor"/> instances, one per encrypted
    /// entity CLR type, used to load batches without reflection.
    /// </param>
    /// <param name="logger">
    /// Optional logger for the <c>EncryptionRotationBatchProcessed</c>/<c>EncryptionRotationCompleted</c>
    /// Information logs (EventIds <c>6009</c>-<c>6010</c>, WO-053/P-333). Resolved by DI when
    /// registered; falls back to <see cref="NullLogger{T}"/> otherwise. Never logs a key byte, a
    /// Base64-encoded key string, or any column plaintext/ciphertext value — only counts and
    /// already-non-secret version-tag strings.
    /// </param>
    protected EncryptionRotationService(
        IDbContextFactory<TContext> contextFactory,
        IOptionsMonitor<EncryptionOptions> optionsMonitor,
        EncryptedEntityBatchProcessorRegistry<TContext> registry,
        ILogger<EncryptionRotationService<TContext>>? logger = null)
    {
        _contextFactory = contextFactory;
        _optionsMonitor = optionsMonitor;
        _registry = registry;
        _logger = logger ?? NullLogger<EncryptionRotationService<TContext>>.Instance;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <strong>Per-row detection limitation:</strong> <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry.CurrentValue"/>
    /// always reflects the <em>decrypted</em> (CLR-side) value — the key id recorded in the stored
    /// payload is not observable through any public EF Core API once the
    /// entity has been materialized. Consequently every row in every batch of every encrypted
    /// entity type is unconditionally marked as modified and re-encrypted with
    /// <paramref name="toVersion"/>. <paramref name="fromVersion"/> is retained for API
    /// stability and audit/logging purposes but does not filter which rows are rewritten.
    /// </para>
    /// <para>
    /// This is safe to run repeatedly: <see cref="EncryptedValueConverter"/> decrypts using the
    /// key id recorded in the existing payload (looked up in
    /// <c>EncryptionOptions.Keys</c>) regardless of <see cref="EncryptionOptions.CurrentVersion"/>,
    /// so plaintext round-trips correctly even when a row is rotated multiple times in a row.
    /// </para>
    /// </remarks>
    public async Task<EncryptionRotationResult> RotateAsync(
        string fromVersion,
        string toVersion,
        CancellationToken ct = default)
    {
        var totalProcessed = 0;
        var totalRotated = 0;
        var totalFailed = 0;
        var errors = new List<string>();

        // Discover all entity types with at least one encrypted property.
        await using var discoveryContext = await _contextFactory.CreateDbContextAsync(ct);

        var encryptedEntityInfos = DiscoverEncryptedEntityTypes(discoveryContext);

        foreach (var entityInfo in encryptedEntityInfos)
        {
            ct.ThrowIfCancellationRequested();

            int skip = 0;
            int batchNumber = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                await using var batchContext = await _contextFactory.CreateDbContextAsync(ct);

                // Load the next batch of rows as raw untyped objects via the reflection-free
                // registry of closed-generic IEncryptedEntityBatchProcessor instances.
                var batch = await LoadBatchAsync(batchContext, entityInfo.ClrType, skip, BatchSize, ct);

                if (batch.Count == 0)
                {
                    break;
                }

                var batchRotated = 0;
                var batchFailed = 0;

                // Disable automatic change detection for this batch: SaveChangesAsync's implicit
                // DetectChanges() call would otherwise revert IsModified back to false for
                // encrypted properties whose CLR (decrypted) value has not changed, even though
                // we need EF to re-run the ValueConverter and rewrite the ciphertext.
                var previousAutoDetect = batchContext.ChangeTracker.AutoDetectChangesEnabled;
                batchContext.ChangeTracker.AutoDetectChangesEnabled = false;

                try
                {
                    foreach (var entity in batch)
                    {
                        totalProcessed++;

                        try
                        {
                            var entry = batchContext.Entry(entity);

                            // CurrentValue reflects the decrypted (CLR-side) value — the stored
                            // payload's key id is not observable post-materialization.
                            // Unconditionally mark every encrypted property as modified so the
                            // ValueConverter re-encrypts with toVersion (via IEncryptionVersionOverride)
                            // on save, regardless of which version it was previously stored with.
                            foreach (var propName in entityInfo.EncryptedPropertyNames)
                            {
                                entry.Property(propName).IsModified = true;
                            }

                            batchRotated++;
                        }
                        catch (Exception ex)
                        {
                            batchFailed++;
                            totalFailed++;
                            errors.Add($"Failed to process row of type '{entityInfo.ClrType.Name}': {ex.Message}");
                        }
                    }

                    if (batchRotated > 0)
                    {
                        // Direct this batch's EncryptedValueConverter instances to encrypt with
                        // toVersion for the duration of SaveChangesAsync, without mutating
                        // EncryptionOptions.CurrentVersion. Reset afterward regardless of outcome.
                        // IEncryptionVersionOverride is a singleton (AsyncLocal-backed) — the same
                        // instance EncryptionModelConvention captured into every
                        // EncryptedValueConverter, so mutating it here affects this batch's save.
                        var versionOverride = batchContext.CurrentEncryptionVersionOverride;

                        try
                        {
                            versionOverride.OverrideVersion = toVersion;
                            await batchContext.SaveChangesAsync(ct);
                            totalRotated += batchRotated;
                        }
                        catch (Exception ex)
                        {
                            totalFailed += batchRotated;
                            errors.Add(
                                $"Batch save failed for type '{entityInfo.ClrType.Name}' " +
                                $"(batch {batchNumber}): {ex.Message}");
                        }
                        finally
                        {
                            versionOverride.OverrideVersion = null;
                        }
                    }
                }
                finally
                {
                    batchContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetect;
                }

                PersistenceLog.EncryptionRotationBatchProcessed(_logger, batchNumber, batch.Count, fromVersion, toVersion);
                OnBatchCompleted(batchNumber, batch.Count);
                batchNumber++;
                skip += batch.Count;

                if (batch.Count < BatchSize)
                {
                    // Last batch — no more rows.
                    break;
                }
            }
        }

        PersistenceLog.EncryptionRotationCompleted(_logger, fromVersion, toVersion, totalProcessed, totalRotated, totalFailed);

        return new EncryptionRotationResult(totalProcessed, totalRotated, totalFailed, errors);
    }

    /// <summary>
    /// Called after each batch completes. Override to add logging or progress reporting.
    /// Default implementation is a no-op.
    /// </summary>
    /// <param name="batchNumber">Zero-based batch index.</param>
    /// <param name="batchSize">Number of rows in this batch (may be less than <see cref="BatchSize"/> for the last batch).</param>
    protected virtual void OnBatchCompleted(int batchNumber, int batchSize) { }

    // Loads a page of entities of the given CLR type from the context via the reflection-free
    // EncryptedEntityBatchProcessorRegistry — one closed-generic processor per encrypted entity
    // type, registered at startup. Returns untyped objects so callers can call context.Entry(entity).
    private async Task<List<object>> LoadBatchAsync(
        DbContext context,
        Type clrType,
        int skip,
        int take,
        CancellationToken ct)
    {
        if (!_registry.TryGet(clrType, out var processor) || processor is null)
        {
            return [];
        }

        return await processor.LoadBatchAsync(context, skip, take, ct);
    }

    // Discovers entity types in the model that have at least one encrypted property.
    private static List<EncryptedEntityInfo> DiscoverEncryptedEntityTypes(DbContext context)
    {
        var result = new List<EncryptedEntityInfo>();
        const string annotationKey = "SharedKernel:Encrypt";

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            // Skip owned types — they are managed through their owner.
            if (entityType.IsOwned())
            {
                continue;
            }

            var encryptedProps = entityType.GetProperties()
                .Where(p => p.FindAnnotation(annotationKey)?.Value is true && p.ClrType == typeof(string))
                .Select(p => p.Name)
                .ToList();

            if (encryptedProps.Count > 0)
            {
                result.Add(new EncryptedEntityInfo(entityType.ClrType!, encryptedProps));
            }
        }

        return result;
    }

    private sealed record EncryptedEntityInfo(Type ClrType, List<string> EncryptedPropertyNames);
}
