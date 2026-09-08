using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Persistence.EfCore.Encryption;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// A fifth EF Core interceptor, registered ONLY by
/// <c>EfCorePersistenceBuilder&lt;TContext&gt;.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c>,
/// that warms <see cref="PreWarmedEncryptionKeyProvider"/>'s current key before either a write or a
/// read that could reach an encrypted property (D-130/P-498/WO-081).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two hooks, not one — this is the fix for the phase's refuted original premise (D-126):</strong>
/// </para>
/// <para>
/// <see cref="SavingChangesAsync"/> (<see cref="ISaveChangesInterceptor"/>) warms before a WRITE —
/// if <c>ChangeTracker.Entries()</c> contains any <see cref="EntityState.Added"/>/
/// <see cref="EntityState.Modified"/> entry whose entity type carries the
/// <c>"SharedKernel:Encrypt"</c> annotation on at least one property, this awaits
/// <see cref="PreWarmedEncryptionKeyProvider.WarmCurrentAsync"/> BEFORE calling the base
/// implementation — strictly before EF Core builds the command batches that invoke
/// <c>EncryptedValueConverter</c>'s synchronous <c>ConvertToProviderExpression</c>.
/// </para>
/// <para>
/// <see cref="ReaderExecutingAsync"/> (<see cref="IDbCommandInterceptor"/>) warms before a READ —
/// EF Core's genuine ASYNC pre-materialization extension point, firing before
/// <c>ExecuteReaderAsync</c> returns a <see cref="DbDataReader"/>, strictly before any row's
/// <c>ConvertFromProviderExpression</c>. This is the half that actually closes the motivating F1
/// defect's literal scenario ("every READ of an encrypted column becomes a blocking call") — a
/// write-only pre-warm hook, as the phase's original brief proposed, does nothing whatsoever for a
/// query, since <c>SaveChangesAsync</c> is never called on the read path. Coarse-grained by design:
/// warms whenever the DbContext's MODEL has any encrypted property, regardless of whether the
/// specific query about to run touches one — a warm no-op call is an O(1) dictionary check inside
/// <see cref="PreWarmedEncryptionKeyProvider"/>, not a KMS round trip, so this is cheap even when
/// unnecessary.
/// </para>
/// <para>
/// Both "does this model/entity type have an encrypted property" checks are cached — the model-wide
/// check per <see cref="DbContext"/> CLR type, the entity-type check per <see cref="IEntityType"/> —
/// so the annotation scan runs at most once per distinct model, not once per save/query.
/// </para>
/// </remarks>
internal sealed class EncryptionKeyPreWarmingInterceptor : SaveChangesInterceptor, IDbCommandInterceptor
{
    private static readonly ConcurrentDictionary<Type, bool> ModelHasEncryptedPropertyCache = new();
    private static readonly ConcurrentDictionary<IEntityType, bool> EntityTypeHasEncryptedPropertyCache = new();

    private readonly PreWarmedEncryptionKeyProvider _provider;

    /// <summary>Initialises a new <see cref="EncryptionKeyPreWarmingInterceptor"/>.</summary>
    /// <param name="provider">The provider to warm before a write/read that could reach an encrypted property.</param>
    public EncryptionKeyPreWarmingInterceptor(PreWarmedEncryptionKeyProvider provider)
    {
        _provider = provider;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && HasEncryptedChanges(context))
        {
            await _provider.WarmCurrentAsync(cancellationToken).ConfigureAwait(false);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && ModelHasEncryptedProperty(context))
        {
            return WarmThenReadAsync(_provider, result, cancellationToken);
        }

        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    private static async ValueTask<InterceptionResult<DbDataReader>> WarmThenReadAsync(
        PreWarmedEncryptionKeyProvider provider,
        InterceptionResult<DbDataReader> result,
        CancellationToken ct)
    {
        await provider.WarmCurrentAsync(ct).ConfigureAwait(false);
        return result;
    }

    // Whole-model, per-DbContext-CLR-type gate — cached, computed at most once per distinct
    // context type across the process lifetime.
    private static bool ModelHasEncryptedProperty(DbContext context) =>
        ModelHasEncryptedPropertyCache.GetOrAdd(
            context.GetType(),
            _ => context.Model.GetEntityTypes().Any(EntityTypeHasEncryptedProperty));

    // Per-entity-type check reused by both hooks — the SAME "SharedKernel:Encrypt" annotation scan
    // EncryptedEntityBatchProcessorRegistry/EncryptionRotationService already perform, cached here
    // independently since this interceptor never references either.
    private static bool HasEncryptedChanges(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (EntityTypeHasEncryptedProperty(entry.Metadata))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EntityTypeHasEncryptedProperty(IEntityType entityType) =>
        EntityTypeHasEncryptedPropertyCache.GetOrAdd(
            entityType,
            static et => et.GetProperties().Any(
                p => p.FindAnnotation(PropertyBuilderEncryptExtensions.AnnotationKey)?.Value is true));
}
