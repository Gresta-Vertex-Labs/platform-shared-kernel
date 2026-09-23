using Microsoft.OpenApi;
using SharedKernel.Presentation.OpenApi.Options;

namespace SharedKernel.Presentation.OpenApi.Documents;

/// <summary>The security schemes the documents declare, from <see cref="SharedKernelOpenApiOptions"/>.</summary>
/// <remarks>
/// A protected operation lists one security requirement per scheme, so satisfying any one of them is enough (OR). A
/// single requirement naming them all would mean every scheme at once.
/// </remarks>
internal sealed class SecuritySchemeSet
{
    /// <summary>The component id of the HTTP bearer scheme.</summary>
    public const string BearerName = "Bearer";

    /// <summary>The component id of the API key scheme.</summary>
    public const string ApiKeyName = "ApiKey";

    /// <summary>The component id of the mutual TLS scheme.</summary>
    public const string MutualTlsName = "MutualTls";

    // RFC 9110 authentication scheme name, as OpenAPI expects it.
    private const string BearerScheme = "bearer";

    private const string BearerFormat = "JWT";

    private readonly string? _apiKeyHeaderName;
    private readonly bool _bearer;
    private readonly bool _mutualTls;

    public SecuritySchemeSet(SharedKernelOpenApiOptions options)
    {
        _bearer = options.Bearer;
        _apiKeyHeaderName = string.IsNullOrEmpty(options.ApiKeyHeaderName) ? null : options.ApiKeyHeaderName;
        _mutualTls = options.MutualTls;

        List<string> names = [];
        if (_bearer)
        {
            names.Add(BearerName);
        }

        if (_apiKeyHeaderName is not null)
        {
            names.Add(ApiKeyName);
        }

        if (_mutualTls)
        {
            names.Add(MutualTlsName);
        }

        Names = names;
    }

    /// <summary>Gets the component ids of the declared schemes, in declaration order.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Returns new scheme components; a document owns and may change its instances, so none is shared.</summary>
    public IEnumerable<KeyValuePair<string, IOpenApiSecurityScheme>> CreateSchemes()
    {
        if (_bearer)
        {
            yield return new(BearerName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = BearerScheme,
                BearerFormat = BearerFormat,
                Description = "An access token in the Authorization header: Authorization: Bearer <token>.",
            });
        }

        if (_apiKeyHeaderName is not null)
        {
            yield return new(ApiKeyName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = _apiKeyHeaderName,
                Description = $"An API key in the {_apiKeyHeaderName} header.",
            });
        }

        if (_mutualTls)
        {
            yield return new(MutualTlsName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.MutualTLS,
                Description = "A client certificate presented during the TLS handshake.",
            });
        }
    }
}
