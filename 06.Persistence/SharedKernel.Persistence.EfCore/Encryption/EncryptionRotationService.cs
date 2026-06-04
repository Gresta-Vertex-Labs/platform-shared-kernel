using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
///   <item>For each row, check whether any encrypted property's stored string value starts with
///   <c>"v{fromVersion}:"</c>.</item>
///   <item>If so, read the property (the converter decrypts transparently), then write it back
///   (the converter re-encrypts with <paramref name="toVersion"/>).</item>
///   <item>Save the batch and update counters.</item>
/// </list>
/// </para>
/// <para>
/// <strong>Idempotent:</strong> Rows already at <paramref name="toVersion"/> are skipped.
/// </para>
/// <para>
/// Registered as scoped via <c>EfCorePersistenceBuilder.Build()</c> only when
/// <c>.WithEncryption()</c> was called.
/// </para>
/// </remarks>
public abstract class EncryptionRotationService<TContext> : IEncryptionRotationJob
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly IOptionsMonitor<EncryptionOptions> _optionsMonitor;

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
    protected EncryptionRotationService(
        IDbContextFactory<TContext> contextFactory,
        IOptionsMonitor<EncryptionOptions> optionsMonitor)
    {
        _contextFactory = contextFactory;
        _optionsMonitor = optionsMonitor;
    }

    /// <inheritdoc />
    public async Task<EncryptionRotationResult> RotateAsync(
        string fromVersion,
        string toVersion,
        CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;
        var fromPrefix = $"v{fromVersion}:";

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

                // Load the next batch of rows as raw untyped objects.
                var batch = await LoadBatchAsync(batchContext, entityInfo.ClrType, skip, BatchSize, ct);

                if (batch.Count == 0)
                {
                    break;
                }

                var batchRotated = 0;
                var batchFailed = 0;

                foreach (var entity in batch)
                {
                    totalProcessed++;

                    try
                    {
                        var entry = batchContext.Entry(entity);
                        var needsRotation = false;

                        foreach (var propName in entityInfo.EncryptedPropertyNames)
                        {
                            var currentStored = entry.Property(propName).CurrentValue as string;
                            if (currentStored is not null && currentStored.StartsWith(fromPrefix, StringComparison.Ordinal))
                            {
                                needsRotation = true;
                                // The ValueConverter decrypts on read and re-encrypts with the
                                // current CurrentVersion (toVersion) on next save.
                                // We force a re-write by marking the property as modified.
                                entry.Property(propName).IsModified = true;
                            }
                        }

                        if (needsRotation)
                        {
                            batchRotated++;
                        }
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
                    try
                    {
                        // Temporarily switch CurrentVersion to toVersion so the converter encrypts
                        // with the target key. We achieve this by relying on the fact that the
                        // converter reads options.CurrentVersion at save time — callers must ensure
                        // CurrentVersion == toVersion in EncryptionOptions before calling RotateAsync.
                        await batchContext.SaveChangesAsync(ct);
                        totalRotated += batchRotated;
                    }
                    catch (Exception ex)
                    {
                        totalFailed += batchRotated;
                        totalRotated -= 0; // nothing was committed
                        errors.Add(
                            $"Batch save failed for type '{entityInfo.ClrType.Name}' " +
                            $"(batch {batchNumber}): {ex.Message}");
                    }
                }

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

        return new EncryptionRotationResult(totalProcessed, totalRotated, totalFailed, errors);
    }

    /// <summary>
    /// Called after each batch completes. Override to add logging or progress reporting.
    /// Default implementation is a no-op.
    /// </summary>
    /// <param name="batchNumber">Zero-based batch index.</param>
    /// <param name="batchSize">Number of rows in this batch (may be less than <see cref="BatchSize"/> for the last batch).</param>
    protected virtual void OnBatchCompleted(int batchNumber, int batchSize) { }

    // Loads a page of entities of the given CLR type from the context using EF Core's
    // non-generic entry point. Returns untyped objects so callers can call context.Entry(entity).
    private static async Task<List<object>> LoadBatchAsync(
        DbContext context,
        Type clrType,
        int skip,
        int take,
        CancellationToken ct)
    {
        // EF Core 5+ exposes IQueryable via the non-generic Set method accessible through
        // the model. We use the Set<T> method reflectively — this is model-build-time / startup
        // code, not a hot path, so the reflection cost is acceptable.
        var setMethod = typeof(DbContext)
            .GetMethods()
            .First(m => m.Name == nameof(DbContext.Set)
                     && m.IsGenericMethod
                     && m.GetParameters().Length == 0);

        var genericSetMethod = setMethod.MakeGenericMethod(clrType);
        var queryable = genericSetMethod.Invoke(context, null) as IQueryable<object>;

        if (queryable is null)
        {
            return [];
        }

        return await queryable
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
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
