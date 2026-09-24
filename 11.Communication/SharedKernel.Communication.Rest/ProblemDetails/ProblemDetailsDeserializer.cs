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
/// Mirrors <c>14.Presentation</c>'s wire shape (P-562): every problem carries <c>Error.Code</c> in the
/// <c>errorCode</c> extension and <c>Error.Message</c> in <c>detail</c> (localized, or a generic sentence for a
/// server error outside Development). The code is <c>errorCode</c>; a problem without one gets
/// <c>http.{status}</c>, the code <c>14.Presentation</c> itself gives a response the framework produced. The code
/// is never <c>title</c>, which is the status reason phrase (<c>"Not Found"</c>) and, from a service outside the
/// platform, free text; and never <c>type</c>, a URI. The message is <c>detail</c>, else
/// <c>HTTP {status} error</c>. The <see cref="ErrorType"/> comes from the response status through
/// <see cref="HttpStatusErrorTypeMap"/>.
/// </para>
/// <para>
/// Field errors are read only from a 400 or a 422 response: 400 is the platform's validation status, and 422 is the
/// one many other frameworks use for the same failure. There the <c>errors</c> extension (keyed by field path, or by
/// code for an error that names no field, each value an array of messages) is rebuilt into
/// <see cref="Error.Validation(System.Collections.Generic.IReadOnlyList{Error})"/>, so a 422 with field errors is a
/// validation failure and a 422 without them stays a <see cref="ErrorType.BusinessRule"/> refusal. The parallel
/// <c>errorCodes</c> extension, when present, supplies each child's real code index by index and the key is kept as
/// its <see cref="ErrorArgumentNames.PropertyPath"/> argument; without it each key is taken as the code.
/// </para>
/// <para>
/// For any other status both maps are ignored and the error keeps the category of its status.
/// <see cref="Error.Details"/> exists only on that validation aggregate. Re-reading a 401, a 409 or a 503 as a
/// validation failure would hide an authentication failure, a conflict or a retryable outage. It would also pass the
/// map's messages on to the caller's own clients, unredacted.
/// </para>
/// <para>
/// A response without a usable ProblemDetails body still takes its <see cref="ErrorType"/> from the status through
/// the same <see cref="HttpStatusErrorTypeMap"/>, with the code <c>http.{status}</c>. Such a response is an HTML
/// error page, an empty body, or a body with none of the members above (for example a bare
/// <c>{"title":"Not Found","status":404}</c>); typically a gateway, load balancer or proxy answering for a service
/// that is down or slow. A gateway's bodiless 503 or 429 reads as <see cref="ErrorType.Unavailable"/> and its 504
/// as <see cref="ErrorType.Timeout"/>, so a caller retries an outage instead of treating it as a defect: the same
/// category a ProblemDetails body without field errors gets for that status.
/// </para>
/// <para>Never throws.</para>
/// </remarks>
internal static class ProblemDetailsDeserializer
{
    internal const string ProblemDetailsContentType = "application/problem+json";

    /// <summary>
    /// The prefix of the code given to an error whose response carried no code of its own:
    /// <c>http.{status}</c>, the code <c>14.Presentation</c> itself gives framework-generated problems.
    /// </summary>
    private const string StatusCodePrefix = "http.";

    /// <summary>
    /// Reflection-based <see cref="JsonSerializerOptions"/> used in the non-<c>application/problem+json</c>
    /// fallback path. Initialized once at class load time to eliminate per-call allocation on every
    /// non-2xx response (R-11 / P-160 Fix 1).
    /// </summary>
    internal static readonly JsonSerializerOptions ReflectionFallbackOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Attempts to deserialize a ProblemDetails body from the response.
    /// For a non-JSON, empty, or unrecognizable body, returns an error of the status's
    /// <see cref="ErrorType"/> (via <see cref="HttpStatusErrorTypeMap"/>) with the code <c>http.{status}</c>.
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

            if (dto is not null && HasRecognizableContent(dto, statusCode))
            {
                return MapToError(dto, statusCode);
            }
        }
        catch (Exception)
        {
            // Never throw — swallow all deserialization failures and fall through to a
            // status-aware generic error below.
        }

        return StatusAwareError(statusCode, response.ReasonPhrase);
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
    /// A deserialized body is only trusted when it carries at least one member this deserializer maps
    /// from for this status: the code (a non-blank <see cref="ProblemDetailsDto.ErrorCode"/>), the message
    /// (a non-blank <see cref="ProblemDetailsDto.Detail"/>) or, on a 400 or 422, field errors
    /// (<see cref="ProblemDetailsDto.Errors"/>). A body with none of these is treated the same as no body
    /// at all: <c>{}</c>, or a problem carrying only the members that are never read (<c>type</c>,
    /// <c>title</c>, <c>status</c>, <c>instance</c>).
    /// </summary>
    private static bool HasRecognizableContent(ProblemDetailsDto dto, int statusCode) =>
        !string.IsNullOrWhiteSpace(dto.ErrorCode)
        || !string.IsNullOrWhiteSpace(dto.Detail)
        || (IsValidationStatus(statusCode) && dto.Errors is { Count: > 0 });

    /// <summary>
    /// Whether a response with this status may carry field errors: 400, the status
    /// <c>14.Presentation</c> answers every validation failure with, and 422, the one many frameworks
    /// outside the platform use for the same failure. For any other status the <c>errors</c> and
    /// <c>errorCodes</c> maps are ignored.
    /// </summary>
    private static bool IsValidationStatus(int statusCode) => statusCode is 400 or 422;

    /// <summary>
    /// The error for a response without a usable body: the status's <see cref="ErrorType"/>, the code
    /// <c>http.{status}</c> and the status line as the message — so an HTML 503 from a gateway is an
    /// <see cref="ErrorType.Unavailable"/> the caller can retry, exactly as a ProblemDetails 503 would be.
    /// </summary>
    private static Error StatusAwareError(int statusCode, string? reasonPhrase) =>
        CreateError(
            HttpStatusErrorTypeMap.Resolve(statusCode),
            CodeForStatus(statusCode),
            $"HTTP {statusCode} {reasonPhrase}".TrimEnd());

    private static string CodeForStatus(int statusCode) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{StatusCodePrefix}{statusCode}");

    private static Error CreateError(ErrorType errorType, string code, string message) => errorType switch
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

    /// <summary>
    /// Maps a body with recognizable content. On a 400 or 422 with field errors this is the
    /// <see cref="Error.Validation(System.Collections.Generic.IReadOnlyList{Error})"/> aggregate of them; otherwise
    /// it is one error of the status's <see cref="ErrorType"/>, with the <see cref="ResolveCode">code</see> and
    /// <see cref="ResolveMessage">message</see> of the body.
    /// </summary>
    private static Error MapToError(ProblemDetailsDto dto, int statusCode)
    {
        if (IsValidationStatus(statusCode) && dto.Errors is { Count: > 0 } fieldErrors)
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

        return CreateError(
            HttpStatusErrorTypeMap.Resolve(statusCode),
            ResolveCode(dto, statusCode),
            ResolveMessage(dto, statusCode));
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
    /// The code is <c>errorCode</c>, which every platform problem carries. A problem without one gets
    /// <c>http.{status}</c>, as a response without a body does.
    /// </summary>
    /// <remarks>
    /// Never <c>title</c>. Since P-562 it is the status reason phrase (<c>"Not Found"</c>), and from a service
    /// outside the platform it is free text. As the code, that text would drive the caller's branches, log
    /// labels and translations, and would go out again as the <c>errorCode</c> of the caller's own responses,
    /// where it could pose as a platform code. Never <c>type</c> either: it is a URI.
    /// </remarks>
    private static string ResolveCode(ProblemDetailsDto dto, int statusCode) =>
        string.IsNullOrWhiteSpace(dto.ErrorCode) ? CodeForStatus(statusCode) : dto.ErrorCode;

    /// <summary>
    /// The message is <c>detail</c>, else <c>HTTP {status} error</c>; never <c>title</c>.
    /// </summary>
    private static string ResolveMessage(ProblemDetailsDto dto, int statusCode) =>
        !string.IsNullOrWhiteSpace(dto.Detail)
            ? dto.Detail!
            : $"HTTP {statusCode} error";
}
