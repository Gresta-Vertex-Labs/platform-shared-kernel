using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Validates <see cref="AuditLedgerOptions"/> at startup: a well-formed keyring whose current key is the
/// newest, distinct key orders, and sane sealer settings.
/// </summary>
internal sealed class AuditLedgerOptionsValidator : IValidateOptions<AuditLedgerOptions>
{
    /// <summary>The minimum decoded length of a sealing key, in bytes.</summary>
    public const int MinimumKeyLengthBytes = 32;

    /// <summary>The largest accepted <see cref="AuditSealerOptions.BatchSize"/>.</summary>
    public const int MaxBatchSize = 5000;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AuditLedgerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!options.UsesCustomAuthenticator)
            ValidateKeyring(options, failures);

        var sealer = options.Sealer ?? new AuditSealerOptions();
        if (sealer.Interval <= TimeSpan.Zero)
            failures.Add($"{nameof(AuditLedgerOptions.Sealer)}:{nameof(AuditSealerOptions.Interval)} must be positive.");
        if (sealer.BatchSize is < 1 or > MaxBatchSize)
            failures.Add($"{nameof(AuditLedgerOptions.Sealer)}:{nameof(AuditSealerOptions.BatchSize)} must be between 1 and {MaxBatchSize}.");
        if (sealer.CheckpointInterval <= TimeSpan.Zero)
            failures.Add($"{nameof(AuditLedgerOptions.Sealer)}:{nameof(AuditSealerOptions.CheckpointInterval)} must be positive.");

        if (!Enum.IsDefined(options.SelfCheck))
            failures.Add($"{nameof(AuditLedgerOptions.SelfCheck)} '{options.SelfCheck}' is not defined.");

        if (options.AcceptedCheckpointSigningKeyIds.Any(string.IsNullOrWhiteSpace))
            failures.Add($"{nameof(AuditLedgerOptions.AcceptedCheckpointSigningKeyIds)} must not contain blank entries.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateKeyring(AuditLedgerOptions options, List<string> failures)
    {
        if (options.Keys.Count == 0)
        {
            failures.Add(
                $"{nameof(AuditLedgerOptions.Keys)} is empty. Configure at least one sealing key " +
                $"({AuditLedgerOptions.SectionName}:{nameof(AuditLedgerOptions.Keys)}:<id>:{nameof(AuditKeyOptions.Material)}) " +
                $"or register a custom {nameof(IAuditRecordAuthenticator)} before UseAuditTrail().");
            return;
        }

        var orders = new Dictionary<int, string>();
        foreach (var (id, key) in options.Keys)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                failures.Add("A sealing key has a blank id.");
                continue;
            }

            if (key is null || string.IsNullOrWhiteSpace(key.Material))
            {
                failures.Add($"Sealing key '{id}' has no {nameof(AuditKeyOptions.Material)}.");
                continue;
            }

            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(key.Material);
            }
            catch (FormatException)
            {
                failures.Add($"Sealing key '{id}': {nameof(AuditKeyOptions.Material)} is not valid Base64.");
                continue;
            }

            if (decoded.Length < MinimumKeyLengthBytes)
                failures.Add($"Sealing key '{id}' decodes to {decoded.Length} bytes; at least {MinimumKeyLengthBytes} are required.");

            if (!orders.TryAdd(key.Order, id))
                failures.Add($"Sealing keys '{orders[key.Order]}' and '{id}' share {nameof(AuditKeyOptions.Order)} {key.Order}; every key needs a distinct order.");
        }

        if (string.IsNullOrWhiteSpace(options.CurrentKeyId))
        {
            failures.Add($"{nameof(AuditLedgerOptions.CurrentKeyId)} is required.");
            return;
        }

        if (!options.Keys.TryGetValue(options.CurrentKeyId, out var current) || current is null)
        {
            failures.Add($"{nameof(AuditLedgerOptions.CurrentKeyId)} '{options.CurrentKeyId}' is not one of the configured {nameof(AuditLedgerOptions.Keys)}.");
            return;
        }

        var newest = options.Keys.Where(k => k.Value is not null).Max(k => k.Value.Order);
        if (current.Order < newest)
        {
            failures.Add(
                $"{nameof(AuditLedgerOptions.CurrentKeyId)} '{options.CurrentKeyId}' has order {current.Order}, but a key with order {newest} exists. " +
                "The current key must be the newest; sealing under an older key would be reported as a key regression.");
        }
    }
}
