using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Persistence.EfCore.Encryption.Maintenance;

/// <summary>The decoded shape of an <see cref="IEncryptionRotationJob"/> checkpoint token.</summary>
/// <param name="CompletedTargetKeys">
/// the <c>"{schema}.{table}.{column}"</c> key of each maintenance target
/// for every rotation target already fully scanned in a previous call. Identified BY NAME, never by position in
/// the target list — the list is rebuilt from the live model on every call, so a target's ordinal position shifts
/// whenever an <c>.Encrypt(...)</c> property is added to or removed from the model between two calls, while its
/// name does not. A target whose key is not in this set and does not equal <see cref="InProgressTargetKey"/> is
/// treated as not yet started, regardless of where it now sits in the (re-sorted) list — including a target that
/// did not exist at all when this checkpoint was produced.
/// </param>
/// <param name="InProgressTargetKey">
/// The <c>CheckpointKey</c> of the one target that was partway through when this checkpoint was produced, or
/// <see langword="null"/> if none was (a checkpoint taken exactly between two targets). Looked up by name in the
/// CURRENT target list on resume; if that name is no longer present (its <c>.Encrypt(...)</c> was removed), there
/// is nothing to resume for it and it is simply never reached again.
/// </param>
/// <param name="LastPrimaryKeyText">
/// The last primary key's textual form (see the maintenance job) for
/// <see cref="InProgressTargetKey"/> — resume strictly after this row. Empty string means "no row read yet for
/// this target". Meaningless when <see cref="InProgressTargetKey"/> is <see langword="null"/>.
/// </param>
internal sealed record EncryptionRotationCheckpoint(
    [property: JsonPropertyName("c")] IReadOnlyList<string> CompletedTargetKeys,
    [property: JsonPropertyName("t")] string? InProgressTargetKey,
    [property: JsonPropertyName("k")] string LastPrimaryKeyText)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = null };

    public string Encode() => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this, SerializerOptions));

    public static EncryptionRotationCheckpoint Decode(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        try
        {
            var bytes = Convert.FromBase64String(token);
            return JsonSerializer.Deserialize<EncryptionRotationCheckpoint>(bytes, SerializerOptions)
                ?? throw new FormatException("Checkpoint token decoded to null.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new ArgumentException($"'{nameof(token)}' is not a valid encryption rotation checkpoint token.", nameof(token), ex);
        }
    }
}
