using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Rest.ProblemDetails;

/// <summary>
/// Deserializes <c>application/problem+json</c> response bodies into <see cref="Error"/> instances.
/// Uses <see cref="ProblemDetailsJsonContext"/> (STJ source-generated) as the primary path for AOT safety.
/// Falls back to reflection-based STJ only when the primary path is unavailable.
/// Maps: <c>type</c> → <see cref="Error.Code"/>; <c>detail ?? title</c> → <see cref="Error.Message"/>.
/// </summary>
internal static class ProblemDetailsDeserializer
{
    internal const string ProblemDetailsContentType = "application/problem+json";

    /// <summary>
    /// Attempts to deserialize a ProblemDetails body from the response.
    /// Returns a generic error for non-problem+json content types.
    /// Never throws.
    /// </summary>
    internal static Task<Error> DeserializeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
