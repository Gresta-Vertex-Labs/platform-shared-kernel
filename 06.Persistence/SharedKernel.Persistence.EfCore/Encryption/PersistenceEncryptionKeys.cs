namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Package-internal keyed-DI service keys isolating this package's own persistence-scoped
/// encryption pipeline from any unrelated general-purpose key provider or encryption service
/// registration elsewhere in the same container.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Never a public contract.</strong> These keys exist purely so
/// <c>EfCorePersistenceBuilder&lt;TContext&gt;.WithEncryption()</c>/
/// <c>.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c> and
/// <see cref="Context.SharedKernelDbContext"/> can find each other's registrations without ever
/// touching the ambient, UNKEYED slots — the ones a general-purpose <c>AddSharedKernelCryptography()</c> call,
/// or <c>13.ServiceDefaults</c>'s Key Vault key provider registration, might also populate for entirely unrelated
/// reasons. Not exposed outside this assembly.
/// </para>
/// <para>
/// <c>.WithEncryption()</c> registers <see cref="EncryptionOptionsKeyProvider"/> under
/// <see cref="EncryptionKeyProviderKey"/> and a <c>SynchronousAesGcmEncryptionService</c> over whatever that slot
/// resolves to under <see cref="SymmetricEncryptionServiceKey"/>. <c>.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c>
/// re-registers only <see cref="EncryptionKeyProviderKey"/>, pointing at <see cref="PreWarmedEncryptionKeyProvider"/> —
/// the .NET DI container resolves the LAST registration for a given (service type, key) pair, so the external
/// provider always wins once both are present, by design.
/// </para>
/// </remarks>
internal static class PersistenceEncryptionKeys
{
    /// <summary>
    /// The keyed-DI slot for this package's own
    /// <see cref="SharedKernel.Cryptography.Symmetric.ISynchronousEncryptionKeyProvider"/>.
    /// </summary>
    internal const string EncryptionKeyProviderKey = "SharedKernel.Persistence.EfCore:Encryption:ISynchronousEncryptionKeyProvider";

    /// <summary>
    /// The keyed-DI slot for this package's own
    /// <see cref="SharedKernel.Cryptography.Symmetric.ISynchronousSymmetricEncryptionService"/> — resolved by
    /// <see cref="Context.SharedKernelDbContext"/> in preference to its constructor-supplied
    /// <c>symmetricEncryptionService</c> parameter.
    /// </summary>
    internal const string SymmetricEncryptionServiceKey = "SharedKernel.Persistence.EfCore:Encryption:ISynchronousSymmetricEncryptionService";
}
