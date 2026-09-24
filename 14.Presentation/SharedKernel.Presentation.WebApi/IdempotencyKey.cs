using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using SharedKernel.Presentation.WebApi.Idempotency;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The <c>Idempotency-Key</c> of a request, as a minimal-API handler parameter: declaring it requires the header,
/// validates it and documents it, with nothing else to register.
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
/// Minimal APIs only; MVC actions use <see cref="RequireIdempotencyKeyAttribute"/> and <c>HttpContext.GetIdempotencyKey()</c>.
/// In a unit test, construct one directly: <c>new IdempotencyKey("order-17")</c>.
/// </para>
/// </remarks>
public readonly record struct IdempotencyKey : IEndpointParameterMetadataProvider
{
    /// <summary>
    /// The longest key accepted, in characters: 256. The pair of double quotes a client may enclose the key in does not
    /// count, so the header value itself may be two characters longer.
    /// </summary>
    public const int MaxLength = 256;

    private static readonly string InvalidKeyMessage = string.Create(
        CultureInfo.InvariantCulture,
        $"An idempotency key must be 1 to {MaxLength} visible ASCII characters.");

    /// <summary>Initializes a new instance of the <see cref="IdempotencyKey"/> struct.</summary>
    /// <param name="value">The key: 1 to <see cref="MaxLength"/> visible ASCII characters (0x21–0x7E).</param>
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
    /// The key, or <see langword="null"/> when the header is missing or invalid — which happens only when
    /// <c>UseSharedKernelWebApi()</c> is not in the pipeline to refuse such a request first; the framework then
    /// answers 400.
    /// </returns>
    public static ValueTask<IdempotencyKey?> BindAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var key = IdempotencyKeyGuard.GetKey(context);
        return ValueTask.FromResult<IdempotencyKey?>(key is null ? null : new IdempotencyKey(key));
    }

    /// <summary>Returns the key.</summary>
    /// <returns><see cref="Value"/>, or an empty string for a default instance.</returns>
    public override string ToString() => Value ?? string.Empty;

    /// <inheritdoc />
    static void IEndpointParameterMetadataProvider.PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(new RequireIdempotencyKeyAttribute());
    }
}
