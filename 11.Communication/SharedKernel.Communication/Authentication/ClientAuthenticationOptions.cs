using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Communication;

/// <summary>How a client proves who it is to the service it calls.</summary>
/// <example>
/// <code>
/// "Authentication": {
///   "Mode": "ClientCredentials",
///   "ClientCredentials": {
///     "TokenEndpoint": "https://login.example.com/oauth2/token",
///     "ClientId": "checkout",
///     "ClientSecret": "from a secret store",
///     "Scope": "inventory.read inventory.reserve"
///   }
/// }
/// </code>
/// </example>
public sealed class ClientAuthenticationOptions
{
    /// <summary>Gets or sets the kind of credential the client sends. <see cref="ClientAuthenticationMode.None"/> by default.</summary>
    public ClientAuthenticationMode Mode { get; set; } = ClientAuthenticationMode.None;

    /// <summary>Gets or sets the OAuth 2.0 client-credentials settings, used when <see cref="Mode"/> is <see cref="ClientAuthenticationMode.ClientCredentials"/>.</summary>
    public ClientCredentialsOptions ClientCredentials { get; set; } = new();

    /// <summary>Gets or sets the API key settings, used when <see cref="Mode"/> is <see cref="ClientAuthenticationMode.ApiKey"/>.</summary>
    public ClientApiKeyOptions ApiKey { get; set; } = new();

    internal IEnumerable<ValidationResult> Validate()
    {
        const string Prefix = nameof(CommunicationClientOptions.Authentication);

        if (!Enum.IsDefined(Mode))
        {
            yield return new ValidationResult($"{Prefix}:Mode '{Mode}' is not a defined value.", [Prefix]);
            yield break;
        }

        if (Mode == ClientAuthenticationMode.ClientCredentials)
        {
            ClientCredentialsOptions cc = ClientCredentials;
            if (cc.TokenEndpoint is not { IsAbsoluteUri: true } endpoint || endpoint.Scheme is not ("https" or "http"))
            {
                yield return new ValidationResult(
                    $"{Prefix}:ClientCredentials:TokenEndpoint must be an absolute https address.", [Prefix]);
            }

            if (string.IsNullOrWhiteSpace(cc.ClientId) || string.IsNullOrWhiteSpace(cc.ClientSecret))
            {
                yield return new ValidationResult(
                    $"{Prefix}:ClientCredentials:ClientId and ClientSecret are required.", [Prefix]);
            }

            if (!Enum.IsDefined(cc.SecretTransport))
            {
                yield return new ValidationResult(
                    $"{Prefix}:ClientCredentials:SecretTransport '{cc.SecretTransport}' is not a defined value.", [Prefix]);
            }

            if (cc.RefreshBeforeExpiry < TimeSpan.Zero || cc.RefreshBeforeExpiry > TimeSpan.FromHours(1))
            {
                yield return new ValidationResult(
                    $"{Prefix}:ClientCredentials:RefreshBeforeExpiry must be between zero and 1 hour.", [Prefix]);
            }
        }

        if (Mode == ClientAuthenticationMode.ApiKey
            && (string.IsNullOrWhiteSpace(ApiKey.HeaderName) || string.IsNullOrWhiteSpace(ApiKey.Value)))
        {
            yield return new ValidationResult($"{Prefix}:ApiKey:HeaderName and Value are required.", [Prefix]);
        }
    }
}

/// <summary>The kind of credential a client sends.</summary>
public enum ClientAuthenticationMode
{
    /// <summary>No credential; the caller may still set its own <c>Authorization</c> header on a request.</summary>
    None = 0,

    /// <summary>An OAuth 2.0 access token from the client-credentials grant, cached and refreshed before it expires.</summary>
    ClientCredentials = 1,

    /// <summary>A fixed API key in a header.</summary>
    ApiKey = 2,

    /// <summary>
    /// A token from the <see cref="IAccessTokenProvider"/> registered for the client with <c>UseAccessTokenProvider&lt;T&gt;()</c>
    /// (managed identity, token exchange, …). Set by that call; not meant for configuration.
    /// </summary>
    AccessTokenProvider = 3,
}

/// <summary>
/// The OAuth 2.0 client-credentials grant (RFC 6749 §4.4): the client exchanges its own id and secret for an access
/// token, sent as <c>Authorization: Bearer</c>.
/// </summary>
public sealed class ClientCredentialsOptions
{
    /// <summary>Gets or sets the authorization server's token endpoint. Required.</summary>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>Gets or sets the client id. Required.</summary>
    public string? ClientId { get; set; }

    /// <summary>Gets or sets the client secret. Required; keep it in a secret store, never in appsettings.json.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Gets or sets the space-separated scopes to request, when the authorization server needs them.</summary>
    public string? Scope { get; set; }

    /// <summary>Gets or sets the <c>audience</c> parameter some authorization servers (Auth0, Okta) require.</summary>
    public string? Audience { get; set; }

    /// <summary>Gets or sets the <c>resource</c> parameter (RFC 8707) some authorization servers require.</summary>
    public string? Resource { get; set; }

    /// <summary>
    /// Gets or sets how the secret is sent to the token endpoint. <see cref="ClientSecretTransport.BasicAuthentication"/>
    /// by default, the method RFC 6749 requires every authorization server to support.
    /// </summary>
    public ClientSecretTransport SecretTransport { get; set; } = ClientSecretTransport.BasicAuthentication;

    /// <summary>
    /// Gets or sets how long before a token expires a new one is fetched, so no request carries a token that expires in
    /// flight. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan RefreshBeforeExpiry { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>How the client secret travels to the token endpoint.</summary>
public enum ClientSecretTransport
{
    /// <summary>HTTP Basic authentication (<c>client_secret_basic</c>).</summary>
    BasicAuthentication = 0,

    /// <summary>Form fields in the request body (<c>client_secret_post</c>).</summary>
    RequestBody = 1,
}

/// <summary>A fixed API key sent in a header on every request.</summary>
public sealed class ClientApiKeyOptions
{
    /// <summary>Gets or sets the header. Defaults to <c>X-Api-Key</c>, the one <c>SharedKernel.Security.ApiKey</c> reads.</summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>Gets or sets the key. Required; keep it in a secret store, never in appsettings.json.</summary>
    public string? Value { get; set; }
}
