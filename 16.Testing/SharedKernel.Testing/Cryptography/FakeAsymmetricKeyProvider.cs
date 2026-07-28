using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="IAsymmetricKeyProvider"/>, backed by freshly generated
/// RSA/ECDSA key pairs per key id.
/// </summary>
/// <remarks>
/// Promoted from <c>SharedKernel.Cryptography.Tests</c>' internal <c>InMemoryAsymmetricKeyProvider</c>
/// test double into this package's public, shared surface (zero behavioral drift; hardened here for
/// thread safety since fakes in this package may be shared across parallel xUnit collections). Every
/// call to <see cref="GetRsaKey"/>/<see cref="GetEcdsaKey"/> returns a FRESH handle cloned via
/// <c>ExportParameters(true)</c>/<c>RSA.Create(...)</c>/<c>ECDsa.Create(...)</c> so the caller's
/// <c>using</c>/<see cref="Dispose"/> never invalidates the cached original key pair.
/// </remarks>
public sealed class FakeAsymmetricKeyProvider : IAsymmetricKeyProvider, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RSA> _rsaKeys = [];
    private readonly Dictionary<string, ECDsa> _ecdsaKeys = [];

    /// <inheritdoc />
    public RSA GetRsaKey(string keyId)
    {
        lock (_gate)
        {
            if (_rsaKeys.TryGetValue(keyId, out RSA? existing))
            {
                return RSA.Create(existing.ExportParameters(true));
            }

            RSA created = RSA.Create(2048);
            _rsaKeys[keyId] = created;
            return RSA.Create(created.ExportParameters(true));
        }
    }

    /// <inheritdoc />
    public ECDsa GetEcdsaKey(string keyId)
    {
        lock (_gate)
        {
            if (_ecdsaKeys.TryGetValue(keyId, out ECDsa? existing))
            {
                return ECDsa.Create(existing.ExportParameters(true));
            }

            ECDsa created = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _ecdsaKeys[keyId] = created;
            return ECDsa.Create(created.ExportParameters(true));
        }
    }

    /// <summary>Disposes every cached RSA/ECDSA key pair.</summary>
    public void Dispose()
    {
        lock (_gate)
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
}
