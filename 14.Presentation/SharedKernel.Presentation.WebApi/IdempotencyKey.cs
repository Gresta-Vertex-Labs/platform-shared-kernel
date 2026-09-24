using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Presentation.WebApi.Idempotency;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The <c>Idempotency-Key</c> of a request, as a minimal-API handler parameter: declaring it validates and documents
/// the header, with nothing else to register. Declared not-null it requires the header; declared nullable
/// (<c>IdempotencyKey?</c>) it accepts one.
/// </summary>
/// <remarks>
/// <para>
/// <c>app.MapPost("/payments", (PaymentRequest body, IdempotencyKey key, ISender sender) =&gt; …)</c>. The parameter
/// adds <see cref="IIdempotencyKeyRequiredMetadata"/> to the endpoint, so <c>UseSharedKernelWebApi()</c> answers a
/// missing key with 400 <c>idempotency.key_required</c> and a malformed one with 400 <c>idempotency.key_invalid</c>
/// before the handler runs, and the OpenAPI add-on documents the header. The handler therefore always receives a
/// valid key: 1 to <see cref="MaxLength"/> visible ASCII characters, one pair of surrounding double quotes (the IETF
/// draft's structured-field string) removed.
/// </para>
/// <para>
/// Declared nullable — <c>IdempotencyKey? key</c> — it adds <see cref="IIdempotencyKeyAcceptedMetadata"/> instead: a
/// request without the header reaches the handler with <see langword="null"/>, and a malformed key is still 400
/// <c>idempotency.key_invalid</c>. The handler receives <see langword="null"/> only when the request sent no key. In
/// code compiled without nullable annotations the parameter requires the header.
/// </para>
/// <para>
/// Minimal APIs only; MVC actions use <see cref="RequireIdempotencyKeyAttribute"/> or
/// <see cref="AcceptIdempotencyKeyAttribute"/> and <c>HttpContext.GetIdempotencyKey()</c>. In a unit test, construct one
/// directly: <c>new IdempotencyKey("order-17")</c>.
/// </para>
/// </remarks>
public sealed record IdempotencyKey : IEndpointParameterMetadataProvider
{
    /// <summary>
    /// The longest key accepted, in characters: 256. The pair of double quotes a client may enclose the key in does not
    /// count, so the header value itself may be two characters longer.
    /// </summary>
    public const int MaxLength = 256;

    private static readonly string InvalidKeyMessage = string.Create(
        CultureInfo.InvariantCulture,
        $"An idempotency key must be 1 to {MaxLength} visible ASCII characters.");

    /// <summary>Initializes a new instance of the <see cref="IdempotencyKey"/> class.</summary>
    /// <param name="value">The key: 1 to <see cref="MaxLength"/> visible ASCII characters (0x21–0x7E).</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a valid idempotency key.</exception>
    public IdempotencyKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!IdempotencyKeyGuard.IsValid(value))
        {
            throw new ArgumentException(InvalidKeyMessage, nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the key, without surrounding quotes.</summary>
    public string Value { get; }

    /// <summary>Binds the parameter from the request's <c>Idempotency-Key</c> header.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>
    /// The key, or <see langword="null"/> when the request sends none — which the framework answers with 400 when the
    /// parameter is not nullable.
    /// </returns>
    /// <exception cref="BadHttpRequestException">
    /// The key is invalid (status 400), so that it never binds as a missing one. This happens only when
    /// <c>UseSharedKernelWebApi()</c> is not in the pipeline to refuse the request first.
    /// </exception>
    public static ValueTask<IdempotencyKey?> BindAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var key = IdempotencyKeyGuard.BindKey(context);
        return ValueTask.FromResult(key is null ? null : new IdempotencyKey(key));
    }

    /// <summary>Returns the key.</summary>
    /// <returns><see cref="Value"/>.</returns>
    public override string ToString() => Value;

    /// <inheritdoc />
    static void IEndpointParameterMetadataProvider.PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(HeaderParameter.IsOptional(parameter) ? new AcceptIdempotencyKeyAttribute() : new RequireIdempotencyKeyAttribute());
    }
}
