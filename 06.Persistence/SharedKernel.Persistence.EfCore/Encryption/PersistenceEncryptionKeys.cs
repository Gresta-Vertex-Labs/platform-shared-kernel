namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Package-internal keyed-DI service keys isolating this package's own persistence-scoped
/// encryption pipeline from any unrelated general-purpose <c>IEncryptionKeyProvider</c>/
/// <c>ISymmetricEncryptionService</c> registration elsewhere in the same container (D-131,
/// P-498/WO-081).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Never a public contract.</strong> These keys exist purely so
/// <c>EfCorePersistenceBuilder&lt;TContext&gt;.WithEncryption()</c>/
/// <c>.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c> and
/// <see cref="Context.SharedKernelDbContext"/> can find each other's registrations without ever
/// touching the ambient, UNKEYED <c>IEncryptionKeyProvider</c>/<c>ISymmetricEncryptionService</c>
/// slot — the exact slot a general-purpose <c>AddSharedKernelCryptography()</c> call, or
/// <c>13.ServiceDefaults</c>'s <c>AddSharedKernelKeyVaultKeyProvider</c>, might also populate for
/// entirely unrelated reasons. Not exposed outside this assembly — a consuming service never
/// resolves against these keys directly.
/// </para>
/// <para>
/// Both keys are registered TWICE across the two supported paths — once by
/// <c>.WithEncryption()</c> (the config-backed default, backed by
/// <see cref="EncryptionOptionsKeyProvider"/>) and, when
/// <c>.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c> is additionally called (KMS-backed
/// mode, backed by <see cref="PreWarmedEncryptionKeyProvider"/>), a SECOND time — the .NET DI
/// container resolves the LAST registration for a given (service type, key) pair, so the
/// external-provider registration always wins once both are present, by design.
/// </para>
/// </remarks>
internal static class PersistenceEncryptionKeys
{
    /// <summary>
    /// The keyed-DI slot for this package's own persistence-scoped
    /// <see cref="SharedKernel.Cryptography.Symmetric.IEncryptionKeyProvider"/> — never the same
    /// slot a general-purpose <c>SharedKernel.Cryptography</c> registration uses.
    /// </summary>
    internal const string EncryptionKeyProviderKey = "SharedKernel.Persistence.EfCore:Encryption:IEncryptionKeyProvider";

    /// <summary>
    /// The keyed-DI slot for this package's own persistence-scoped
    /// <see cref="SharedKernel.Cryptography.Symmetric.ISymmetricEncryptionService"/> — resolved by
    /// <see cref="Context.SharedKernelDbContext"/> when its constructor-supplied
    /// <c>symmetricEncryptionService</c> parameter is not explicitly overridden by the caller.
    /// </summary>
    internal const string SymmetricEncryptionServiceKey = "SharedKernel.Persistence.EfCore:Encryption:ISymmetricEncryptionService";
}
