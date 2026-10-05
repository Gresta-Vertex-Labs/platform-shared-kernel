using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Encryption.Crypto;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;
using SharedKernel.Persistence.EfCore.Encryption.Metadata;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

namespace SharedKernel.Persistence.EfCore.Encryption.Interception;

/// <summary>
/// Encrypts every changed encrypted property immediately before it is saved, restores the plaintext immediately
/// after, and decrypts on materialization.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why not a value converter:</strong> the associated data binds the row's primary key and tenant, which a
/// converter never sees. This interceptor has the whole entry on save and the whole instance on materialization.
/// </para>
/// <para>
/// <strong>Save:</strong> for each added entry every encrypted property is encrypted; for each modified entry only
/// the encrypted properties that actually changed are, so an unrelated change never re-encrypts (or rewrites) a
/// value. The tracked current value is replaced by its ciphertext for the physical save and restored afterwards.
/// When the save was accepted, the restored plaintext also becomes the original value and the property is marked
/// unchanged, so the entry ends up <see cref="EntityState.Unchanged"/> and a second save writes nothing. When the
/// save failed, the entry keeps its state and plaintext, ready to be retried. A save that stopped between the two
/// steps (a later interceptor threw) is detected and restored at the start of the next save, so a value is never
/// encrypted twice.
/// </para>
/// <para>
/// <strong>Ordering:</strong> registered through an options extension, which the core package applies after its
/// own interceptors and the service's own, so encryption sees every value they set.
/// </para>
/// <para>
/// <strong>Identity:</strong> one singleton instance serves every context, because EF Core caches its internal
/// service provider by the identity of materialization interceptors. Per-context state lives in a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/>.
/// </para>
/// </remarks>
internal sealed class EncryptionInterceptor(FieldEncryptionRuntime runtime, ILogger<EncryptionInterceptor> logger)
    : ISaveChangesInterceptor, IMaterializationInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<PendingRestore>> _pending = new();

    public FieldEncryptionRuntime Runtime => runtime;

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            PrepareTenantKeys(context);
            EncryptPending(context);
        }

        return result;
    }

    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            await PrepareTenantKeysAsync(context, cancellationToken).ConfigureAwait(false);
            EncryptPending(context);
        }

        return result;
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context)
            RestorePending(context);

        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            RestorePending(context);

        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context)
            RestorePending(context);
    }

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            RestorePending(context);

        return Task.CompletedTask;
    }

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        var members = EncryptionModelMetadata.For(materializationData.Context.Model).GetMembers(materializationData.EntityType);
        if (members.Count == 0)
            return entity;

        var tenantId = entity is IHasTenant tenanted ? tenanted.TenantId.Value : (Guid?)null;
        byte[]? primaryKey = null;

        foreach (var member in members)
        {
            if (member.GetDeclaringInstance(entity) is not { } instance || member.GetValue(instance) is not { } stored)
                continue;

            primaryKey ??= PrimaryKeyCanonicalizer.Canonicalize(materializationData.EntityType, entity);
            var associatedData = AssociatedDataBuilder.Build(member.Purpose, primaryKey, tenantId);
            member.SetValue(instance, runtime.Decrypt(member, stored, associatedData, tenantId, materializationData.Context));
        }

        return entity;
    }

    // Loads (creating when needed) the data keys of every tenant this save writes for, asynchronously, so the
    // synchronous encryption below never blocks on the key store; then re-checks, in the save's transaction, that none
    // of those tenants was shredded since (another process's shred is not in this process's key cache).
    private async Task PrepareTenantKeysAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (TenantsWrittenWithTenantKeys(context) is not { } tenants)
            return;

        var side = runtime.SideConnection(context);
        foreach (var tenant in tenants)
        {
            var entry = await runtime.TenantKeys.GetAsync(RequireTenant(tenant), side, create: true, cancellationToken).ConfigureAwait(false);
            if (entry is { IsShredded: true })
                throw new TenantKeyShreddedException();
        }

        await runtime.EnsureNotShreddedAsync(context, tenants, cancellationToken).ConfigureAwait(false);
    }

    // The synchronous form of PrepareTenantKeysAsync.
    private void PrepareTenantKeys(DbContext context)
    {
        if (TenantsWrittenWithTenantKeys(context) is not { } tenants)
            return;

        var side = runtime.SideConnection(context);
        foreach (var tenant in tenants)
        {
            if (runtime.TenantKeys.GetBlocking(RequireTenant(tenant), side, create: true) is { IsShredded: true })
                throw new TenantKeyShreddedException();
        }

        runtime.EnsureNotShredded(context, tenants);
    }

    // The tenants of the added or modified tenanted entries with encrypted members, or null when there are none.
    private HashSet<Guid>? TenantsWrittenWithTenantKeys(DbContext context)
    {
        if (!runtime.Settings.TenantDataKeys)
            return null;

        var metadata = EncryptionModelMetadata.For(context.Model);
        if (!metadata.HasEncryptedMembers)
            return null;

        HashSet<Guid>? tenants = null;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified
                && entry.Entity is IHasTenant tenanted
                && metadata.GetMembers(entry.Metadata).Count > 0)
            {
                (tenants ??= []).Add(tenanted.TenantId.Value);
            }
        }

        return tenants;
    }

    private void EncryptPending(DbContext context)
    {
        if (_pending.TryGetValue(context, out _))
        {
            RestorePending(context);
            EncryptionLog.StalePendingRestored(logger, context.GetType().Name);
        }

        var metadata = EncryptionModelMetadata.For(context.Model);
        if (!metadata.HasEncryptedMembers)
            return;

        List<PendingRestore>? pending = null;
        try
        {
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified))
                    continue;

                var members = metadata.GetMembers(entry.Metadata);
                if (members.Count > 0)
                    EncryptEntry(context, entry, members, ref pending);
            }
        }
        catch
        {
            if (pending is not null)
                Restore(pending);
            throw;
        }

        if (pending is not null)
            _pending.AddOrUpdate(context, pending);
    }

    private void EncryptEntry(DbContext context, EntityEntry entry, IReadOnlyList<EncryptedMember> members, ref List<PendingRestore>? pending)
    {
        var tenantId = entry.Entity is IHasTenant tenanted ? tenanted.TenantId.Value : (Guid?)null;
        byte[]? primaryKey = null;

        foreach (var member in members)
        {
            if (member.GetPropertyEntry(entry) is not { } propertyEntry)
                continue;

            if (entry.State == EntityState.Modified && !propertyEntry.IsModified)
                continue;

            var plaintext = propertyEntry.CurrentValue as string;
            if (plaintext is null)
            {
                if (member.BlindIndexProperty is { } nullIndex)
                    entry.Property(nullIndex.Name).CurrentValue = null;
                continue;
            }

            if (primaryKey is null)
            {
                EnsureClientGeneratedKey(entry);
                primaryKey = PrimaryKeyCanonicalizer.Canonicalize(entry.Metadata, entry.Entity);
            }

            var associatedData = AssociatedDataBuilder.Build(member.Purpose, primaryKey, tenantId);
            string ciphertext;
            try
            {
                if (runtime.UsesTenantKey(member))
                {
                    var tenantKey = runtime.TenantKeys.GetBlocking(RequireTenant(tenantId!.Value), runtime.SideConnection(context), create: true)!;
                    ciphertext = FieldCipher.EncryptWithTenantKey(tenantKey, member.Purpose, associatedData, plaintext);
                }
                else
                {
                    ciphertext = runtime.Cipher.EncryptWithRootKey(runtime.KeyRing.GetCurrentKey(), member.Purpose, associatedData, plaintext);
                }
            }
            catch (Exception exception) when (exception is not TenantKeyShreddedException)
            {
                EncryptionMeter.RecordEncryptFailure("encrypt_failed");
                throw;
            }

            (pending ??= []).Add(new PendingRestore(entry, member, plaintext));
            propertyEntry.CurrentValue = ciphertext;

            if (member.BlindIndexProperty is { } blindIndex)
                entry.Property(blindIndex.Name).CurrentValue = runtime.BlindIndexer.Compute(member, plaintext, tenantId);
        }
    }

    private void RestorePending(DbContext context)
    {
        if (!_pending.TryGetValue(context, out var pending))
            return;

        _pending.Remove(context);
        Restore(pending);
    }

    private static void Restore(List<PendingRestore> pending)
    {
        // Read every entry's state before touching any value: restoring the first property of an accepted entry
        // would otherwise flip it to Modified and hide that it was accepted.
        var accepted = pending.Select(p => p.Entry.State == EntityState.Unchanged).ToArray();

        for (var i = 0; i < pending.Count; i++)
        {
            var (entry, member, plaintext) = pending[i];
            if (entry.State == EntityState.Detached)
            {
                if (member.GetDeclaringInstance(entry.Entity) is { } instance)
                    member.SetValue(instance, plaintext);
                continue;
            }

            if (member.GetPropertyEntry(entry) is not { } propertyEntry)
                continue;

            propertyEntry.CurrentValue = plaintext;
            if (accepted[i])
            {
                propertyEntry.OriginalValue = plaintext;
                propertyEntry.IsModified = false;
            }
        }
    }

    private static Guid RequireTenant(Guid tenantId) =>
        tenantId != Guid.Empty
            ? tenantId
            : throw new InvalidOperationException("An entity encrypted with tenant data keys has an empty TenantId.");

    // The primary key is bound into the associated data before the save that would assign a store-generated key.
    private static void EnsureClientGeneratedKey(EntityEntry entry)
    {
        foreach (var keyProperty in entry.Metadata.FindPrimaryKey()!.Properties)
        {
            if (entry.Property(keyProperty.Name).IsTemporary)
            {
                throw new InvalidOperationException(
                    $"'{entry.Metadata.ShortName()}' has an encrypted property but its primary key '{keyProperty.Name}' is " +
                    "store-generated and not yet assigned. Field encryption binds the primary key into every value, so " +
                    "assign it on the client (for example a UUID v7 or a strongly-typed id).");
            }
        }
    }

    private readonly record struct PendingRestore(EntityEntry Entry, EncryptedMember Member, string? Plaintext);
}
