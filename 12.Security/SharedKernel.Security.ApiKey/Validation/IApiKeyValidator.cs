namespace SharedKernel.Security.ApiKey.Validation;

/// <summary>
/// Validates a presented API key. The sole consumer-supplied extensibility point for
/// <c>SharedKernel.Security.ApiKey</c> — this package never dictates a key storage mechanism
/// (configuration, database, secret store — the consuming service decides).
/// </summary>
/// <remarks>
/// Implementations should treat key comparison as security-sensitive: prefer a hashed lookup (e.g. via
/// <c>01.Core/SharedKernel.Cryptography</c>'s <c>IOneWayHasher</c>) over storing keys in plaintext, and
/// never compare the raw presented key against secret material with an early-exit comparison — use
/// <c>SharedKernel.Cryptography.FixedTimeComparison.AreEqual</c>, or <c>FixedTimeComparison.AreEqualToAny</c>
/// when more than one key per client is active during a rotation window.
/// </remarks>
public interface IApiKeyValidator
{
    /// <summary>
    /// Validates the presented API key.
    /// </summary>
    /// <param name="presentedKey">The raw key value extracted from the request. Never null or empty.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// An <see cref="ApiKeyValidationResult"/> describing whether the key is valid and, when valid, the
    /// associated client identity.
    /// </returns>
    Task<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken);
}
