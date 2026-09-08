using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Tests.Signing;

/// <summary>
/// Wraps a real, otherwise-valid <see cref="RSA"/> instance but reports an artificially small
/// <see cref="KeySize"/> — used to exercise <c>RsaSignatureService</c>'s minimum-key-size gate
/// (P-493/WO-081) without needing to construct a genuinely undersized RSA key. Because the gate
/// throws before <c>SignData</c>/<c>VerifyData</c> is ever reached, the wrapped key's actual
/// cryptographic operations are never exercised in these tests.
/// </summary>
internal sealed class RsaWithKeySize(RSA inner, int fakeKeySize) : RSA
{
    public override int KeySize
    {
        get => fakeKeySize;
        set => inner.KeySize = value;
    }

    public override RSAParameters ExportParameters(bool includePrivateParameters) =>
        inner.ExportParameters(includePrivateParameters);

    public override void ImportParameters(RSAParameters parameters) => inner.ImportParameters(parameters);

    public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) =>
        inner.SignHash(hash, hashAlgorithm, padding);

    public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) =>
        inner.VerifyHash(hash, signature, hashAlgorithm, padding);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Wraps a real, otherwise-valid <see cref="ECDsa"/> instance but reports an artificially small
/// <see cref="KeySize"/> — used to exercise <c>EcdsaSignatureService</c>'s minimum-key-size gate
/// (P-493/WO-081) without needing to construct a curve smaller than P-256. Because the gate
/// throws before <c>SignData</c>/<c>VerifyData</c> is ever reached, the wrapped key's actual
/// cryptographic operations are never exercised in these tests.
/// </summary>
internal sealed class EcdsaWithKeySize(ECDsa inner, int fakeKeySize) : ECDsa
{
    public override int KeySize
    {
        get => fakeKeySize;
        set => inner.KeySize = value;
    }

    public override ECParameters ExportParameters(bool includePrivateParameters) =>
        inner.ExportParameters(includePrivateParameters);

    public override void ImportParameters(ECParameters parameters) => inner.ImportParameters(parameters);

    public override void GenerateKey(ECCurve curve) => inner.GenerateKey(curve);

    public override byte[] SignHash(byte[] hash) => inner.SignHash(hash);

    public override bool VerifyHash(byte[] hash, byte[] signature) => inner.VerifyHash(hash, signature);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
