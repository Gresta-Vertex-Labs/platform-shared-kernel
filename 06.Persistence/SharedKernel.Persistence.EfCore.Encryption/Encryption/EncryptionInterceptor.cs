using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.KeyDerivation;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Encrypts every <c>.Encrypt(...)</c>-annotated property before it is physically saved, and decrypts it on
/// materialization.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why not a <c>ValueConverter</c>:</strong> a <c>ValueConverter</c> sees only the one property being
/// converted — it has no access to the row's primary key or tenant id, both of which the associated-data (AAD)
/// binding requires (see <see cref="AssociatedDataBuilder"/>). This interceptor has full entity-graph access at
/// both ends: <see cref="ISaveChangesInterceptor.SavingChangesAsync"/> sees the whole tracked entry before the
/// physical save, and <see cref="IMaterializationInterceptor.InitializedInstance"/> sees the whole materialized
/// instance, with every property (including the primary key) already assigned.
/// </para>
/// <para>
/// <strong>Encrypt path (fully synchronous, deliberately):</strong> EF Core's row materializer has no asynchronous
/// extension point at all — <c>IMaterializationInterceptor</c> is a synchronous interface, so decryption on read
/// must be synchronous regardless of anything this interceptor does on write. Rather than give encryption and
/// decryption two different key-resolution paths (an async one for save, a sync one for read), both directions use
/// the SAME <see cref="ISynchronousSymmetricEncryptionService"/> — AES-GCM computation is CPU-bound, so calling it
/// from inside an async <c>SavingChangesAsync</c> override costs nothing and is not "sync-over-async" (which means
/// blocking a thread on a <see cref="Task"/>, not calling an ordinary synchronous method from async code). This is
/// option (b) from this package's design brief, chosen over an async-encrypt/sync-decrypt split specifically
/// because EF Core 10 offers no way to make the read side asynchronous, so unifying on one key-resolution story
/// avoids that split providing no benefit while doubling the key-provider surface this package depends on.
/// </para>
/// <para>
/// <strong>Encrypt-then-restore, not a shadow column:</strong> the encrypted CLR property IS the physically mapped
/// column (no value converter, no separate ciphertext-only shadow property) — <c>SavingChanges</c>/
/// <c>SavingChangesAsync</c> temporarily overwrites each annotated property's tracked <c>CurrentValue</c> with its
/// ciphertext immediately before the physical save, and <c>SavedChanges</c>/<c>SavedChangesAsync</c>/
/// <c>SaveChangesFailed</c>/<c>SaveChangesFailedAsync</c> restore the plaintext immediately after — so the tracked
/// entity graph the caller continues to use never observably holds ciphertext, whether the save succeeded or
/// failed. Pending restores are tracked per <see cref="DbContext"/> instance via a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/>, because this interceptor is registered as a single, stable,
/// process-lifetime singleton shared by every <see cref="DbContext"/> instance (required — EF Core keys its
/// internal service provider cache on the identity of registered materialization interceptors; a fresh instance
/// per context would rebuild that cache on every context construction). See
/// <see cref="EncryptionInterceptorOptionsContributor"/> for how it is wired in, bypassing
/// <c>EfCorePersistenceBuilder.AddInterceptor&lt;T&gt;()</c> (which registers Scoped, wrong for this constraint).
/// </para>
/// <para>
/// <strong>Store-generated keys:</strong> the primary key must already be assigned on the tracked entity BEFORE
/// <c>SavingChanges</c> runs, because it is one of the AAD inputs computed at that point — a database-generated
/// key (an identity/serial column) is not yet known then. Every <c>SharedKernel.Domain</c> aggregate/entity base
/// assigns its id client-side at construction (<c>StronglyTypedId</c>/<c>Guid.NewGuid()</c>/UUID v7), so this is
/// satisfied by convention across the platform; an entity with a store-generated key and an encrypted property is
/// unsupported and is not separately guarded here (a plain string/service-agnostic model-build check cannot
/// distinguish "this key is store-generated" cheaply for every provider) — document this constraint alongside the
/// entity if it is ever needed.
/// </para>
/// <para>
/// <strong>Projections silently bypass decryption:</strong> materialization only happens for a fully materialized
/// entity. A LINQ projection that selects the raw property (<c>.Select(x =&gt; x.Email)</c>) returns the stored
/// ciphertext untouched — this interceptor never runs for it. Always read an encrypted property through its owning
/// entity.
/// </para>
/// </remarks>
public sealed class EncryptionInterceptor : ISaveChangesInterceptor, IMaterializationInterceptor
{
    private readonly ISynchronousSymmetricEncryptionService _encryptionService;
    private readonly ISynchronousEncryptionKeyProvider _keyProvider;
    private readonly IBlindIndexService _blindIndexService;
    private readonly EncryptionOptions _options;
    private readonly ConditionalWeakTable<DbContext, List<PendingRestore>> _pending = new();

    /// <summary>Initialises a new <see cref="EncryptionInterceptor"/>.</summary>
    public EncryptionInterceptor(
        ISynchronousSymmetricEncryptionService encryptionService,
        ISynchronousEncryptionKeyProvider keyProvider,
        IBlindIndexService blindIndexService,
        IOptions<EncryptionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(encryptionService);
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(blindIndexService);
        ArgumentNullException.ThrowIfNull(options);
        _encryptionService = encryptionService;
        _keyProvider = keyProvider;
        _blindIndexService = blindIndexService;
        _options = options.Value;
    }

    // ComplexPropertyName is null for a direct entity property, or the owning complex property's name for a
    // property inside a complex type (see EncryptPending's complex-property loop and RestorePending's use of it).
    private readonly record struct PendingRestore(EntityEntry Entry, string? ComplexPropertyName, string PropertyName, string? Plaintext)
    {
        public PropertyEntry Resolve() =>
            ComplexPropertyName is null ? Entry.Property(PropertyName) : Entry.ComplexProperty(ComplexPropertyName).Property(PropertyName);
    }

    /// <inheritdoc />
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
            EncryptPending(context);

        return result;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            EncryptPending(context);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context)
            RestorePending(context);

        return result;
    }

    /// <inheritdoc />
    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            RestorePending(context);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context)
            RestorePending(context);
    }

    /// <inheritdoc />
    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            RestorePending(context);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        var entityType = materializationData.EntityType;
        var tenantId = entity is IHasTenant hasTenant ? hasTenant.TenantId : (Guid?)null;
        byte[]? primaryKey = null;
        byte[] GetPrimaryKey() => primaryKey ??= PrimaryKeyCanonicalizer.Canonicalize(entityType, entity);

        foreach (var property in entityType.GetProperties())
        {
            if (property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt)?.Value is not string purpose)
                continue;

            DecryptProperty(property, entity, purpose, entityType.ShortName(), property.Name, GetPrimaryKey, tenantId);
        }

        // EF Core 10 complex-type (value-object) properties are flattened onto the SAME table/row as the owning
        // entity, so they share its primary key/tenant for AAD purposes — only the target instance to decrypt
        // INTO differs (the complex object, not the entity itself).
        foreach (var complexProperty in entityType.GetComplexProperties())
        {
            var complexInstance = complexProperty.GetGetter().GetClrValue(entity);
            if (complexInstance is null)
                continue;

            foreach (var property in complexProperty.ComplexType.GetProperties())
            {
                if (property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt)?.Value is not string purpose)
                    continue;

                DecryptProperty(
                    property, complexInstance, purpose, entityType.ShortName(),
                    $"{complexProperty.Name}.{property.Name}", GetPrimaryKey, tenantId);
            }
        }

        return entity;
    }

    // Decrypts one property's stored ciphertext IN PLACE on targetInstance — the owning entity itself for a
    // direct property, or the complex-type instance for a property inside a complex property. AAD always binds
    // to the OWNING ROW's primary key/tenant (via getPrimaryKey/tenantId), never anything about targetInstance.
    private void DecryptProperty(
        IProperty property,
        object targetInstance,
        string purpose,
        string entityTypeShortName,
        string propertyPath,
        Func<byte[]> getPrimaryKey,
        Guid? tenantId)
    {
        var getter = property.GetGetter();
        if (getter.GetClrValue(targetInstance) is not string stored)
            return;

        var perTenantKey = property.FindAnnotation(PropertyBuilderEncryptExtensions.PerTenantKeyAnnotationKey)?.Value is true;
        // AAD binds the tenant id for every IHasTenant entity unconditionally — perTenantKey controls ONLY
        // which key material is used (see ResolveService), never whether tenant enters the AAD.
        var aad = AssociatedDataBuilder.Build(purpose, getPrimaryKey(), tenantId);
        var service = ResolveService(purpose, perTenantKey, tenantId);

        if (!EncryptedPayload.TryParse(stored, out var payload))
        {
            if (_options.AllowUnencryptedValues)
                return;

            EncryptionMeter.RecordDecryptFailure("malformed_payload");
            throw new CryptographicException(
                $"The stored value of encrypted property '{entityTypeShortName}.{propertyPath}' is not an " +
                "encrypted payload. It was written unencrypted, truncated or otherwise altered. To read " +
                "columns that still hold unencrypted data during a migration, set " +
                "EncryptionOptions.AllowUnencryptedValues temporarily.");
        }

        var decrypted = service.Decrypt(payload, aad);
        if (decrypted.IsFailure)
        {
            if (decrypted.Error.Code == CryptographyErrorCodes.UnknownKeyId)
            {
                EncryptionMeter.RecordDecryptFailure("unknown_key");
                throw new EncryptionKeyNotFoundException(payload.KeyId);
            }

            EncryptionMeter.RecordDecryptFailure("decryption_failed");
            throw new CryptographicException(
                $"Decryption failed for encrypted property '{entityTypeShortName}.{propertyPath}' " +
                $"(ciphertext key '{payload.KeyId}'). This can mean tampering, the wrong key, or ciphertext " +
                "copied from a different row, column or tenant — see EncryptionInterceptor's remarks.");
        }

        var plaintextBytes = decrypted.Value;
        try
        {
            SetClrValue(property, targetInstance, System.Text.Encoding.UTF8.GetString(plaintextBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    private void EncryptPending(DbContext context)
    {
        List<PendingRestore>? pending = null;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            byte[]? primaryKey = null;
            var tenantId = entry.Entity is IHasTenant hasTenant ? hasTenant.TenantId : (Guid?)null;
            byte[] GetPrimaryKey()
            {
                if (primaryKey is null)
                {
                    EnsurePrimaryKeyIsClientGenerated(entry);
                    primaryKey = PrimaryKeyCanonicalizer.Canonicalize(entry.Metadata, entry.Entity);
                }

                return primaryKey;
            }

            void EncryptEntryProperty(IProperty property, string purpose, string? complexPropertyName, string blindIndexShadowNameBase)
            {
                var propertyEntry = complexPropertyName is null
                    ? entry.Property(property.Name)
                    : entry.ComplexProperty(complexPropertyName).Property(property.Name);
                var plaintext = propertyEntry.CurrentValue as string;

                (pending ??= []).Add(new PendingRestore(entry, complexPropertyName, property.Name, plaintext));

                var blindIndexed = property.FindAnnotation(PropertyBuilderEncryptExtensions.BlindIndexAnnotationKey)?.Value is true;
                var perTenantKey = property.FindAnnotation(PropertyBuilderEncryptExtensions.PerTenantKeyAnnotationKey)?.Value is true;
                var blindIndexShadowName = blindIndexShadowNameBase + "BlindIndex";

                if (plaintext is null)
                {
                    propertyEntry.CurrentValue = null;
                    if (blindIndexed)
                        entry.Property(blindIndexShadowName).CurrentValue = null;
                    return;
                }

                // AAD binds the tenant id for every IHasTenant entity unconditionally — perTenantKey controls ONLY
                // which key material is used (see ResolveService), never whether tenant enters the AAD or the
                // blind index.
                var aad = AssociatedDataBuilder.Build(purpose, GetPrimaryKey(), tenantId);
                var service = ResolveService(purpose, perTenantKey, tenantId);

                try
                {
                    propertyEntry.CurrentValue = service.EncryptToString(plaintext, aad);
                }
                catch (Exception)
                {
                    // Never a silent failure: EncryptToString can throw (e.g. misconfigured key material — see
                    // AesGcmCipher.EnsureKeySize), and until this metric existed the encrypt write path had no
                    // failure telemetry at all. The exception itself already carries the diagnostic detail; the
                    // metric exists so a dashboard/alert can see this without every caller instrumenting its own
                    // SaveChanges call. Never tagged with a key id, property name, or any plaintext/ciphertext
                    // value — see EncryptionMeter's own remarks.
                    EncryptionMeter.RecordEncryptFailure("encrypt_failed");
                    throw;
                }

                if (blindIndexed)
                {
                    var normalize = property.FindAnnotation(PropertyBuilderEncryptExtensions.BlindIndexNormalizeAnnotationKey)?.Value
                        as Func<string, string>;
                    var normalized = normalize is null ? plaintext : normalize(plaintext);
                    entry.Property(blindIndexShadowName).CurrentValue =
                        _blindIndexService.Compute(purpose, normalized, tenantId);
                }
            }

            foreach (var property in entry.Metadata.GetProperties())
            {
                if (property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt)?.Value is not string purpose)
                    continue;

                EncryptEntryProperty(property, purpose, complexPropertyName: null, blindIndexShadowNameBase: property.Name);
            }

            // See InitializedInstance's identical remark: complex-type sub-properties share the owning entity's
            // row for AAD/blind-index purposes, only their own CurrentValue/shadow-column differ.
            foreach (var complexProperty in entry.Metadata.GetComplexProperties())
            {
                foreach (var property in complexProperty.ComplexType.GetProperties())
                {
                    if (property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt)?.Value is not string purpose)
                        continue;

                    EncryptEntryProperty(
                        property, purpose, complexPropertyName: complexProperty.Name,
                        blindIndexShadowNameBase: $"{complexProperty.Name}_{property.Name}");
                }
            }
        }

        if (pending is not null)
            _pending.AddOrUpdate(context, pending);
    }

    private void RestorePending(DbContext context)
    {
        if (!_pending.TryGetValue(context, out var pending))
            return;

        _pending.Remove(context);

        foreach (var restore in pending)
        {
            var propertyEntry = restore.Resolve();
            propertyEntry.CurrentValue = restore.Plaintext;

            // Accept-all-changes-on-success already ran (the entry is Unchanged) — the ciphertext this
            // interceptor wrote became the accepted "original" value, so restore OriginalValue too or a future
            // SaveChanges would see the property as (falsely) modified back to plaintext. When accept-all did NOT
            // run (entry still Added/Modified — a failed save, or SaveChanges(false)), OriginalValue already
            // reflects the pre-save value correctly and must not be touched.
            if (restore.Entry.State == EntityState.Unchanged)
                propertyEntry.OriginalValue = restore.Plaintext;
        }
    }

    // Store-generated keys (identity/serial columns) are not yet known at SavingChanges time, but the
    // primary key is one of this row's AAD inputs — see this class's "Store-generated keys" remarks. EF
    // Core marks a not-yet-assigned store-generated key property IsTemporary on its PropertyEntry, a cheap,
    // provider-agnostic signal this checks for every Added entry with an encrypted property before ever
    // computing that entry's AAD.
    private static void EnsurePrimaryKeyIsClientGenerated(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return;

        foreach (var keyProperty in key.Properties)
        {
            if (entry.Property(keyProperty.Name).IsTemporary)
            {
                throw new InvalidOperationException(
                    $"'{entry.Metadata.ShortName()}' has an encrypted property but its primary key " +
                    $"('{keyProperty.Name}') is store-generated and not yet assigned. Field-level encryption " +
                    "requires a client-generated primary key (e.g. a StronglyTypedId over Guid.NewGuid()/UUID " +
                    "v7), because the key is bound into the row's authenticated associated data before the " +
                    "physical save that would otherwise assign it.");
            }
        }
    }

    // EF Core 10's IPropertyBase exposes a delegate-based GetGetter() but no equivalent GetSetter() — only a
    // MemberInfo via GetMemberInfo(forMaterialization, forSet), which this platform's rule against
    // Type.GetMethod/MakeGenericMethod-style reflection does not cover: EF itself resolves and hands back the
    // exact member to use, this method only invokes it, mirroring EF Core's own internal materialization code
    // for the case where no compiled delegate exists on the public surface.
    private static void SetClrValue(IProperty property, object entity, string? value)
    {
        var member = ((IPropertyBase)property).GetMemberInfo(forMaterialization: true, forSet: true);
        switch (member)
        {
            case System.Reflection.PropertyInfo propertyInfo:
                propertyInfo.SetValue(entity, value);
                break;
            case System.Reflection.FieldInfo fieldInfo:
                fieldInfo.SetValue(entity, value);
                break;
            default:
                throw new InvalidOperationException(
                    $"Could not resolve a settable member for '{property.DeclaringType.ShortName()}.{property.Name}'.");
        }
    }

    private ISynchronousSymmetricEncryptionService ResolveService(string purpose, bool perTenantKey, Guid? tenantId)
    {
        if (!perTenantKey || tenantId is not { } id)
            return _encryptionService;

        var tenantKeyProvider = _keyProvider.ForPurposeSynchronous($"{purpose}:tenant", id.ToByteArray());
        return new SynchronousAesGcmEncryptionService(tenantKeyProvider);
    }
}
