namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Marker options used exclusively to trigger the <see cref="EncryptionStartupValidator"/> at
/// application startup via <c>ValidateOnStart()</c> when <c>WithEncryption()</c> was called.
/// </summary>
internal sealed class EncryptionStartupOptions
{
}
