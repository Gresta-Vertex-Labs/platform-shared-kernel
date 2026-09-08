using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.Tests.Signing;

/// <summary>
/// Minimal in-memory <see cref="IAsymmetricKeyProvider"/> test double backed by freshly
/// generated RSA/ECDSA key pairs per key id. Genuinely never performs I/O, so it implements
/// <see cref="ISynchronousAsymmetricKeyProvider"/> — this is the default double used by the
/// sync-member round-trip tests (P-493/WO-081).
/// </summary>
internal sealed class InMemoryAsymmetricKeyProvider : ISynchronousAsymmetricKeyProvider, IDisposable
{
    private readonly Dictionary<string, RSA> _rsaKeys = [];
    private readonly Dictionary<string, ECDsa> _ecdsaKeys = [];

    public ValueTask<RSA> GetRsaKeyAsync(string keyId, CancellationToken ct = default)
    {
        if (_rsaKeys.TryGetValue(keyId, out RSA? existing))
        {
            return new ValueTask<RSA>(RSA.Create(existing.ExportParameters(true)));
        }

        RSA created = RSA.Create(2048);
        _rsaKeys[keyId] = created;
        return new ValueTask<RSA>(RSA.Create(created.ExportParameters(true)));
    }

    public ValueTask<ECDsa> GetEcdsaKeyAsync(string keyId, CancellationToken ct = default)
    {
        if (_ecdsaKeys.TryGetValue(keyId, out ECDsa? existing))
        {
            return new ValueTask<ECDsa>(ECDsa.Create(existing.ExportParameters(true)));
        }

        ECDsa created = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _ecdsaKeys[keyId] = created;
        return new ValueTask<ECDsa>(ECDsa.Create(created.ExportParameters(true)));
    }

    public void Dispose()
    {
        foreach (RSA rsa in _rsaKeys.Values)
        {
            rsa.Dispose();
        }

        foreach (ECDsa ecdsa in _ecdsaKeys.Values)
        {
            ecdsa.Dispose();
        }
    }
}
