using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Configuration;
using SharedKernel.Persistence.EfCore.Encryption.Crypto;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;
using SharedKernel.Persistence.EfCore.Encryption.KeyRing;
using SharedKernel.Persistence.EfCore.Encryption.Metadata;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

namespace SharedKernel.Persistence.EfCore.Encryption.Interception;

/// <summary>The services every field-encryption component shares, one instance per container.</summary>
internal sealed class FieldEncryptionRuntime(
    IServiceProvider services,
    FieldEncryptionSettings settings,
    FieldKeyRing keyRing,
    FieldCipher cipher,
    BlindIndexer blindIndexer,
    TenantKeyStore tenantKeys)
{
    private DbDataSource? _sideDataSource;
    private bool _sideDataSourceResolved;

    public FieldEncryptionSettings Settings => settings;

    public FieldKeyRing KeyRing => keyRing;

    public FieldCipher Cipher => cipher;

    public BlindIndexer BlindIndexer => blindIndexer;

    public TenantKeyStore TenantKeys => tenantKeys;

    /// <summary>Whether values of <paramref name="member"/>'s entity are encrypted with the row's tenant data key.</summary>
    public bool UsesTenantKey(EncryptedMember member) =>
        settings.TenantDataKeys && typeof(Domain.Abstractions.IHasTenant).IsAssignableFrom(member.EntityType.ClrType);

    /// <summary>Opens a connection separate from <paramref name="context"/>'s own, for tenant key reads and writes.</summary>
    public Func<CancellationToken, Task<DbConnection>> SideConnection(DbContext context) => async cancellationToken =>
    {
        if (!_sideDataSourceResolved)
        {
            _sideDataSource = settings.ResolveSideDataSource(services);
            _sideDataSourceResolved = true;
        }

        if (_sideDataSource is { } dataSource)
            return await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Npgsql clones a connection with its data source and credentials, so the clone reaches the same database
        // as the context without needing the password the connection string no longer shows.
        if (context.Database.GetDbConnection() is ICloneable cloneable && cloneable.Clone() is DbConnection clone)
        {
            await clone.OpenAsync(cancellationToken).ConfigureAwait(false);
            return clone;
        }

        throw new InvalidOperationException(
            "Tenant data keys need a connection of their own. Register an NpgsqlDataSource or call " +
            "'UseFieldEncryption(k => k.UseMaintenanceDataSource(...))'.");
    };

    /// <summary>
    /// Decrypts one stored value on the synchronous path, turning every failure into the exception the caller
    /// should see.
    /// </summary>
    public string Decrypt(EncryptedMember member, string stored, ReadOnlySpan<byte> associatedData, Guid? tenantId, DbContext context)
    {
        if (!EncryptedPayload.TryParse(stored, out var payload))
        {
            EncryptionMeter.RecordDecryptFailure("malformed_payload");
            throw new CryptographicException(
                $"The stored value of encrypted property '{member.DisplayName}' is not an encrypted payload: it was " +
                "written unencrypted, truncated or altered. To encrypt existing plaintext, run the maintenance job with " +
                "'EncryptionMaintenanceMode.EncryptPlaintext'.");
        }

        var side = SideConnection(context);
        var outcome = cipher.TryDecrypt(
            payload,
            member.Purpose,
            associatedData,
            tenantId,
            keyRing.GetKey,
            tenant => tenantKeys.GetBlocking(tenant, side, create: false),
            out var plaintext);

        switch (outcome)
        {
            case DecryptOutcome.Success:
                return plaintext!;
            case DecryptOutcome.UnknownKey:
                EncryptionMeter.RecordDecryptFailure("unknown_key");
                throw new EncryptionKeyNotFoundException(payload.KeyId);
            case DecryptOutcome.TenantKeyShredded:
                EncryptionMeter.RecordDecryptFailure("tenant_key_shredded");
                throw new TenantKeyShreddedException();
            default:
                EncryptionMeter.RecordDecryptFailure("decryption_failed");
                throw new CryptographicException(
                    $"Decryption failed for encrypted property '{member.DisplayName}' (key '{payload.KeyId}'): the " +
                    "value was altered, or copied from another row, column or tenant.");
        }
    }

    /// <summary>Finds the runtime of the encryption interceptor registered on <paramref name="context"/>.</summary>
    public static FieldEncryptionRuntime For(DbContext context)
    {
        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors;
        return interceptors?.OfType<EncryptionInterceptor>().FirstOrDefault()?.Runtime
            ?? throw new InvalidOperationException(
                $"Field encryption is not wired into '{context.GetType().Name}'. Call 'UseFieldEncryption(...)' on its persistence builder.");
    }
}
