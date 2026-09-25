using Microsoft.AspNetCore.Http;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.WebApi.Idempotency;

/// <summary>
/// Extracts and format-validates the inbound <see cref="IdempotencyKeyHeader"/> request header.
/// </summary>
public static class HttpContextIdempotencyExtensions
{
    /// <summary>The maximum accepted length, in characters, of an idempotency key.</summary>
    public const int MaxIdempotencyKeyLength = 256;

    /// <summary>
    /// The inbound HTTP request header name carrying a client-supplied idempotency key
    /// (<c>"Idempotency-Key"</c>).
    /// </summary>
    /// <remarks>
    /// Forwards <c>01.Core</c>'s <see cref="WellKnownHeaders.IdempotencyKey"/>, the name
    /// <c>11.Communication.Rest</c> sends the key under (P-566). The two sides used to disagree
    /// (<c>x-idempotency-key</c> out, <c>Idempotency-Key</c> in), so a retried outbound call arrived here
    /// with no key at all.
    /// </remarks>
    public const string IdempotencyKeyHeader = WellKnownHeaders.IdempotencyKey;

    /// <summary>
    /// Attempts to read and format-validate the <see cref="IdempotencyKeyHeader"/> header from
    /// <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="key">
    /// The validated key when this method returns <see langword="true"/>; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the header is present and holds a non-empty,
    /// non-whitespace-only value no longer than <see cref="MaxIdempotencyKeyLength"/> characters;
    /// otherwise <see langword="false"/>. Never throws on missing or malformed input.
    /// </returns>
    /// <remarks>
    /// This is the recommended way for an endpoint handler to read the validated value onto a
    /// command's <c>IIdempotentRequest.IdempotencyKey</c> property before dispatch — this
    /// package does not attempt automatic request binding, only extraction/validation and the
    /// guard filter (<see cref="IdempotencyKeyRequirementEndpointFilter"/>).
    /// </remarks>
    public static bool TryGetIdempotencyKey(this HttpContext context, out string? key)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var headerValue))
        {
            var value = headerValue.ToString();

            if (!string.IsNullOrWhiteSpace(value) && value.Length <= MaxIdempotencyKeyLength)
            {
                key = value;
                return true;
            }
        }

        key = null;
        return false;
    }
}
