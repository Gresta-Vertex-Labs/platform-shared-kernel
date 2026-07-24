using System.ClientModel;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.SemanticKernel.Errors;

/// <summary>
/// Maps a raised <see cref="Exception"/> from the underlying OpenAI-compatible connector onto the
/// canonical <see cref="IntelligenceErrors"/> factory — the only construction path for an
/// <see cref="Error"/> value in this package.
/// </summary>
internal static class SemanticKernelErrors
{
    /// <summary>Maps <paramref name="exception"/> to the corresponding <see cref="Error"/>.</summary>
    public static Error FromException(Exception exception, string providerName, string operation, string modelId) =>
        exception switch
        {
            ClientResultException clientError => FromClientResultException(clientError, providerName, operation, modelId),
            _ => IntelligenceErrors.CompletionFailed(providerName, exception.Message),
        };

    private static Error FromClientResultException(ClientResultException exception, string providerName, string operation, string modelId) =>
        exception.Status switch
        {
            401 or 403 => IntelligenceErrors.Unauthorized(providerName, operation),
            404 => IntelligenceErrors.ModelNotFound(modelId),
            // The connector version verified for this package exposes no strongly-typed Retry-After
            // header accessor on ClientResultException — retryAfter is therefore null, documented as a
            // known gap rather than guessed at.
            429 => IntelligenceErrors.RateLimited(providerName, retryAfter: null),
            >= 500 => IntelligenceErrors.EngineFault(providerName, operation, exception.Message),
            _ => IntelligenceErrors.CompletionFailed(providerName, exception.Message),
        };
}
