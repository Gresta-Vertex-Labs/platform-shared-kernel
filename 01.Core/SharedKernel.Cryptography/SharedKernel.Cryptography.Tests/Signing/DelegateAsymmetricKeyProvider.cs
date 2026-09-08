using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.Tests.Signing;

/// <summary>
/// A minimal, genuinely synchronous <see cref="IAsymmetricKeyProvider"/> test double that
/// delegates key resolution to caller-supplied factory functions — used where a scenario needs
/// full control over the exact <see cref="RSA"/>/<see cref="ECDsa"/> instance returned (e.g. the
/// key-size-override wrappers in <see cref="RsaWithKeySize"/>/<see cref="EcdsaWithKeySize"/>).
/// </summary>
internal sealed class DelegateAsymmetricKeyProvider(
    Func<string, RSA>? rsaFactory = null,
    Func<string, ECDsa>? ecdsaFactory = null) : ISynchronousAsymmetricKeyProvider
{
    public ValueTask<RSA> GetRsaKeyAsync(string keyId, CancellationToken ct = default) =>
        new(rsaFactory is not null ? rsaFactory(keyId) : throw new KeyNotFoundException(keyId));

    public ValueTask<ECDsa> GetEcdsaKeyAsync(string keyId, CancellationToken ct = default) =>
        new(ecdsaFactory is not null ? ecdsaFactory(keyId) : throw new KeyNotFoundException(keyId));
}
