using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption.Crypto;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;
using SharedKernel.Persistence.EfCore.Encryption.Interception;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

namespace SharedKernel.Persistence.EfCore.Encryption.Maintenance;

/// <summary>The <see cref="IEncryptionRotationJob"/> of one context type.</summary>
/// <typeparam name="TContext">The context whose model is processed.</typeparam>
internal sealed class EncryptionMaintenanceJob<TContext> : IEncryptionRotationJob
    where TContext : SharedKernelDbContext
{
    private const string RunOperation = "IEncryptionRotationJob.RunAsync";

    private readonly TContext context;
    private readonly FieldEncryptionRuntime runtime;
    private readonly IOptions<EncryptionOptions> options;
    private readonly IServiceProvider services;
    private readonly ILogger<EncryptionMaintenanceJob<TContext>> logger;

    public EncryptionMaintenanceJob(
        TContext context,
        FieldEncryptionRuntime runtime,
        IOptions<EncryptionOptions> options,
        IServiceProvider services,
        ILogger<EncryptionMaintenanceJob<TContext>> logger)
    {
        this.context = context;
        this.runtime = runtime;
        this.options = options;
        this.services = services;
        this.logger = logger;
    }

    public async Task<EncryptionMaintenanceReport> RunAsync(
        EncryptionMaintenanceRequest request,
        IProgress<EncryptionMaintenanceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mode = request.Mode;
        Validate(request);
        MaintenanceSession.RequireCrossTenantScope(context, services, RunOperation);

        var writes = (mode & (EncryptionMaintenanceMode.ReEncrypt | EncryptionMaintenanceMode.EncryptPlaintext)) != 0;
        CryptographicKey? currentRootKey = null;
        if (writes)
        {
            currentRootKey = await runtime.KeyRing.Provider.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(currentRootKey.Id, request.ExpectedCurrentKeyId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The key source's current key is '{currentRootKey.Id}', not the expected '{request.ExpectedCurrentKeyId}'. " +
                    "Make the new key current (and wait for every process to load it) before re-encrypting.");
            }
        }

        var targets = MaintenanceTarget.Build(context.Model, context.GetService<ISqlGenerationHelper>());
        var run = new Run(this, request, currentRootKey, progress, targets.Count);

        var completed = new HashSet<string>(StringComparer.Ordinal);
        string? resumeTarget = null;
        var resumeKey = string.Empty;
        if (request.CheckpointToken is { } token)
        {
            var checkpoint = EncryptionRotationCheckpoint.Decode(token);
            completed.UnionWith(checkpoint.CompletedTargetKeys);
            resumeTarget = checkpoint.InProgressTargetKey;
            resumeKey = checkpoint.LastPrimaryKeyText;
        }

        await using var session = await MaintenanceSession
            .OpenAsync(context, runtime, options.Value, services, RunOperation, cancellationToken)
            .ConfigureAwait(false);

        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            if (completed.Contains(target.Key))
                continue;

            var after = string.Equals(target.Key, resumeTarget, StringComparison.Ordinal) ? ParseKey(resumeKey, target.KeyKind) : null;
            var finished = await run.ProcessTargetAsync(session, target, index + 1, after, cancellationToken).ConfigureAwait(false);
            if (!finished.Completed)
            {
                var checkpoint = new EncryptionRotationCheckpoint([.. completed], target.Key, FormatKey(finished.LastKey)).Encode();
                return run.Report(completed: false, checkpoint);
            }

            completed.Add(target.Key);
        }

        return run.Report(completed: true, checkpointToken: null);
    }

    private static void Validate(EncryptionMaintenanceRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(request.BatchSize, 0, nameof(request.BatchSize));
        var mode = request.Mode;
        const EncryptionMaintenanceMode all = EncryptionMaintenanceMode.VerifyOnly | EncryptionMaintenanceMode.ReEncrypt
            | EncryptionMaintenanceMode.RecomputeBlindIndexes | EncryptionMaintenanceMode.EncryptPlaintext;
        if (mode == 0 || (mode & ~all) != 0)
            throw new ArgumentException($"'{mode}' is not a valid maintenance mode.", nameof(request));
        if (mode.HasFlag(EncryptionMaintenanceMode.VerifyOnly) && mode != EncryptionMaintenanceMode.VerifyOnly)
            throw new ArgumentException("'VerifyOnly' cannot be combined with a writing mode.", nameof(request));
        if ((mode & (EncryptionMaintenanceMode.ReEncrypt | EncryptionMaintenanceMode.EncryptPlaintext)) != 0
            && string.IsNullOrWhiteSpace(request.ExpectedCurrentKeyId))
        {
            throw new ArgumentException(
                "'ExpectedCurrentKeyId' is required to re-encrypt or encrypt plaintext.", nameof(request));
        }
    }

    private static string FormatKey(object? value) => value switch
    {
        null => string.Empty,
        Guid guid => guid.ToString("D"),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static object? ParseKey(string text, RotationKeyKind kind) => text.Length == 0 ? null : kind switch
    {
        RotationKeyKind.Guid => Guid.Parse(text),
        RotationKeyKind.Int64 => long.Parse(text, CultureInfo.InvariantCulture),
        RotationKeyKind.Int32 => int.Parse(text, CultureInfo.InvariantCulture),
        _ => text,
    };

    /// <summary>The counters and caches of one run.</summary>
    private sealed class Run(
        EncryptionMaintenanceJob<TContext> job,
        EncryptionMaintenanceRequest request,
        CryptographicKey? currentRootKey,
        IProgress<EncryptionMaintenanceProgress>? progress,
        int targetCount)
    {
        private readonly Dictionary<string, CryptographicKey?> _rootKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, TenantKeyEntry?> _tenantKeys = [];
        private readonly Dictionary<string, long> _valuesByKeyId = new(StringComparer.Ordinal);
        private long _scanned;
        private long _reEncrypted;
        private long _fromPlaintext;
        private long _blindIndexes;
        private long _concurrent;
        private long _plaintext;
        private long _undecryptable;
        private long _shredded;
        private long _shreddedTenantNotErased;
        private long _staleBlindIndexes;

        private EncryptionMaintenanceMode Mode => request.Mode;

        public async Task<(bool Completed, object? LastKey)> ProcessTargetAsync(
            MaintenanceSession session, MaintenanceTarget target, int targetIndex, object? after, CancellationToken cancellationToken)
        {
            var estimate = await EstimateRowsAsync(session, target, cancellationToken).ConfigureAwait(false);
            EncryptionLog.MaintenanceTargetStarted(job.logger, Mode.ToString(), target.Key, estimate);
            long targetScanned = 0;
            long targetPlaintext = 0;
            long targetUndecryptable = 0;
            var shreddedNotErasedBefore = _shreddedTenantNotErased;

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                    return (false, after);

                List<Row> rows;
                await using (var transaction = await BeginAsync(session, target, cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        rows = await ReadAsync(session, transaction, target, after, cancellationToken).ConfigureAwait(false);
                        if (rows.Count == 0)
                        {
                            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        }

                        var updates = new List<Update>();
                        foreach (var row in rows)
                        {
                            var (plaintextSeen, undecryptable) = await ProcessRowAsync(target, row, updates, cancellationToken).ConfigureAwait(false);
                            targetPlaintext += plaintextSeen ? 1 : 0;
                            targetUndecryptable += undecryptable ? 1 : 0;
                        }

                        if (updates.Count > 0)
                            await WriteAsync(session, transaction, target, updates, cancellationToken).ConfigureAwait(false);

                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (MaintenanceSession.TranslateRowSecurityError(exception, target.Table) is { } translated)
                    {
                        throw translated;
                    }
                }

                _scanned += rows.Count;
                targetScanned += rows.Count;
                after = rows[^1].Key;
                EncryptionLog.MaintenanceBatchProcessed(job.logger, target.Key, rows.Count);
                progress?.Report(new EncryptionMaintenanceProgress(target.Key, targetIndex, targetCount, targetScanned, estimate));
            }

            if (targetPlaintext > 0)
                EncryptionLog.MaintenanceValuesSkipped(job.logger, target.Key, targetPlaintext, "plaintext");
            if (targetUndecryptable > 0)
                EncryptionLog.MaintenanceValuesSkipped(job.logger, target.Key, targetUndecryptable, "undecryptable");
            if (_shreddedTenantNotErased > shreddedNotErasedBefore)
            {
                EncryptionLog.MaintenanceValuesSkipped(
                    job.logger, target.Key, _shreddedTenantNotErased - shreddedNotErasedBefore, "shredded tenant, not erased: delete the rows");
            }

            // Without the row-security bypass the scan may have been filtered; refuse an empty scan of a table the
            // statistics say is not empty, rather than report completion over rows it never saw.
            if (!job.options.Value.RequireRowSecurityBypass && targetScanned == 0 && after is null && estimate > 0
                && await CountVisibleRowsAsync(session, target, cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new InvalidOperationException(
                    $"Encryption maintenance saw no rows in {target.Table}, but PostgreSQL estimates {estimate}. The " +
                    "connecting role probably cannot see them (row-level security). Run maintenance as a role that sees " +
                    "every row.");
            }

            return (true, after);
        }

        public EncryptionMaintenanceReport Report(bool completed, string? checkpointToken)
        {
            EncryptionMeter.RecordMaintenanceRowsWritten("re_encrypt", _reEncrypted);
            EncryptionMeter.RecordMaintenanceRowsWritten("encrypt_plaintext", _fromPlaintext);
            EncryptionMeter.RecordMaintenanceRowsWritten("recompute_blind_index", _blindIndexes);
            EncryptionMeter.RecordMaintenanceRowsSkipped("concurrent_write", _concurrent);
            EncryptionMeter.RecordMaintenanceRowsSkipped("plaintext", _plaintext);
            EncryptionMeter.RecordMaintenanceRowsSkipped("undecryptable", _undecryptable);
            EncryptionMeter.RecordMaintenanceRowsSkipped("shredded", _shredded);
            EncryptionMeter.RecordMaintenanceRowsSkipped("shredded_tenant_not_erased", _shreddedTenantNotErased);
            EncryptionLog.MaintenanceCompleted(
                job.logger, Mode.ToString(), _scanned, _reEncrypted + _fromPlaintext + _blindIndexes, _concurrent, _plaintext, _undecryptable, _shredded);

            return new EncryptionMaintenanceReport(
                Mode, completed, checkpointToken, _scanned, _reEncrypted, _fromPlaintext, _blindIndexes, _concurrent,
                _plaintext, _undecryptable, _shredded, _shreddedTenantNotErased, _staleBlindIndexes,
                new Dictionary<string, long>(_valuesByKeyId, StringComparer.Ordinal));
        }

        // Decides what one value needs and queues the write; returns whether it was plaintext left alone and whether
        // it failed to decrypt.
        private async Task<(bool Plaintext, bool Undecryptable)> ProcessRowAsync(
            MaintenanceTarget target, Row row, List<Update> updates, CancellationToken cancellationToken)
        {
            var member = target.Member;
            var runtime = job.runtime;
            var associatedData = AssociatedDataBuilder.Build(member.Purpose, PrimaryKeyCanonicalizer.CanonicalizeSingleValue(row.Key), row.TenantId);
            var usesTenantKey = runtime.UsesTenantKey(member) && row.TenantId is not null;

            // A shredded tenant's row is never written (no re-encryption, no recomputed blind index), and its values are
            // counted for what they are: under the destroyed tenant key they are erased; under a root key or as
            // plaintext they are not, whatever the platform refuses to read.
            if (usesTenantKey && await LoadTenantKeyAsync(row.TenantId!.Value, create: false, cancellationToken).ConfigureAwait(false) is { IsShredded: true })
            {
                var parsed = EncryptedPayload.TryParse(row.Value, out var stored);
                if (parsed && TenantKeyIds.TryParse(stored!.KeyId, out _))
                {
                    _shredded++;
                    Count(EncryptionMaintenanceReport.TenantDataKeysLabel);
                }
                else
                {
                    _shreddedTenantNotErased++;
                    Count(parsed ? stored!.KeyId : "(plaintext)");
                }

                return (false, false);
            }

            string plaintext;
            string? currentKeyId;
            if (!EncryptedPayload.TryParse(row.Value, out var payload))
            {
                if (!Mode.HasFlag(EncryptionMaintenanceMode.EncryptPlaintext))
                {
                    _plaintext++;
                    Count("(plaintext)");
                    return (true, false);
                }

                plaintext = row.Value;
                currentKeyId = null;
            }
            else
            {
                currentKeyId = payload.KeyId;
                var tenantKeyNeeded = TenantKeyIds.TryParse(payload.KeyId, out var keyTenant) ? keyTenant : (Guid?)null;
                if (tenantKeyNeeded is { } tenant)
                    await LoadTenantKeyAsync(tenant, create: false, cancellationToken).ConfigureAwait(false);
                else
                    await LoadRootKeyAsync(payload.KeyId, cancellationToken).ConfigureAwait(false);

                var outcome = runtime.Cipher.TryDecrypt(
                    payload, member.Purpose, associatedData, row.TenantId,
                    id => _rootKeys.GetValueOrDefault(id), id => _tenantKeys.GetValueOrDefault(id), out var decrypted);

                if (outcome != DecryptOutcome.Success)
                {
                    if (outcome == DecryptOutcome.TenantKeyShredded)
                        _shredded++;
                    else
                        _undecryptable++;

                    Count(Label(payload.KeyId));
                    return (false, outcome != DecryptOutcome.TenantKeyShredded);
                }

                plaintext = decrypted!;
            }

            var newValue = row.Value;
            var reEncrypt = currentKeyId is null
                || (Mode.HasFlag(EncryptionMaintenanceMode.ReEncrypt)
                    && !string.Equals(currentKeyId, await DesiredKeyIdAsync(row, usesTenantKey, cancellationToken).ConfigureAwait(false), StringComparison.Ordinal));

            if (reEncrypt && Mode != EncryptionMaintenanceMode.VerifyOnly)
            {
                if (usesTenantKey)
                {
                    var tenantKey = await LoadTenantKeyAsync(row.TenantId!.Value, create: true, cancellationToken).ConfigureAwait(false);
                    if (tenantKey is null || tenantKey.IsShredded)
                    {
                        // Shredded while this run was going: the value itself (root key or plaintext) is still readable.
                        _shreddedTenantNotErased++;
                        Count(Label(currentKeyId ?? "(plaintext)"));
                        return (false, false);
                    }

                    newValue = FieldCipher.EncryptWithTenantKey(tenantKey, member.Purpose, associatedData, plaintext);
                }
                else
                {
                    newValue = runtime.Cipher.EncryptWithRootKey(currentRootKey!, member.Purpose, associatedData, plaintext);
                }
            }

            var newIndex = row.BlindIndex;
            if (target.BlindIndexColumn is not null)
            {
                var computed = runtime.BlindIndexer.Compute(member, plaintext, row.TenantId);
                if (Mode == EncryptionMaintenanceMode.VerifyOnly)
                {
                    if (!string.Equals(computed, row.BlindIndex, StringComparison.Ordinal))
                        _staleBlindIndexes++;
                }
                else if (reEncrypt || Mode.HasFlag(EncryptionMaintenanceMode.RecomputeBlindIndexes))
                {
                    newIndex = computed;
                }
            }

            var valueChanged = !string.Equals(newValue, row.Value, StringComparison.Ordinal);
            var indexChanged = !string.Equals(newIndex, row.BlindIndex, StringComparison.Ordinal);
            if (valueChanged || indexChanged)
            {
                updates.Add(new Update(row, newValue, newIndex, valueChanged ? (currentKeyId is null ? UpdateKind.FromPlaintext : UpdateKind.ReEncrypt) : UpdateKind.None, indexChanged));
            }
            else
            {
                Count(Label(currentKeyId!));
            }

            return (false, false);
        }

        private async ValueTask<string> DesiredKeyIdAsync(Row row, bool usesTenantKey, CancellationToken cancellationToken)
        {
            if (usesTenantKey)
                return TenantKeyIds.For(row.TenantId!.Value);

            return (currentRootKey ?? await job.runtime.KeyRing.Provider.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false)).Id;
        }

        private async Task<CryptographicKey?> LoadRootKeyAsync(string keyId, CancellationToken cancellationToken)
        {
            if (!_rootKeys.TryGetValue(keyId, out var key))
            {
                key = await job.runtime.KeyRing.Provider.GetKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
                _rootKeys[keyId] = key;
            }

            return key;
        }

        private async Task<TenantKeyEntry?> LoadTenantKeyAsync(Guid tenantId, bool create, CancellationToken cancellationToken)
        {
            if (_tenantKeys.TryGetValue(tenantId, out var entry) && (entry is not null || !create))
                return entry;

            entry = await job.runtime.TenantKeys
                .GetAsync(tenantId, job.runtime.SideConnection(job.context), create, cancellationToken)
                .ConfigureAwait(false);
            _tenantKeys[tenantId] = entry;
            return entry;
        }

        private void Count(string label) => _valuesByKeyId[label] = _valuesByKeyId.GetValueOrDefault(label) + 1;

        private static string Label(string keyId) =>
            TenantKeyIds.TryParse(keyId, out _) ? EncryptionMaintenanceReport.TenantDataKeysLabel : keyId;

        private static async Task<DbTransaction> BeginAsync(MaintenanceSession session, MaintenanceTarget target, CancellationToken cancellationToken)
        {
            try
            {
                return await session.BeginAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (MaintenanceSession.TranslateRowSecurityError(exception, target.Table) is { } translated)
            {
                throw translated;
            }
        }

        private async Task<List<Row>> ReadAsync(
            MaintenanceSession session, DbTransaction transaction, MaintenanceTarget target, object? after, CancellationToken cancellationToken)
        {
            var tenantColumn = target.TenantSource is { } tenant ? tenant.Column : "NULL::uuid";
            var blindIndex = target.BlindIndexColumn is { } index ? "t." + index : "NULL::text";
            await using var command = session.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"SELECT t.{target.PrimaryKeyColumn}, t.{target.ValueColumn}, {tenantColumn}, {blindIndex} " +
                $"FROM {target.Table} AS t{target.TenantSource?.Join} " +
                $"WHERE t.{target.ValueColumn} IS NOT NULL{(after is null ? string.Empty : $" AND t.{target.PrimaryKeyColumn} > @after")} " +
                $"ORDER BY t.{target.PrimaryKeyColumn} LIMIT {request.BatchSize.ToString(CultureInfo.InvariantCulture)}";
            if (after is not null)
                AddParameter(command, "after", after);

            var rows = new List<Row>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = target.KeyKind switch
                {
                    RotationKeyKind.Guid => (object)reader.GetGuid(0),
                    RotationKeyKind.Int64 => reader.GetInt64(0),
                    RotationKeyKind.Int32 => reader.GetInt32(0),
                    _ => reader.GetString(0),
                };
                rows.Add(new Row(
                    key,
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }

            return rows;
        }

        // One compare-and-swap statement per batch: a row is written only if its value still equals the value read.
        private async Task WriteAsync(
            MaintenanceSession session, DbTransaction transaction, MaintenanceTarget target, List<Update> updates, CancellationToken cancellationToken)
        {
            var setIndex = target.BlindIndexColumn is { } index ? $", {index} = v.new_index" : string.Empty;
            await using var command = session.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"UPDATE {target.Table} AS t SET {target.ValueColumn} = v.new_value{setIndex} " +
                "FROM unnest(@keys, @old_values, @new_values, @new_indexes) AS v(key, old_value, new_value, new_index) " +
                $"WHERE t.{target.PrimaryKeyColumn} = v.key AND t.{target.ValueColumn} = v.old_value " +
                $"RETURNING t.{target.PrimaryKeyColumn}";

            AddParameter(command, "keys", target.KeyKind switch
            {
                RotationKeyKind.Guid => updates.Select(u => (Guid)u.Row.Key).ToArray(),
                RotationKeyKind.Int64 => updates.Select(u => (long)u.Row.Key).ToArray(),
                RotationKeyKind.Int32 => updates.Select(u => (int)u.Row.Key).ToArray(),
                _ => (object)updates.Select(u => (string)u.Row.Key).ToArray(),
            });
            AddParameter(command, "old_values", updates.Select(u => u.Row.Value).ToArray());
            AddParameter(command, "new_values", updates.Select(u => u.NewValue).ToArray());
            AddParameter(command, "new_indexes", updates.Select(u => u.NewIndex).ToArray());

            var applied = new HashSet<object>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    applied.Add(reader.GetValue(0));
            }

            foreach (var update in updates)
            {
                if (!applied.Contains(update.Row.Key))
                {
                    _concurrent++;
                    Count(update.Row.Value is { } old && EncryptedPayload.TryParse(old, out var oldPayload) ? Label(oldPayload.KeyId) : "(plaintext)");
                    continue;
                }

                switch (update.Kind)
                {
                    case UpdateKind.ReEncrypt:
                        _reEncrypted++;
                        break;
                    case UpdateKind.FromPlaintext:
                        _fromPlaintext++;
                        break;
                }

                if (update.IndexChanged)
                    _blindIndexes++;

                Count(EncryptedPayload.TryParse(update.NewValue, out var newPayload) ? Label(newPayload.KeyId) : "(plaintext)");
            }
        }

        private static async Task<long> EstimateRowsAsync(MaintenanceSession session, MaintenanceTarget target, CancellationToken cancellationToken)
        {
            await using var command = session.Connection.CreateCommand();
            command.CommandText = "SELECT COALESCE((SELECT GREATEST(reltuples, 0)::bigint FROM pg_class WHERE oid = to_regclass(@table)), 0)";
            AddParameter(command, "table", target.QualifiedTableName);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        private static async Task<long> CountVisibleRowsAsync(MaintenanceSession session, MaintenanceTarget target, CancellationToken cancellationToken)
        {
            await using var command = session.Connection.CreateCommand();
            command.CommandText = $"SELECT count(*) FROM {target.Table}";
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }

    private sealed record Row(object Key, string Value, Guid? TenantId, string? BlindIndex);

    private enum UpdateKind
    {
        None,
        ReEncrypt,
        FromPlaintext,
    }

    private sealed record Update(Row Row, string NewValue, string? NewIndex, UpdateKind Kind, bool IndexChanged);
}
