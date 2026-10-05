namespace SharedKernel.Security.ApiKey;

/// <summary>Names used by the API key authentication scheme.</summary>
public static class ApiKeyAuthenticationDefaults
{
    /// <summary>The authentication scheme name, also the identity's authentication type: <c>ApiKey</c>.</summary>
    public const string AuthenticationScheme = "ApiKey";

    /// <summary>
    /// The policy scheme that becomes the default scheme: it uses <see cref="AuthenticationScheme"/> when the request
    /// carries an API key header, and the previous default scheme otherwise.
    /// </summary>
    public const string ForwardingScheme = "ApiKeyOrDefault";

    /// <summary>The default request header: <c>X-Api-Key</c>.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>The claim carrying the key id of a managed key: <c>api_key_id</c>.</summary>
    public const string KeyIdClaimType = "api_key_id";
}
