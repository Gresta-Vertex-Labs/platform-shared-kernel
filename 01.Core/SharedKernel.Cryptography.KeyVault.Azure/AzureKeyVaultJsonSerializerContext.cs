using System.Text.Json.Serialization;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for
/// <see cref="VersionSecretPayload"/> — used to serialize/deserialize each data-key version's Key
/// Vault Secret value without reflection-based System.Text.Json serialization, per this platform's
/// AOT-preferred guidance.
/// </summary>
[JsonSerializable(typeof(VersionSecretPayload))]
internal sealed partial class AzureKeyVaultJsonSerializerContext : JsonSerializerContext;
