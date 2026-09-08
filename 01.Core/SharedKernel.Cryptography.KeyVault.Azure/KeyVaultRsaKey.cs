using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys.Cryptography;
// AsymmetricAlgorithm (RSA's base class) declares an unrelated instance `string SignatureAlgorithm`
// property, which shadows the Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm TYPE
// name for unqualified identifier resolution inside a class derived from RSA. An alias sidesteps
// the ambiguity instead of fully qualifying every reference.
using AzureSignatureAlgorithm = Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>
/// A thin <see cref="RSA"/> subclass returned by <see cref="AzureKeyVaultAsymmetricKeyProvider.GetRsaKeyAsync"/>
/// whose sign/verify operations are performed <b>remotely</b> by Azure Key Vault — this instance
/// never holds private key material.
/// </summary>
/// <remarks>
/// <para>
/// <b>OVERRIDES <c>SignHash</c>/<c>VerifyHash</c>, NOT <c>SignData</c>/<c>VerifyData</c>.</b>
/// <see cref="RSA.SignData(byte[], HashAlgorithmName, RSASignaturePadding)"/> and
/// <see cref="RSA.VerifyData(byte[], byte[], HashAlgorithmName, RSASignaturePadding)"/> are
/// <b>non-virtual</b> convenience methods that hash the input locally and then call
/// <see cref="SignHash"/>/<see cref="VerifyHash"/> internally — the real overridable BCL extension
/// points. Attempting <c>override</c> on <c>SignData</c>/<c>VerifyData</c> directly fails to
/// compile (CS0506); this was discovered during P-493's own test-double implementation (see
/// <c>SharedKernel.Cryptography.Tests/Signing/DisposeGuardedKeys.cs</c>). Because
/// <see cref="Signing.RsaSignatureService"/> calls the convenience methods, and those internally
/// route to <see cref="SignHash"/>/<see cref="VerifyHash"/> below, this class is consumed through
/// the exact same public <see cref="RSA"/> contract as any local key — <b>zero changes to
/// <see cref="Signing.RsaSignatureService"/> were needed beyond what P-493 already introduced</b>.
/// </para>
/// <para>
/// <b>NARROW, FIXED ALGORITHM MAPPING — NOT A GENERAL-PURPOSE NEGOTIATION SURFACE.</b> This class
/// exists to serve exactly the one fixed <c>(SHA-256, PSS)</c> combination
/// <see cref="Signing.RsaSignatureService"/> always requests, mapped to Azure's
/// <see cref="AzureSignatureAlgorithm.PS256"/>. Any other <see cref="HashAlgorithmName"/>/
/// <see cref="RSASignaturePadding"/> combination throws <see cref="NotSupportedException"/>
/// rather than silently guessing a different Azure algorithm.
/// </para>
/// <para>
/// <b>SIGNING AND VERIFICATION PERFORM A REAL, UNAVOIDABLE BLOCKING NETWORK CALL TO AZURE KEY
/// VAULT.</b> Azure's SDK offers a genuine <em>synchronous</em>
/// <see cref="CryptographyClient.Sign(AzureSignatureAlgorithm, byte[], System.Threading.CancellationToken)"/>/
/// <see cref="CryptographyClient.Verify(AzureSignatureAlgorithm, byte[], byte[], System.Threading.CancellationToken)"/>
/// pair — this is not a <c>.GetAwaiter().GetResult()</c> bridge over the async members, since
/// <see cref="RSA"/>'s BCL surface gives this class no async entry point to bridge from in the
/// first place (<see cref="Signing.IAsymmetricSignatureService.SignAsync"/>'s own P-493 remarks
/// state this limitation explicitly: only <em>key resolution</em> is genuinely asynchronous). The
/// call is still synchronous network I/O either way — it blocks the calling thread for the
/// duration of the round trip to Key Vault, on every single call, with no local fast path.
/// </para>
/// <para>
/// <see cref="ExportParameters"/>/<see cref="ImportParameters"/> both throw
/// <see cref="NotSupportedException"/> — private key material never crosses the process boundary,
/// mirroring <see cref="AzureKeyVaultEncryptionKeyProvider"/>'s equivalent invariant for the vault's
/// master key material.
/// </para>
/// </remarks>
internal sealed class KeyVaultRsaKey : RSA
{
    private static readonly HashAlgorithmName ExpectedHashAlgorithm = HashAlgorithmName.SHA256;
    private static readonly RSASignaturePadding ExpectedPadding = RSASignaturePadding.Pss;

    private readonly CryptographyClient _cryptographyClient;
    private readonly int _keySizeBits;

    internal KeyVaultRsaKey(CryptographyClient cryptographyClient, int keySizeBits)
    {
        _cryptographyClient = cryptographyClient;
        _keySizeBits = keySizeBits;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Fixed by the underlying Azure Key Vault key at resolution time — the setter throws
    /// <see cref="NotSupportedException"/>, since this instance cannot change the vault key's size.
    /// </remarks>
    public override int KeySize
    {
        get => _keySizeBits;
        set => throw new NotSupportedException(
            $"{nameof(KeyVaultRsaKey)}'s key size is fixed by the underlying Azure Key Vault key " +
            "and cannot be changed locally.");
    }

    /// <summary>
    /// Performs a REAL, BLOCKING NETWORK CALL to Azure Key Vault. See the type-level remarks.
    /// </summary>
    public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(hashAlgorithm.Name);
        ArgumentNullException.ThrowIfNull(padding);

        AzureSignatureAlgorithm algorithm = ResolveSignatureAlgorithm(hashAlgorithm, padding);

        // REAL, BLOCKING NETWORK CALL — see type-level remarks.
        SignResult result = _cryptographyClient.Sign(algorithm, hash);
        return result.Signature;
    }

    /// <summary>
    /// Performs a REAL, BLOCKING NETWORK CALL to Azure Key Vault. See the type-level remarks.
    /// </summary>
    public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(hashAlgorithm.Name);
        ArgumentNullException.ThrowIfNull(padding);

        AzureSignatureAlgorithm algorithm = ResolveSignatureAlgorithm(hashAlgorithm, padding);

        // REAL, BLOCKING NETWORK CALL — see type-level remarks.
        VerifyResult result = _cryptographyClient.Verify(algorithm, hash, signature);
        return result.IsValid;
    }

    /// <summary>
    /// Private key material never crosses the process boundary — always throws.
    /// </summary>
    public override RSAParameters ExportParameters(bool includePrivateParameters) =>
        throw new NotSupportedException(
            $"{nameof(KeyVaultRsaKey)} never exports key material — the underlying Azure Key " +
            "Vault key's private material never crosses the process boundary, and this class is " +
            "not a general-purpose local RSA key.");

    /// <summary>
    /// This instance is bound to a specific, already-provisioned Azure Key Vault key — always
    /// throws.
    /// </summary>
    public override void ImportParameters(RSAParameters parameters) =>
        throw new NotSupportedException(
            $"{nameof(KeyVaultRsaKey)} cannot import key material — it is permanently bound to " +
            "the Azure Key Vault key it was resolved for.");

    private static AzureSignatureAlgorithm ResolveSignatureAlgorithm(HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        if (hashAlgorithm == ExpectedHashAlgorithm && padding == ExpectedPadding)
        {
            return AzureSignatureAlgorithm.PS256;
        }

        throw new NotSupportedException(
            $"{nameof(KeyVaultRsaKey)} only supports SHA-256 + PSS padding (mapped to Azure's " +
            $"{nameof(AzureSignatureAlgorithm.PS256)}) — the fixed combination {nameof(Signing.RsaSignatureService)} " +
            $"always requests. Requested: {hashAlgorithm.Name} + {padding}.");
    }
}
