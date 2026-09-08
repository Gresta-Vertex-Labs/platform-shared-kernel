using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Tests.Signing;

/// <summary>
/// Wraps a real <see cref="RSA"/> instance, forwarding every member
/// <see cref="Signing.RsaSignatureService"/> actually calls to it, but throws if
/// <see cref="Dispose(bool)"/> is ever invoked — proving that the service never disposes the
/// instance an <see cref="Signing.IAsymmetricKeyProvider"/> hands back to it (P-493/WO-081: the
/// instance is not caller-owned).
/// </summary>
internal sealed class DisposeGuardedRsa(RSA inner) : RSA
{
    public override int KeySize
    {
        get => inner.KeySize;
        set => inner.KeySize = value;
    }

    public override RSAParameters ExportParameters(bool includePrivateParameters) =>
        inner.ExportParameters(includePrivateParameters);

    public override void ImportParameters(RSAParameters parameters) => inner.ImportParameters(parameters);

    // RSA.SignData/VerifyData are non-virtual convenience methods that hash the input and then
    // delegate to SignHash/VerifyHash — the actual overridable extension points.
    public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) =>
        inner.SignHash(hash, hashAlgorithm, padding);

    public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) =>
        inner.VerifyHash(hash, signature, hashAlgorithm, padding);

    protected override void Dispose(bool disposing) =>
        throw new InvalidOperationException(
            "RSA.Dispose() was called on a provider-returned instance. The instance returned by " +
            "IAsymmetricKeyProvider.GetRsaKeyAsync is not caller-owned and must never be disposed " +
            "by IAsymmetricSignatureService.");
}

/// <summary>
/// Wraps a real <see cref="ECDsa"/> instance, forwarding every member
/// <see cref="Signing.EcdsaSignatureService"/> actually calls to it, but throws if
/// <see cref="Dispose(bool)"/> is ever invoked — proving that the service never disposes the
/// instance an <see cref="Signing.IAsymmetricKeyProvider"/> hands back to it (P-493/WO-081: the
/// instance is not caller-owned).
/// </summary>
internal sealed class DisposeGuardedEcdsa(ECDsa inner) : ECDsa
{
    public override int KeySize
    {
        get => inner.KeySize;
        set => inner.KeySize = value;
    }

    public override ECParameters ExportParameters(bool includePrivateParameters) =>
        inner.ExportParameters(includePrivateParameters);

    public override void ImportParameters(ECParameters parameters) => inner.ImportParameters(parameters);

    public override void GenerateKey(ECCurve curve) => inner.GenerateKey(curve);

    // ECDsa.SignData/VerifyData are non-virtual convenience methods that hash the input and then
    // delegate to SignHash/VerifyHash — the actual overridable extension points.
    public override byte[] SignHash(byte[] hash) => inner.SignHash(hash);

    public override bool VerifyHash(byte[] hash, byte[] signature) => inner.VerifyHash(hash, signature);

    protected override void Dispose(bool disposing) =>
        throw new InvalidOperationException(
            "ECDsa.Dispose() was called on a provider-returned instance. The instance returned by " +
            "IAsymmetricKeyProvider.GetEcdsaKeyAsync is not caller-owned and must never be " +
            "disposed by IAsymmetricSignatureService.");
}
