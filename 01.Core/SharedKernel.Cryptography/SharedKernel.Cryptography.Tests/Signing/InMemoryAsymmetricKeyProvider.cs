using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.Tests.Signing;

/// <summary>
/// Minimal in-memory <see cref="IAsymmetricKeyProvider"/> test double backed by freshly
/// generated RSA/ECDSA key pairs per key id.
/// </summary>
internal sealed class InMemoryAsymmetricKeyProvider : IAsymmetricKeyProvider, IDisposable
{
    private readonly Dictionary<string, RSA> _rsaKeys = [];
    private readonly Dictionary<string, ECDsa> _ecdsaKeys = [];

    public RSA GetRsaKey(string keyId)
    {
        if (_rsaKeys.TryGetValue(keyId, out RSA? existing))
        {
            return RSA.Create(existing.ExportParameters(true));
        }

        RSA created = RSA.Create(2048);
        _rsaKeys[keyId] = created;
        return RSA.Create(created.ExportParameters(true));
    }

    public ECDsa GetEcdsaKey(string keyId)
    {
        if (_ecdsaKeys.TryGetValue(keyId, out ECDsa? existing))
        {
            return ECDsa.Create(existing.ExportParameters(true));
        }

        ECDsa created = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _ecdsaKeys[keyId] = created;
        return ECDsa.Create(created.ExportParameters(true));
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
