using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.Tests.Signing;

/// <summary>
/// Wraps an inner <see cref="IAsymmetricKeyProvider"/> but deliberately implements only the
/// plain, unmarked interface — never <see cref="ISynchronousAsymmetricKeyProvider"/> — so tests
/// can exercise <see cref="RsaSignatureService"/>/<see cref="EcdsaSignatureService"/>'s
/// <see cref="NotSupportedException"/> gating (P-493/WO-081) regardless of whether the wrapped
/// provider would itself have been safe to mark.
/// </summary>
internal sealed class NonSynchronousAsymmetricKeyProvider(IAsymmetricKeyProvider inner) : IAsymmetricKeyProvider
{
    public ValueTask<RSA> GetRsaKeyAsync(string keyId, CancellationToken ct = default) =>
        inner.GetRsaKeyAsync(keyId, ct);

    public ValueTask<ECDsa> GetEcdsaKeyAsync(string keyId, CancellationToken ct = default) =>
        inner.GetEcdsaKeyAsync(keyId, ct);
}
