using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;
using SharedKernel.Persistence.EfCore.Encryption.Interception;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;

namespace SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

/// <summary>The <see cref="ITenantEncryptionKeyManager"/> of one context type.</summary>
internal sealed class TenantEncryptionKeyManager<TContext>(
    TContext context,
    FieldEncryptionRuntime runtime,
    IOptions<EncryptionOptions> options,
    IServiceProvider services,
    ILogger<TenantEncryptionKeyManager<TContext>> logger) : ITenantEncryptionKeyManager
    where TContext : SharedKernelDbContext
{
    public async Task EnsureTenantKeyAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        var entry = await runtime.TenantKeys.GetAsync(tenantId, runtime.SideConnection(context), create: true, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.IsShredded)
            throw new TenantKeyShreddedException();
    }

    public async Task<TenantShredResult> ShredTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);

        var targets = MaintenanceTarget.Build(context.Model, context.GetService<ISqlGenerationHelper>())
            .Where(t => t.BlindIndexColumn is not null && t.TenantSource is not null)
            .ToList();

        long cleared = 0;
        await using (var session = await MaintenanceSession
            .OpenAsync(context, runtime, options.Value, services, "tenant crypto-shredding", cancellationToken)
            .ConfigureAwait(false))
        {
            await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await runtime.TenantKeys.ShredAsync(tenantId, session.Connection, transaction, cancellationToken).ConfigureAwait(false);

                foreach (var target in targets)
                {
                    await using var command = session.Connection.CreateCommand();
                    command.Transaction = transaction;
                    var (join, tenantColumn) = target.TenantSource!.Value;
                    command.CommandText = join.Length == 0
                        ? $"UPDATE {target.Table} AS t SET {target.BlindIndexColumn} = NULL WHERE {tenantColumn} = @tenant AND t.{target.BlindIndexColumn} IS NOT NULL"
                        : $"UPDATE {target.Table} AS t SET {target.BlindIndexColumn} = NULL FROM (SELECT t.{target.PrimaryKeyColumn} AS key FROM {target.Table} AS t{join} WHERE {tenantColumn} = @tenant) AS s WHERE t.{target.PrimaryKeyColumn} = s.key AND t.{target.BlindIndexColumn} IS NOT NULL";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "tenant";
                    parameter.Value = tenantId;
                    command.Parameters.Add(parameter);
                    cleared += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

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
        return new TenantShredResult(tenantId, cleared);
    }
}
