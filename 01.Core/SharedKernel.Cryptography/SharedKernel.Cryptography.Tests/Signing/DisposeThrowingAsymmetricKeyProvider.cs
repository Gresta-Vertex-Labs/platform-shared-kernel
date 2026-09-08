using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.Tests.Signing;

/// <summary>
/// Returns the SAME cached, dispose-guarded RSA/ECDSA instance on every call for a given key
/// kind — used by T-70 (P-493/WO-081) to prove that <see cref="Signing.RsaSignatureService"/>/
/// <see cref="Signing.EcdsaSignatureService"/> never dispose the instance this provider hands
/// back. Implements <see cref="ISynchronousAsymmetricKeyProvider"/> so both the sync and async
/// signing paths can be exercised against it.
/// </summary>
internal sealed class DisposeThrowingAsymmetricKeyProvider : ISynchronousAsymmetricKeyProvider, IDisposable
{
    private readonly RSA _innerRsa = RSA.Create(2048);
    private readonly ECDsa _innerEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly DisposeGuardedRsa _rsa;
    private readonly DisposeGuardedEcdsa _ecdsa;

    public DisposeThrowingAsymmetricKeyProvider()
    {
        _rsa = new DisposeGuardedRsa(_innerRsa);
        _ecdsa = new DisposeGuardedEcdsa(_innerEcdsa);
    }

    public ValueTask<RSA> GetRsaKeyAsync(string keyId, CancellationToken ct = default) => new(_rsa);

    public ValueTask<ECDsa> GetEcdsaKeyAsync(string keyId, CancellationToken ct = default) => new(_ecdsa);

    /// <summary>
    /// Disposes the REAL underlying instances directly, bypassing the dispose-guard wrappers
    /// (which would otherwise throw) — this is test cleanup, not a call the services under test
    /// make.
    /// </summary>
    public void Dispose()
    {
        _innerRsa.Dispose();
        _innerEcdsa.Dispose();
    }
}
