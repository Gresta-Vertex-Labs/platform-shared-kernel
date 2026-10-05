using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Options;

namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// The default <see cref="IOneWayHasher"/>: hashes with the configured <see cref="IOneWayHashAlgorithm"/> and
/// verifies with whichever registered algorithm produced the stored hash.
/// </summary>
/// <remarks>
/// <para>
/// <b>Encoding.</b> The secret is normalized to Unicode NFKC and encoded as UTF-8. When
/// <see cref="OneWayHashingOptions.CurrentPepperId"/> is set, the encoded secret is replaced by
/// HMAC-SHA256(pepper, secret) and the pepper id is stored as the <c>k</c> parameter of the hash.
/// </para>
/// <para>
/// <b>Legacy hashes.</b> The pre-release binary PBKDF2 format is verified over the raw UTF-8 secret, as it was
/// produced, and always reports a rehash.
/// </para>
/// <para>
/// <b>Rehash.</b> <see cref="Verify"/> returns <see cref="HashVerificationResult.SuccessRehashNeeded"/> when the
/// stored hash uses another algorithm than <see cref="OneWayHashingOptions.Algorithm"/>, another cost, another pepper
/// (or none where one is now configured), or the pre-release binary format.
/// </para>
/// </remarks>
public sealed class OneWayHasher : IOneWayHasher
{
    private const string PepperParameter = "k";

    private readonly Dictionary<string, IOneWayHashAlgorithm> _algorithms;
    private readonly IOptionsMonitor<CryptographyOptions> _options;

    /// <summary>Creates the hasher.</summary>
    /// <param name="algorithms">Every registered algorithm. Identifiers must be unique.</param>
    /// <param name="options">Supplies the algorithm and pepper settings.</param>
    /// <exception cref="ArgumentException">Two algorithms share an identifier.</exception>
    public OneWayHasher(IEnumerable<IOneWayHashAlgorithm> algorithms, IOptionsMonitor<CryptographyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(algorithms);
        ArgumentNullException.ThrowIfNull(options);

        _algorithms = new Dictionary<string, IOneWayHashAlgorithm>(StringComparer.Ordinal);
        foreach (IOneWayHashAlgorithm algorithm in algorithms)
        {
            if (!_algorithms.TryAdd(algorithm.AlgorithmId, algorithm))
            {
                throw new ArgumentException(
                    $"More than one {nameof(IOneWayHashAlgorithm)} is registered with the id '{algorithm.AlgorithmId}'.",
                    nameof(algorithms));
            }
        }

        _options = options;
    }

    /// <inheritdoc />
    public string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        OneWayHashingOptions settings = _options.CurrentValue.OneWayHashing;
        if (!_algorithms.TryGetValue(settings.Algorithm, out IOneWayHashAlgorithm? algorithm))
        {
            throw new InvalidOperationException(
                $"The configured one-way hash algorithm '{settings.Algorithm}' is not registered.");
        }

        byte[] input = Encode(secret, settings.CurrentPepperId, settings);
        try
        {
            PhcHashString hash = algorithm.Hash(input);
            return settings.CurrentPepperId is null
                ? hash.ToString()
                : hash.WithParameter(PepperParameter, settings.CurrentPepperId).ToString();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    /// <inheritdoc />
    public HashVerificationResult Verify(string hash, string secret)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(secret);

        bool legacy = false;
        if (!PhcHashString.TryParse(hash, out PhcHashString? stored))
        {
            if (!Pbkdf2OneWayHashAlgorithm.TryParseLegacy(hash, out stored))
            {
                return HashVerificationResult.Failed;
            }

            legacy = true;
        }

        if (secret.Length == 0 || !_algorithms.TryGetValue(stored.AlgorithmId, out IOneWayHashAlgorithm? algorithm))
        {
            return HashVerificationResult.Failed;
        }

        OneWayHashingOptions settings = _options.CurrentValue.OneWayHashing;
        string? pepperId = stored.TryGetParameter(PepperParameter, out string? storedPepperId) ? storedPepperId : null;
        if (pepperId is not null && !settings.Peppers.ContainsKey(pepperId))
        {
            return HashVerificationResult.Failed;
        }

        PhcHashString algorithmHash = stored.WithoutParameter(PepperParameter);
        byte[] input;
        try
        {
            // Pre-release hashes were derived over the raw UTF-8 secret, without normalization or pepper.
            input = legacy ? Encoding.UTF8.GetBytes(secret) : Encode(secret, pepperId, settings);
        }
        catch (ArgumentException)
        {
            // Normalization rejects strings with unpaired surrogates; such a string never matched a stored hash.
            return HashVerificationResult.Failed;
        }

        try
        {
            if (!algorithm.Verify(algorithmHash, input))
            {
                return HashVerificationResult.Failed;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }

        bool rehash = legacy
            || !string.Equals(stored.AlgorithmId, settings.Algorithm, StringComparison.Ordinal)
            || !string.Equals(pepperId, settings.CurrentPepperId, StringComparison.Ordinal)
            || algorithm.RequiresRehash(algorithmHash);

        return rehash ? HashVerificationResult.SuccessRehashNeeded : HashVerificationResult.Success;
    }

    private static byte[] Encode(string secret, string? pepperId, OneWayHashingOptions settings)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(secret.Normalize(NormalizationForm.FormKC));
        if (pepperId is null)
        {
            return encoded;
        }

        if (!settings.Peppers.TryGetValue(pepperId, out string? pepperText) || !PepperKeys.TryDecode(pepperText, out byte[]? pepper))
        {
            CryptographicOperations.ZeroMemory(encoded);
            throw new InvalidOperationException($"The pepper '{pepperId}' is not configured or is not valid.");
        }

        try
        {
            return HMACSHA256.HashData(pepper, encoded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pepper);
            CryptographicOperations.ZeroMemory(encoded);
        }
    }
}
