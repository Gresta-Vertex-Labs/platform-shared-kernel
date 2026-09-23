using System.Text.Json;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Rest.ProblemDetails;

/// <summary>
/// Deserializes <c>application/problem+json</c> response bodies into <see cref="Error"/> instances.
/// Uses <see cref="ProblemDetailsJsonContext"/> (STJ source-generated) as the primary path for AOT safety.
/// Falls back to reflection-based STJ only when the content type is not <c>application/problem+json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>14.Presentation</c>'s real wire shape (<c>ErrorProblemDetailsExtensions</c>/
/// <c>ValidationProblemDetailsExtensions</c>): <c>title</c> and the <c>errorCode</c> extension both
/// carry <c>Error.Code</c> (never <c>type</c>, which is an RFC 9457 status URI such as
/// <c>"https://httpstatuses.io/404"</c>, not a machine code); <c>detail</c> carries <c>Error.Message</c>
/// (localized or the throw-site message, never blank on a real response); the resolved HTTP status
/// maps back to an <see cref="ErrorType"/> via <see cref="HttpStatusErrorTypeMap"/>; and, when the
/// failure aggregates several field errors, the <c>errors</c> extension (keyed by field path, or by
/// code for an error that names no field, each value an array of messages) is rebuilt into
/// <see cref="Error.Validation(System.Collections.Generic.IReadOnlyList{Error})"/>. The parallel
/// <c>errorCodes</c> extension, when present, supplies each child's real code index by index and
/// the key is kept as its <see cref="ErrorArgumentNames.PropertyPath"/> argument; without it each
/// key is taken as the code.
/// </para>
/// <para>Never throws.</para>
/// </remarks>
internal static class ProblemDetailsDeserializer
{
    internal const string ProblemDetailsContentType = "application/problem+json";

    /// <summary>
    /// Reflection-based <see cref="JsonSerializerOptions"/> used in the non-<c>application/problem+json</c>
    /// fallback path. Initialized once at class load time to eliminate per-call allocation on every
    /// non-2xx response (R-11 / P-160 Fix 1).
    /// </summary>
    internal static readonly JsonSerializerOptions ReflectionFallbackOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Attempts to deserialize a ProblemDetails body from the response.
    /// Returns a status-aware unexpected error for a non-JSON, empty, or unrecognizable body.
    /// Never throws.
    /// </summary>
    internal static async Task<Error> DeserializeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;

        try
        {
            var contentType = response.Content.Headers.ContentType?.MediaType;

            var dto = string.Equals(contentType, ProblemDetailsContentType, StringComparison.OrdinalIgnoreCase)
                ? await DeserializeSourceGeneratedAsync(response, cancellationToken).ConfigureAwait(false)
                : await DeserializeReflectionFallbackAsync(response, cancellationToken).ConfigureAwait(false);

            if (dto is not null && HasRecognizableContent(dto))
            {
                return MapToError(dto, statusCode);
            }
        }
        catch (Exception)
        {
            // Never throw — swallow all deserialization failures and fall through to a
            // status-aware generic error below.
        }

        return StatusAwareUnexpected(statusCode, response.ReasonPhrase);
    }

    private static async Task<ProblemDetailsDto?> DeserializeSourceGeneratedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // Primary path: STJ source-generated context (AOT-safe)
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        return await JsonSerializer
            .DeserializeAsync(stream, ProblemDetailsJsonContext.Default.ProblemDetailsDto, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ProblemDetailsDto?> DeserializeReflectionFallbackAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // Fallback path: reflection-based STJ for non-problem+json non-2xx responses
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProblemDetailsDto>(body, ReflectionFallbackOptions);
        }
        catch (JsonException)
        {
            // Body is not JSON — treat as unrecognizable, never throw.
            return null;
        }
    }

    /// <summary>
    /// A deserialized body is only trusted when it carries at least one member this deserializer
    /// actually maps from: a code source (<see cref="ProblemDetailsDto.ErrorCode"/> or
    /// <see cref="ProblemDetailsDto.Title"/>), a message source (<see cref="ProblemDetailsDto.Detail"/>),
    /// or a field-error aggregate (<see cref="ProblemDetailsDto.Errors"/>). A body with none of these
    /// (e.g. <c>{}</c>, or a JSON literal that happened to deserialize without error) is treated the
    /// same as no body at all.
    /// </summary>
    private static bool HasRecognizableContent(ProblemDetailsDto dto) =>
        dto.ErrorCode is not null
        || dto.Title is not null
        || dto.Detail is not null
        || (dto.Errors is { Count: > 0 });

    private static Error StatusAwareUnexpected(int statusCode, string? reasonPhrase) =>
        Error.Unexpected($"http.{statusCode}", $"HTTP {statusCode} {reasonPhrase}".TrimEnd());

    private static Error MapToError(ProblemDetailsDto dto, int statusCode)
    {
        if (dto.Errors is { Count: > 0 } fieldErrors)
        {
            var details = new List<Error>();

            foreach (var (key, messages) in fieldErrors)
            {
                var codes = dto.ErrorCodes?.GetValueOrDefault(key);

                for (var i = 0; i < messages.Length; i++)
                {
                    details.Add(ToDetail(key, messages[i], codes is not null && i < codes.Length ? codes[i] : null));
                }
            }

            if (details.Count > 0)
            {
                return Error.Validation(details);
            }
        }

        var errorType = HttpStatusErrorTypeMap.Resolve(statusCode);
        var code = ResolveCode(dto, statusCode);
        var message = ResolveMessage(dto, statusCode);

        return errorType switch
        {
            ErrorType.Validation => Error.Validation(code, message),
            ErrorType.Unauthorized => Error.Unauthorized(code, message),
            ErrorType.Forbidden => Error.Forbidden(code, message),
            ErrorType.NotFound => Error.NotFound(code, message),
            ErrorType.Conflict => Error.Conflict(code, message),
            ErrorType.BusinessRule => Error.BusinessRule(code, message),
            ErrorType.Unavailable => Error.Unavailable(code, message),
            ErrorType.Timeout => Error.Timeout(code, message),
            _ => Error.Unexpected(code, message),
        };
    }

    /// <summary>
    /// Rebuilds one child error of a field-error aggregate. <paramref name="key"/> is the
    /// <c>errors</c> map key, which <c>14.Presentation</c> sets to the error's field path when it
    /// has one and to its code otherwise; <paramref name="wireCode"/> is the matching entry of the
    /// <c>errorCodes</c> map, absent from older servers.
    /// </summary>
    /// <remarks>
    /// With a code from <c>errorCodes</c> that differs from the key, the key is a field path: the
    /// error takes the real code and records the key under <see cref="ErrorArgumentNames.PropertyPath"/>.
    /// Without one, the key is used as the code, as before <c>errorCodes</c> existed.
    /// </remarks>
    private static Error ToDetail(string key, string message, string? wireCode)
    {
        if (string.IsNullOrWhiteSpace(wireCode) || string.Equals(wireCode, key, StringComparison.Ordinal))
        {
            return Error.Validation(key, message);
        }

        return Error.Validation(wireCode, message) with
        {
            MessageArguments = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ErrorArgumentNames.PropertyPath] = key,
            },
        };
    }

    /// <summary>
    /// The wire shape carries <c>Error.Code</c> in both <c>errorCode</c> and <c>title</c> (never
    /// <c>type</c>, an RFC 9457 status URI) — <c>errorCode</c> is preferred as the more explicit
    /// source, falling back to <c>title</c>, then to a status-derived code when neither is present.
    /// </summary>
    private static string ResolveCode(ProblemDetailsDto dto, int statusCode) =>
        !string.IsNullOrWhiteSpace(dto.ErrorCode)
            ? dto.ErrorCode!
            : !string.IsNullOrWhiteSpace(dto.Title)
                ? dto.Title!
                : $"http.{statusCode}";

    private static string ResolveMessage(ProblemDetailsDto dto, int statusCode) =>
        !string.IsNullOrWhiteSpace(dto.Detail)
            ? dto.Detail!
            : $"HTTP {statusCode} error";
}
