using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using SharedKernel.Presentation.WebApi.Http;

namespace SharedKernel.Presentation.WebApi.Uploads;

/// <summary>
/// Global endpoint filter that enforces <see cref="RequireValidatedUploadAttribute"/> metadata
/// attached to an endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Safe to register on every route — this filter reads endpoint metadata and no-ops (calls
/// <c>next(context)</c> immediately, resolving nothing) when the attribute is absent. Mirrors
/// <see cref="Idempotency.IdempotencyKeyRequirementEndpointFilter"/>'s exact
/// global-registration/no-op-when-absent shape.
/// </para>
/// <para>
/// Declared <c>Content-Type</c>/<c>Content-Length</c> are checked before the body is fully
/// buffered. When a magic-byte signature is configured for the declared content type
/// (<see cref="UploadValidationOptions.AllowedMagicBytes"/>), the request body is buffered
/// (<c>HttpRequest.EnableBuffering()</c>) and the leading bytes are compared, then the
/// stream position is reset to <c>0</c> so the endpoint handler still sees the full body. A
/// mismatch short-circuits with a 413/415/400 <see cref="ProblemDetails"/> built via the shared
/// internal RFC 9457 shaping helper — never a hand-rolled body.
/// </para>
/// <para>
/// <b>THIS IS A BOUNDARY-SHAPE CHECK ONLY.</b> VIRUS/MALWARE SCANNING AND ANTIVIRUS-ENGINE
/// INTEGRATION ARE EXPLICITLY OUT OF SCOPE — SEE <see cref="UploadValidationOptions"/>.
/// </para>
/// </remarks>
public sealed class UploadValidationEndpointFilter : IEndpointFilter
{
    private const string PayloadTooLargeDetail = "The request body exceeds the maximum accepted upload size.";
    private const string UnsupportedMediaTypeDetail = "The request's Content-Type is not an accepted upload type for this endpoint.";
    private const string ContentSignatureMismatchDetail = "The request body's content does not match its declared Content-Type.";

    private readonly UploadValidationOptions _defaultOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="UploadValidationEndpointFilter"/> class.
    /// </summary>
    /// <param name="defaultOptions">
    /// The platform-default upload validation configuration, used whenever a
    /// <see cref="RequireValidatedUploadAttribute"/> omits its own override. When no
    /// <see cref="UploadValidationOptions"/> is registered in the container, a fresh default
    /// instance is used instead.
    /// </param>
    public UploadValidationEndpointFilter(UploadValidationOptions? defaultOptions = null)
    {
        _defaultOptions = defaultOptions ?? new UploadValidationOptions();
    }

    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var attribute = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RequireValidatedUploadAttribute>();

        if (attribute is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        var httpContext = context.HttpContext;
        var request = httpContext.Request;
        var maxSizeBytes = attribute.MaxSizeBytes ?? _defaultOptions.MaxSizeBytes;
        var allowedContentTypes = attribute.AllowedContentTypes.Count > 0
            ? attribute.AllowedContentTypes
            : (IReadOnlyCollection<string>)_defaultOptions.AllowedContentTypes;

        if (request.ContentLength is { } contentLength && contentLength > maxSizeBytes)
        {
            return BuildProblemResult(httpContext, StatusCodes.Status413PayloadTooLarge, PayloadTooLargeDetail);
        }

        var contentType = ExtractMediaType(request.ContentType);

        if (allowedContentTypes.Count > 0
            && (contentType is null || !allowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase)))
        {
            return BuildProblemResult(httpContext, StatusCodes.Status415UnsupportedMediaType, UnsupportedMediaTypeDetail);
        }

        if (contentType is not null
            && _defaultOptions.AllowedMagicBytes.TryGetValue(contentType, out var signature)
            && signature.Length > 0
            && !await BodyStartsWithSignatureAsync(request, signature, httpContext.RequestAborted).ConfigureAwait(false))
        {
            return BuildProblemResult(httpContext, StatusCodes.Status400BadRequest, ContentSignatureMismatchDetail);
        }

        return await next(context).ConfigureAwait(false);
    }

    private static async ValueTask<bool> BodyStartsWithSignatureAsync(HttpRequest request, byte[] signature, CancellationToken cancellationToken)
    {
        request.EnableBuffering();

        var buffer = new byte[signature.Length];
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        request.Body.Position = 0;

        return totalRead == signature.Length && buffer.AsSpan().SequenceEqual(signature);
    }

    private static string? ExtractMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var separatorIndex = contentType.IndexOf(';');
        return (separatorIndex >= 0 ? contentType[..separatorIndex] : contentType).Trim();
    }

    private static IResult BuildProblemResult(HttpContext httpContext, int statusCode, string detail)
        => Microsoft.AspNetCore.Http.Results.Problem(ProblemDetailsShaping.Create(statusCode, ReasonPhrases.GetReasonPhrase(statusCode), detail, httpContext));
}
