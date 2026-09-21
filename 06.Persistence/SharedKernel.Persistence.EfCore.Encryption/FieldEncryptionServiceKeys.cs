namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>Keys of the keyed services field encryption registers.</summary>
public static class FieldEncryptionServiceKeys
{
    /// <summary>
    /// The key of the <c>IEncryptionKeyProviderProbe</c> that reports whether field encryption's keys are loaded
    /// and fresh.
    /// </summary>
    public const string KeyRingProbe = "SharedKernel.Persistence.EfCore.Encryption.KeyRing";
}
