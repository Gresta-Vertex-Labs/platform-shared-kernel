using System.Text.Json;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Rest.ProblemDetails;

/// <summary>
/// Deserializes <c>application/problem+json</c> response bodies into <see cref="Error"/> instances.
/// Uses <see cref="ProblemDetailsJsonContext"/> (STJ source-generated) as the primary path for AOT safety.
/// Falls back to reflection-based STJ only when the content type is not <c>application/problem+json</c>.
/// Maps: <c>type</c> → <see cref="Error.Code"/>; <c>detail ?? title</c> → <see cref="Error.Message"/>.
/// Never throws.
/// </summary>
internal static class ProblemDetailsDeserializer
{
    internal const string ProblemDetailsContentType = "application/problem+json";

    private static readonly Error GenericError = Error.Unexpected(
        "http.error",
        "An unexpected HTTP error occurred.");

    /// <summary>
    /// Attempts to deserialize a ProblemDetails body from the response.
    /// Returns a generic error for non-problem+json content types or on deserialization failure.
    /// Never throws.
    /// </summary>
    internal static async Task<Error> DeserializeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var contentType = response.Content.Headers.ContentType?.MediaType;

            if (string.Equals(contentType, ProblemDetailsContentType, StringComparison.OrdinalIgnoreCase))
            {
                // Primary path: STJ source-generated context (AOT-safe)
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                var dto = await JsonSerializer
                    .DeserializeAsync(stream, ProblemDetailsJsonContext.Default.ProblemDetailsDto, cancellationToken)
                    .ConfigureAwait(false);

                if (dto is not null)
                {
                    return MapToError(dto, (int)(response.StatusCode));
                }
            }
            else
            {
                // Fallback path: reflection-based STJ for non-problem+json non-2xx responses
                var body = await response.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        var dto = JsonSerializer.Deserialize<ProblemDetailsDto>(
                            body,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (dto is not null && (dto.Type is not null || dto.Title is not null || dto.Detail is not null))
                        {
                            return MapToError(dto, (int)(response.StatusCode));
                        }
                    }
                    catch (JsonException)
                    {
                        // Body is not JSON — fall through to generic error
                    }
                }

                // Return generic error with status code context
                return Error.Unexpected(
                    $"http.{(int)response.StatusCode}",
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
        catch (Exception)
        {
            // Never throw — swallow all deserialization failures
        }

        return GenericError;
    }

    private static Error MapToError(ProblemDetailsDto dto, int statusCode)
    {
        var code = !string.IsNullOrWhiteSpace(dto.Type)
            ? dto.Type!
            : $"http.{statusCode}";

        var message = !string.IsNullOrWhiteSpace(dto.Detail)
            ? dto.Detail!
            : !string.IsNullOrWhiteSpace(dto.Title)
                ? dto.Title!
                : $"HTTP {statusCode} error";

        return Error.Unexpected(code, message);
    }
}
