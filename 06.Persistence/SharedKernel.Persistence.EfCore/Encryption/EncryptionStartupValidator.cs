using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Validates at application startup (P-227) that this package's persistence-scoped encryption
/// pipeline is soundly wired when <c>EfCorePersistenceBuilder.WithEncryption()</c> was called.
/// </summary>
/// <remarks>
/// <para>
/// <strong>D-132/P-498/WO-081 (corrected):</strong> this package now constructs its own
/// persistence-scoped <see cref="ISymmetricEncryptionService"/> directly — it never resolves the
/// ambient, unkeyed <c>ISymmetricEncryptionService</c>/<see cref="IEncryptionKeyProvider"/> slot
/// anymore (D-131), so the original "is <c>ISymmetricEncryptionService</c> resolvable" check no
/// longer applies. Instead this validator checks the ONE thing that genuinely varies by path: that
/// SOME <see cref="IEncryptionKeyProvider"/> is registered under this package's own keyed-DI slot
/// (<see cref="PersistenceEncryptionKeys.EncryptionKeyProviderKey"/>) — self-contained and always
/// true for the config-backed default (<see cref="EncryptionOptionsKeyProvider"/> depends on
/// nothing this package doesn't already register itself) and for
/// <c>.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c> (as long as the consumer separately
/// registered <c>TProvider</c>, which <see cref="PreWarmedEncryptionKeyProvider"/>'s own
/// registration factory requires) — plus a defense-in-depth
/// <see cref="EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(IEncryptionKeyProvider)"/>
/// check against whatever is found there. Given D-127 (the config-backed default is marked
/// directly) and D-129 (<see cref="PreWarmedEncryptionKeyProvider"/> earns the marker honestly by
/// construction), the second check should be UNREACHABLE-FALSE on every path this package itself
/// wires through the builder — it exists only to catch a hypothetical future regression, e.g. a
/// maintainer bypassing the builder and registering an unmarked provider directly under this
/// package's keyed-DI slot.
/// </para>
/// </remarks>
internal sealed class EncryptionStartupValidator : IValidateOptions<EncryptionStartupOptions>
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Initialises a new <see cref="EncryptionStartupValidator"/>.</summary>
    /// <param name="serviceProvider">The root DI service provider.</param>
    public EncryptionStartupValidator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, EncryptionStartupOptions options)
    {
        var keyProvider = _serviceProvider.GetKeyedService<IEncryptionKeyProvider>(
            PersistenceEncryptionKeys.EncryptionKeyProviderKey);

        if (keyProvider is null)
        {
            return ValidateOptionsResult.Fail(
                "EfCorePersistenceBuilder.WithEncryption() was called, but no IEncryptionKeyProvider " +
                "is registered under this package's internal keyed-DI slot. This should be " +
                "unreachable through the builder itself — always configure field-level encryption via " +
                ".WithEncryption(...) (config-backed default) or " +
                ".WithEncryption(...).WithExternalEncryptionKeyProvider<TProvider>() (KMS-backed) " +
                "rather than constructing the encryption pipeline by hand.");
        }

        if (!EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(keyProvider))
        {
            return ValidateOptionsResult.Fail(
                $"The IEncryptionKeyProvider wired into this package's persistence-scoped " +
                $"ISymmetricEncryptionService ('{keyProvider.GetType().Name}') does not implement " +
                "ISynchronousEncryptionKeyProvider, so its retained synchronous " +
                "Encrypt/Decrypt/EncryptToString/DecryptToString members would throw " +
                "NotSupportedException on every call. Use .WithEncryption(...) alone (the " +
                "config-backed default, honestly marked ISynchronousEncryptionKeyProvider) or " +
                ".WithExternalEncryptionKeyProvider<TProvider>() (whose PreWarmedEncryptionKeyProvider " +
                "wrapper earns the marker honestly by construction) — both sanctioned paths guarantee " +
                "this check passes.");
        }

        return ValidateOptionsResult.Success;
    }
}
