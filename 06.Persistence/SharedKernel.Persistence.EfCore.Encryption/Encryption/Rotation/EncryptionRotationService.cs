using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.KeyDerivation;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption.Rotation;

/// <summary>
/// Default <see cref="IEncryptionRotationJob"/>: re-encrypts every row not already on the current key, entity type
/// by entity type, using raw ADO.NET — never EF Core's change tracker, value converters, query filters, or
/// platform interceptors.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Bypasses everything on purpose:</strong> reads raw ciphertext text with a plain <c>SELECT</c> (so soft-
/// deleted and cross-tenant rows are included — there are no EF query filters in a hand-written SQL string), and
/// writes back with a compare-and-swap <c>UPDATE... WHERE pk = @pk AND ciphertext_column = @old</c> issued
/// directly against the connection (so <c>AuditInterceptor</c> never stamps a rotation as a business change, and a
/// concurrent writer's own save is detected — zero affected rows — rather than silently overwritten, a real lost-
/// update guard). Plaintext is decrypted only transiently, to recompute a blind index, and is zeroed immediately
/// after (<see cref="CryptographicOperations.ZeroMemory(System.Span{byte})"/>) — it never reaches a log, an exception message,
/// or the returned <see cref="EncryptionRotationReport"/>.
/// </para>
/// <para>
/// <strong>Scope:</strong> only entity types with a SINGLE-column primary key whose provider (post-converter) type
/// is one of <see cref="RotationKeyKind"/> (<see cref="Guid"/>, <see langword="long"/>, <see langword="int"/>,
/// <see langword="string"/> — covering <c>StronglyTypedId&lt;Guid&gt;</c>/<c>&lt;long&gt;</c>/<c>&lt;string&gt;</c>
/// and a bare <see langword="int"/> key, the realistic single-column shapes on this platform) are supported. An
/// entity type with a composite key, or a single-column key of any other type, that also has an
/// <c>.Encrypt(...)</c> property fails at MODEL BUILD time — see <see cref="EncryptionModelConvention"/>'s own
/// validation — not only when a rotation happens to run; <see cref="BuildTargets"/> re-checks the same shape as
/// defense in depth, so a model built before this validation existed still fails loudly here rather than silently
/// corrupting data. Widening beyond composite keys is a documented follow-up (see the type's own remarks).
/// </para>
/// </remarks>
public sealed class EncryptionRotationService<TContext> : IEncryptionRotationJob
    where TContext : SharedKernelDbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly ISymmetricEncryptionService _encryptionService;
    private readonly IEncryptionKeyProvider _keyProvider;
    private readonly IBlindIndexService _blindIndexService;
    private readonly ILogger<EncryptionRotationService<TContext>> _logger;

    /// <summary>Initialises a new <see cref="EncryptionRotationService{TContext}"/>.</summary>
    public EncryptionRotationService(
        IDbContextFactory<TContext> contextFactory,
        ISymmetricEncryptionService encryptionService,
        IEncryptionKeyProvider keyProvider,
        IBlindIndexService blindIndexService,
        ILogger<EncryptionRotationService<TContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(encryptionService);
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(blindIndexService);
        ArgumentNullException.ThrowIfNull(logger);
        _contextFactory = contextFactory;
        _encryptionService = encryptionService;
        _keyProvider = keyProvider;
        _blindIndexService = blindIndexService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EncryptionRotationReport> RotateAsync(
        string expectedCurrentKeyId,
        string? checkpointToken = null,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCurrentKeyId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchSize, 0);

        var currentKey = await _keyProvider.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(currentKey.Id, expectedCurrentKeyId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected the registered key provider's current key to be '{expectedCurrentKeyId}' but it is " +
                $"'{currentKey.Id}'. Make the expected key current at the key provider before rotating rows onto it.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var sqlGenerationHelper = context.GetService<ISqlGenerationHelper>();
        var targets = BuildTargets(context.Model, sqlGenerationHelper);

        // Identified BY NAME (RotationTarget.CheckpointKey), never by position — see EncryptionRotationCheckpoint's
        // remarks for why a positional index cannot survive an '.Encrypt(...)' property being added to or removed
        // from the model between the call that produced this checkpoint and this one.
        var completed = new HashSet<string>(StringComparer.Ordinal);
        string? inProgressTargetKey = null;
        var lastPrimaryKeyText = "";
        if (checkpointToken is not null)
        {
            var checkpoint = EncryptionRotationCheckpoint.Decode(checkpointToken);
            completed = new HashSet<string>(checkpoint.CompletedTargetKeys, StringComparer.Ordinal);
            inProgressTargetKey = checkpoint.InProgressTargetKey;
            lastPrimaryKeyText = checkpoint.LastPrimaryKeyText;
        }

        var connection = context.Database.GetDbConnection();
        var ownsConnection = connection.State != ConnectionState.Open;
        if (ownsConnection)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        long rowsProcessed = 0;
        long rowsRotated = 0;
        long rowsFailed = 0;
        long rowsSkippedUnparseable = 0;
        var remaining = new Dictionary<string, long>(StringComparer.Ordinal);

        try
        {
            foreach (var target in targets)
            {
                // Already fully scanned by a previous call — durable by name, immune to every other target's
                // position shifting around it.
                if (completed.Contains(target.CheckpointKey))
                    continue;

                var after = string.Equals(target.CheckpointKey, inProgressTargetKey, StringComparison.Ordinal)
                    ? ParsePrimaryKeyText(lastPrimaryKeyText, target.KeyKind)
                    : null;

                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        var token = new EncryptionRotationCheckpoint(
                            [.. completed], target.CheckpointKey, FormatPrimaryKeyText(after, target.KeyKind)).Encode();
                        var partialRemaining = remaining
                            .Where(kv => kv.Value > 0 && !string.Equals(kv.Key, expectedCurrentKeyId, StringComparison.Ordinal))
                            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

                        // Surface progress made before cancellation the same way a completed run does — an
                        // operator watching the meter should see a cancelled/resumed run's progress too, not only
                        // a run that happened to finish in one call.
                        foreach (var (keyId, count) in partialRemaining)
                            EncryptionMeter.RecordRotationRowsFailed(keyId, count);
                        if (rowsRotated > 0)
                            EncryptionMeter.RecordRotationRowsRotated(expectedCurrentKeyId, rowsRotated);
                        if (rowsSkippedUnparseable > 0)
                            EncryptionMeter.RecordRotationRowsSkippedUnparseable("(model)", rowsSkippedUnparseable);

                        return new EncryptionRotationReport(
                            rowsProcessed, rowsRotated, rowsFailed, rowsSkippedUnparseable, false, token, partialRemaining);
                    }

                    var page = await ReadPageAsync(connection, target, after, batchSize, cancellationToken).ConfigureAwait(false);
                    if (page.Count == 0)
                        break;

                    foreach (var row in page)
                    {
                        rowsProcessed++;
                        after = row.PrimaryKey;

                        // A NULL stored value (a nullable encrypted property never populated) is not this job's
                        // concern at all — nothing to rotate, not a skip. A NON-NULL value that fails to parse is
                        // different: either a column still mid-migration (see EncryptionOptions.AllowUnencryptedValues)
                        // or corrupted data — either way, silently moving on without counting it hides exactly the
                        // information an operator needs before trusting a "rotation complete" report and retiring
                        // the old key.
                        if (row.Ciphertext is null)
                            continue;

                        if (!EncryptedPayload.TryParse(row.Ciphertext, out var payload))
                        {
                            rowsSkippedUnparseable++;
                            EncryptionLog.RotationRowUnparseable(_logger, target.EntityTypeName);
                            continue;
                        }

                        remaining[payload.KeyId] = remaining.GetValueOrDefault(payload.KeyId) + 1;

                        if (string.Equals(payload.KeyId, expectedCurrentKeyId, StringComparison.Ordinal))
                            continue;

                        var pkBytes = PrimaryKeyCanonicalizer.CanonicalizeSingleValue(row.PrimaryKey);
                        // AAD binds the tenant id for every IHasTenant entity unconditionally — perTenantKey
                        // controls ONLY which key material ResolveService picks, never whether tenant enters
                        // the AAD or the blind index (see EncryptionInterceptor's identical rule).
                        var aad = AssociatedDataBuilder.Build(target.Purpose, pkBytes, row.TenantId);
                        var service = ResolveService(target.Purpose, target.PerTenantKey, row.TenantId);

                        var reencrypted = await service.ReEncryptAsync(payload, aad, cancellationToken).ConfigureAwait(false);
                        if (reencrypted.IsFailure)
                        {
                            rowsFailed++;
                            continue;
                        }

                        string? newBlindIndex = null;
                        if (target.BlindIndexColumn is not null)
                        {
                            // Pinned to the SAME currentKey RotateAsync already validated above — never the
                            // (possibly stale) ambient ISynchronousEncryptionKeyProvider — see
                            // IBlindIndexService.Compute(CryptographicKey, ...)'s remarks for why.
                            newBlindIndex = await ComputeBlindIndexAsync(target, row, reencrypted.Value, aad, currentKey, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        var applied = await CompareAndSwapAsync(
                            connection, target, row.PrimaryKey, row.Ciphertext, reencrypted.Value.ToString(), newBlindIndex, cancellationToken)
                                .ConfigureAwait(false);

                        if (applied)
                        {
                            rowsRotated++;
                            remaining[payload.KeyId]--;
                        }
                        else
                        {
                            rowsFailed++;
                            EncryptionLog.RotationRowConcurrentlyModified(_logger, target.EntityTypeName);
                        }
                    }

                    EncryptionLog.RotationBatchProcessed(_logger, target.EntityTypeName, page.Count, expectedCurrentKeyId);
                }

                completed.Add(target.CheckpointKey);
            }
        }
        finally
        {
            if (ownsConnection)
                await connection.CloseAsync().ConfigureAwait(false);
        }

        remaining.Remove(expectedCurrentKeyId);
        var remainingOutstanding = remaining.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        foreach (var (keyId, count) in remainingOutstanding)
            EncryptionMeter.RecordRotationRowsFailed(keyId, count);

        EncryptionMeter.RecordRotationRowsRotated(expectedCurrentKeyId, rowsRotated);
        if (rowsSkippedUnparseable > 0)
            EncryptionMeter.RecordRotationRowsSkippedUnparseable("(model)", rowsSkippedUnparseable);

        EncryptionLog.RotationCompleted(_logger, "(model)", expectedCurrentKeyId, rowsProcessed, rowsRotated, rowsFailed, rowsSkippedUnparseable);

        return new EncryptionRotationReport(rowsProcessed, rowsRotated, rowsFailed, rowsSkippedUnparseable, true, null, remainingOutstanding);
    }

    private async Task<string?> ComputeBlindIndexAsync(
        RotationTarget target, RotationRow row, EncryptedPayload reencrypted, byte[] aad, CryptographicKey currentKey, CancellationToken cancellationToken)
    {
        var service = ResolveService(target.Purpose, target.PerTenantKey, row.TenantId);
        var decrypted = await service.DecryptAsync(reencrypted, aad, cancellationToken).ConfigureAwait(false);
        if (decrypted.IsFailure)
            return null;

        try
        {
            var plaintext = Encoding.UTF8.GetString(decrypted.Value);
            var normalized = target.Normalize is null ? plaintext : target.Normalize(plaintext);
            return _blindIndexService.Compute(currentKey, target.Purpose, normalized, row.TenantId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted.Value);
        }
    }

    // Mirrors EncryptionInterceptor.ResolveService for the asynchronous path rotation uses — a per-tenant-keyed
    // property's ciphertext was produced under a tenant-derived subkey and can only be re-encrypted through the
    // same derivation, never the root provider directly.
    private ISymmetricEncryptionService ResolveService(string purpose, bool perTenantKey, Guid? tenantId)
    {
        if (!perTenantKey || tenantId is not { } id)
            return _encryptionService;

        var tenantKeyProvider = _keyProvider.ForPurpose($"{purpose}:tenant", id.ToByteArray());
        return new AesGcmEncryptionService(tenantKeyProvider);
    }

    private static async Task<List<RotationRow>> ReadPageAsync(
        DbConnection connection, RotationTarget target, object? after, int batchSize, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT {target.PrimaryKeyColumn}, {target.CiphertextColumn}{(target.TenantColumn is null ? "" : $", {target.TenantColumn}")}
            FROM {target.Table}
            WHERE {(after is null ? "1 = 1" : $"{target.PrimaryKeyColumn} > @after")}
            ORDER BY {target.PrimaryKeyColumn}
            {target.OrderByLimitClause(batchSize)}
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (after is not null)
            AddParameter(command, "after", after);

        var rows = new List<RotationRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var primaryKey = ReadPrimaryKey(reader, 0, target.KeyKind);
            var ciphertext = reader.IsDBNull(1) ? null : reader.GetString(1);
            Guid? tenantId = target.TenantColumn is not null && !reader.IsDBNull(2) ? reader.GetGuid(2) : null;
            rows.Add(new RotationRow(primaryKey, ciphertext, tenantId));
        }

        return rows;
    }

    private static object ReadPrimaryKey(DbDataReader reader, int ordinal, RotationKeyKind kind) => kind switch
    {
        RotationKeyKind.Guid => reader.GetGuid(ordinal),
        RotationKeyKind.Int64 => reader.GetInt64(ordinal),
        RotationKeyKind.Int32 => reader.GetInt32(ordinal),
        RotationKeyKind.String => reader.GetString(ordinal),
        _ => throw new NotSupportedException($"Unsupported {nameof(RotationKeyKind)} '{kind}'."),
    };

    // The checkpoint's textual form of a primary key value — round-trips through ParsePrimaryKeyText for the
    // SAME RotationKeyKind. Empty string is the sentinel for "no cursor yet" (start of this entity type).
    private static string FormatPrimaryKeyText(object? value, RotationKeyKind kind) => value switch
    {
        null => "",
        Guid guid => guid.ToString("D"),
        long int64 => int64.ToString(System.Globalization.CultureInfo.InvariantCulture),
        int int32 => int32.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string text => text,
        _ => throw new NotSupportedException($"Unsupported {nameof(RotationKeyKind)} '{kind}'."),
    };

    private static object? ParsePrimaryKeyText(string text, RotationKeyKind kind)
    {
        if (text.Length == 0)
            return null;

        return kind switch
        {
            RotationKeyKind.Guid => Guid.Parse(text),
            RotationKeyKind.Int64 => long.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
            RotationKeyKind.Int32 => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
            RotationKeyKind.String => text,
            _ => throw new NotSupportedException($"Unsupported {nameof(RotationKeyKind)} '{kind}'."),
        };
    }

    private static async Task<bool> CompareAndSwapAsync(
        DbConnection connection,
        RotationTarget target,
        object primaryKey,
        string? oldCiphertext,
        string newCiphertext,
        string? newBlindIndex,
        CancellationToken cancellationToken)
    {
        var setClause = target.BlindIndexColumn is null
            ? $"{target.CiphertextColumn} = @newValue"
            : $"{target.CiphertextColumn} = @newValue, {target.BlindIndexColumn} = @newBlindIndex";

        var sql = $"""
            UPDATE {target.Table}
            SET {setClause}
            WHERE {target.PrimaryKeyColumn} = @pk AND {target.CiphertextColumn} = @oldValue
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        AddParameter(command, "newValue", newCiphertext);
        AddParameter(command, "pk", primaryKey);
        AddParameter(command, "oldValue", (object?)oldCiphertext ?? DBNull.Value);
        if (target.BlindIndexColumn is not null)
            AddParameter(command, "newBlindIndex", (object?)newBlindIndex ?? DBNull.Value);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static List<RotationTarget> BuildTargets(IModel model, ISqlGenerationHelper sqlGenerationHelper)
    {
        var targets = new List<RotationTarget>();

        foreach (var entityType in model.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            var encryptedProperties = entityType.GetProperties()
                .Where(p => p.FindAnnotation(PersistenceModelAnnotationNames.Encrypt) is not null)
                .Select(p => (Property: p, ComplexPropertyName: (string?)null, ShadowNameBase: p.Name))
                .ToList();

            // EF Core 10 complex-type (value-object) sub-properties are flattened onto the SAME table as the
            // owning entity — they rotate through the identical Table/PrimaryKeyColumn/KeyKind/TenantColumn,
            // only their own CiphertextColumn (and blind-index shadow column, qualified by complex property
            // name) differ. See EncryptionModelConvention/EncryptionInterceptor's identical traversal.
            foreach (var complexProperty in entityType.GetComplexProperties())
            {
                encryptedProperties.AddRange(
                    complexProperty.ComplexType.GetProperties()
                        .Where(p => p.FindAnnotation(PersistenceModelAnnotationNames.Encrypt) is not null)
                        .Select(p => (Property: p, ComplexPropertyName: (string?)complexProperty.Name, ShadowNameBase: $"{complexProperty.Name}_{p.Name}")));
            }

            if (encryptedProperties.Count == 0)
                continue;

            var tableName = entityType.GetTableName();
            if (tableName is null)
                continue; // a view/keyless/TPC-abstract type — nothing to rotate at the storage level.

            var schema = entityType.GetSchema();
            var table = schema is null
                ? sqlGenerationHelper.DelimitIdentifier(tableName)
                : sqlGenerationHelper.DelimitIdentifier(tableName, schema);

            var primaryKey = entityType.FindPrimaryKey()
                ?? throw new InvalidOperationException($"'{entityType.ShortName()}' has an encrypted property but no primary key.");

            // Defense in depth: EncryptionModelConvention already validates this at model-build time (fails
            // before the host accepts traffic). Re-checked here too, so a model built before that validation
            // existed still fails loudly rather than silently corrupting data.
            if (primaryKey.Properties.Count != 1)
            {
                throw new NotSupportedException(
                    $"'{entityType.ShortName()}' has a composite primary key and an '.Encrypt(...)' property — " +
                    "EncryptionRotationService supports only single-column primary keys.");
            }

            var pkProperty = primaryKey.Properties[0];
            var pkProviderType = pkProperty.GetValueConverter()?.ProviderClrType ?? pkProperty.ClrType;
            var keyKind = RotationKeySupport.Classify(pkProviderType)
                ?? throw new NotSupportedException(
                    $"'{entityType.ShortName()}' has an '.Encrypt(...)' property but its primary key's provider " +
                    $"type is '{pkProviderType.Name}' — EncryptionRotationService supports only Guid, long, int " +
                    "and string primary keys.");

            string? tenantColumn = null;
            if (typeof(IHasTenant).IsAssignableFrom(entityType.ClrType))
            {
                var tenantProperty = entityType.FindProperty(nameof(IHasTenant.TenantId))
                    ?? throw new InvalidOperationException(
                        $"'{entityType.ShortName()}' implements IHasTenant but has no mapped '{nameof(IHasTenant.TenantId)}' property.");
                tenantColumn = sqlGenerationHelper.DelimitIdentifier(tenantProperty.GetColumnName());
            }

            foreach (var (property, complexPropertyName, shadowNameBase) in encryptedProperties)
            {
                var purpose = (string)property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt)!.Value!;
                var blindIndexed = property.FindAnnotation(PropertyBuilderEncryptExtensions.BlindIndexAnnotationKey)?.Value is true;
                var perTenantKey = property.FindAnnotation(PropertyBuilderEncryptExtensions.PerTenantKeyAnnotationKey)?.Value is true;
                var normalize = property.FindAnnotation(PropertyBuilderEncryptExtensions.BlindIndexNormalizeAnnotationKey)?.Value as Func<string, string>;
                var propertyPath = complexPropertyName is null ? property.Name : $"{complexPropertyName}.{property.Name}";

                string? blindIndexColumn = null;
                if (blindIndexed)
                {
                    var shadowName = shadowNameBase + "BlindIndex";
                    var shadow = entityType.FindProperty(shadowName)
                        ?? throw new InvalidOperationException(
                            $"'{entityType.ShortName()}.{propertyPath}' calls '.WithBlindIndex()' but its shadow property was not found.");
                    blindIndexColumn = sqlGenerationHelper.DelimitIdentifier(shadow.GetColumnName());
                }

                targets.Add(new RotationTarget(
                    EntityTypeName: entityType.Name,
                    PropertyPath: propertyPath,
                    Table: table,
                    PrimaryKeyColumn: sqlGenerationHelper.DelimitIdentifier(pkProperty.GetColumnName()),
                    KeyKind: keyKind,
                    CiphertextColumn: sqlGenerationHelper.DelimitIdentifier(property.GetColumnName()),
                    TenantColumn: tenantColumn,
                    BlindIndexColumn: blindIndexColumn,
                    Purpose: purpose,
                    PerTenantKey: perTenantKey,
                    Normalize: normalize));
            }
        }

        return targets;
    }

    private sealed record RotationTarget(
        string EntityTypeName,
        string PropertyPath,
        string Table,
        string PrimaryKeyColumn,
        RotationKeyKind KeyKind,
        string CiphertextColumn,
        string? TenantColumn,
        string? BlindIndexColumn,
        string Purpose,
        bool PerTenantKey,
        Func<string, string>? Normalize)
    {
        // Provider-agnostic row-count limiting clause built without relying on any provider-specific SQL
        // dialect object this package must not reference (see 06.Persistence/CLAUDE.md's "never provider-
        // specific SQL" rule for SharedKernel.Persistence.EfCore.* packages) — "LIMIT" is accepted by every
        // relational provider this platform ships (PostgreSQL today; SQL Server/SQLite also accept it via
        // their own compatibility, but this package is validated against PostgreSQL only).
        public string OrderByLimitClause(int batchSize) => $"LIMIT {batchSize}";

        // The durable checkpoint identity for this target — see EncryptionRotationCheckpoint's remarks. Two
        // different '.Encrypt(...)' properties on the SAME entity type (e.g. Email and Ssn) share EntityTypeName
        // but never PropertyPath, so this is unique across the whole model without needing anything positional.
        public string CheckpointKey => $"{EntityTypeName}::{PropertyPath}";
    }

    private sealed record RotationRow(object PrimaryKey, string? Ciphertext, Guid? TenantId);
}
