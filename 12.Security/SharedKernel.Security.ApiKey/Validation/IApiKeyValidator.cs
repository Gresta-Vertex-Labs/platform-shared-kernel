namespace SharedKernel.Security.ApiKey.Validation;

/// <summary>Decides whether a presented API key is valid and who it belongs to.</summary>
/// <remarks>
/// Use the managed keys registered by <c>AddManagedApiKeyAuthentication</c> unless keys come from an existing
/// system. A custom implementation must compare secrets in fixed time and never store keys in plain text.
/// </remarks>
public interface IApiKeyValidator
{
    /// <summary>Validates a key.</summary>
    /// <param name="presentedKey">The key from the request. Never empty; treat it as a secret.</param>
    /// <param name="cancellationToken">A token to cancel the validation.</param>
    /// <returns>The client the key belongs to, or a failure.</returns>
    ValueTask<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken);
}
