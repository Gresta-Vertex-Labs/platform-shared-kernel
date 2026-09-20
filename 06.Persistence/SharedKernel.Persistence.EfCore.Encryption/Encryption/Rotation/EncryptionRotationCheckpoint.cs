using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Persistence.EfCore.Encryption.Rotation;

/// <summary>The decoded shape of an <see cref="IEncryptionRotationJob"/> checkpoint token.</summary>
/// <param name="EntityTypeIndex">
/// Index into the alphabetically sorted list of entity type full names that have at least one encrypted property —
/// which entity type to resume scanning.
/// </param>
/// <param name="LastPrimaryKeyText">
/// The last primary key's textual form (see <c>EncryptionRotationService{TContext}.FormatPrimaryKeyText</c>) —
/// resume strictly after this row. Empty string means "no row read yet for this entity type".
/// </param>
internal sealed record EncryptionRotationCheckpoint(
    [property: JsonPropertyName("i")] int EntityTypeIndex,
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
