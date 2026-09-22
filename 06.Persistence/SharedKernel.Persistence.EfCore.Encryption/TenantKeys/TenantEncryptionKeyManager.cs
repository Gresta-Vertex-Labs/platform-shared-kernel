using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;
using SharedKernel.Persistence.EfCore.Encryption.Interception;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;

namespace SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

/// <summary>The <see cref="ITenantEncryptionKeyManager"/> of one context type.</summary>
internal sealed class TenantEncryptionKeyManager<TContext>(
    TContext context,
    FieldEncryptionRuntime runtime,
    IOptions<EncryptionOptions> encryptionOptions,
    IServiceProvider services,
    ILogger<TenantEncryptionKeyManager<TContext>> logger) : ITenantEncryptionKeyManager
    where TContext : SharedKernelDbContext
{
    private const string ShredOperation = "ITenantEncryptionKeyManager.ShredTenantAsync";

    public async Task EnsureTenantKeyAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        var entry = await runtime.TenantKeys.GetAsync(tenantId, runtime.SideConnection(context), create: true, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.IsShredded)
            throw new TenantKeyShreddedException();
    }

    public async Task<TenantShredResult> ShredTenantAsync(
        Guid tenantId,
        TenantShredOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        options ??= new TenantShredOptions();
        MaintenanceSession.RequireCrossTenantScope(context, services, ShredOperation);

        if (!runtime.Settings.TenantDataKeys)
        {
            throw new InvalidOperationException(
                "Tenant data keys are not enabled ('UseFieldEncryption(k => k.UseTenantDataKeys())'): every encrypted value is " +
                "under a root key, which shredding a tenant cannot erase.");
        }

        var targets = MaintenanceTarget.Build(context.Model, context.GetService<ISqlGenerationHelper>())
            .Where(t => t.TenantSource is not null && runtime.UsesTenantKey(t.Member))
            .ToList();

        long cleared = 0;
        (long RootKey, long Plaintext) remaining;
        await using (var session = await MaintenanceSession
            .OpenAsync(context, runtime, encryptionOptions.Value, services, ShredOperation, cancellationToken)
            .ConfigureAwait(false))
        {
            await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // The tombstone first: it takes the key row's lock, so every in-flight save of the tenant (which holds a
                // share lock on the same row) has committed before the scan below, and none starts until this commits.
                await runtime.TenantKeys.ShredAsync(tenantId, session.Connection, transaction, cancellationToken).ConfigureAwait(false);

                remaining = await CountValuesOutsideTenantKeyAsync(session.Connection, transaction, targets, tenantId, cancellationToken)
                    .ConfigureAwait(false);
                if ((remaining.RootKey > 0 || remaining.Plaintext > 0) && !options.AllowIncompleteErasure)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw new TenantShredIncompleteException(remaining.RootKey, remaining.Plaintext);
                }

                foreach (var target in targets.Where(t => t.BlindIndexColumn is not null))
                    cleared += await ClearBlindIndexAsync(session.Connection, transaction, target, tenantId, cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (MaintenanceSession.TranslateRowSecurityError(exception, "the tenant's tables") is { } translated)
            {
                throw translated;
            }
        }

        runtime.TenantKeys.MarkShredded(tenantId);
        EncryptionMeter.RecordTenantKeyShredded();
        EncryptionLog.TenantKeyShredded(logger, cleared);
        if (remaining.RootKey > 0 || remaining.Plaintext > 0)
            EncryptionLog.TenantShredIncomplete(logger, remaining.RootKey, remaining.Plaintext);

        return new TenantShredResult(tenantId, cleared, remaining.RootKey, remaining.Plaintext);
    }

    // Reads the tenant's values of every column encrypted with tenant data keys and counts those the key's destruction
    // does not erase: values under another key (a root key) and plaintext.
    private static async Task<(long RootKey, long Plaintext)> CountValuesOutsideTenantKeyAsync(
        DbConnection connection, DbTransaction transaction, List<MaintenanceTarget> targets, Guid tenantId, CancellationToken cancellationToken)
    {
        var tenantKeyId = TenantKeyIds.For(tenantId);
        long rootKey = 0;
        long plaintext = 0;
        foreach (var target in targets)
        {
            var (join, tenantColumn) = target.TenantSource!.Value;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"SELECT t.{target.ValueColumn} FROM {target.Table} AS t{join} WHERE {tenantColumn} = @tenant AND t.{target.ValueColumn} IS NOT NULL";
            AddTenantParameter(command, tenantId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!EncryptedPayload.TryParse(reader.GetString(0), out var payload))
                    plaintext++;
                else if (!string.Equals(payload.KeyId, tenantKeyId, StringComparison.Ordinal))
                    rootKey++;
            }
        }

        return (rootKey, plaintext);
    }

    private static async Task<int> ClearBlindIndexAsync(
        DbConnection connection, DbTransaction transaction, MaintenanceTarget target, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var (join, tenantColumn) = target.TenantSource!.Value;
        command.CommandText = join.Length == 0
            ? $"UPDATE {target.Table} AS t SET {target.BlindIndexColumn} = NULL WHERE {tenantColumn} = @tenant AND t.{target.BlindIndexColumn} IS NOT NULL"
            : $"UPDATE {target.Table} AS t SET {target.BlindIndexColumn} = NULL FROM (SELECT t.{target.PrimaryKeyColumn} AS key FROM {target.Table} AS t{join} WHERE {tenantColumn} = @tenant) AS s WHERE t.{target.PrimaryKeyColumn} = s.key AND t.{target.BlindIndexColumn} IS NOT NULL";
        AddTenantParameter(command, tenantId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddTenantParameter(DbCommand command, Guid tenantId)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenantId;
        command.Parameters.Add(parameter);
    }
}
