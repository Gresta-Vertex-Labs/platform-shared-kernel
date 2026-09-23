using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Net.Http.Headers;

namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>
/// A 200 OK result carrying an <c>ETag</c>, which answers a <c>GET</c> or <c>HEAD</c> whose <c>If-None-Match</c>
/// already names that version with 304 Not Modified and no body. Returned by <c>ToOkWithETag(…)</c>.
/// </summary>
/// <typeparam name="TValue">The type of the response body.</typeparam>
/// <remarks>
/// <c>If-None-Match</c> uses the weak comparison of RFC 9110 section 13.1.2, so <c>W/"42"</c> matches <c>"42"</c>.
/// For other methods the body is always sent: the request has already been carried out. OpenAPI documents the 200
/// body and the 304.
/// </remarks>
public sealed class OkWithETag<TValue> : IResult, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<TValue>, IEndpointMetadataProvider
{
    private const string JsonContentType = "application/json";

    private readonly string _entityTag;

    /// <summary>Initializes a new instance of the <see cref="OkWithETag{TValue}"/> class.</summary>
    /// <param name="value">The response body.</param>
    /// <param name="version">The version sent as the <c>ETag</c>; visible ASCII other than <c>"</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="version"/> is empty or cannot be an entity tag.</exception>
    public OkWithETag(TValue? value, string version)
    {
        _entityTag = EntityTags.ToEntityTag(version, nameof(version));
        Value = value;
        Version = version;
    }

    /// <summary>Gets the response body.</summary>
    public TValue? Value { get; }

    /// <inheritdoc />
    object? IValueHttpResult.Value => Value;

    /// <summary>Gets the version sent as the <c>ETag</c>, without quotes.</summary>
    public string Version { get; }

    /// <summary>Gets 200, the status sent unless the client already has this version.</summary>
    public int StatusCode => StatusCodes.Status200OK;

    /// <inheritdoc />
    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.Headers[HeaderNames.ETag] = _entityTag;

        var request = httpContext.Request;
        if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            && EntityTags.IfNoneMatchMatches(request, _entityTag))
        {
            httpContext.Response.StatusCode = StatusCodes.Status304NotModified;
            return Task.CompletedTask;
        }

        return TypedResults.Ok(Value).ExecuteAsync(httpContext);
    }

    /// <inheritdoc />
    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(TValue), [JsonContentType]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status304NotModified, typeof(void)));
    }
}
