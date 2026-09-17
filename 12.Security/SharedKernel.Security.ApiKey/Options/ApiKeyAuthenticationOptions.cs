using Microsoft.AspNetCore.Authentication;

namespace SharedKernel.Security.ApiKey.Options;

/// <summary>Options for the API key authentication scheme.</summary>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>Gets or sets the request header carrying the key. Defaults to <c>X-Api-Key</c>.</summary>
    /// <remarks>Keys are never read from the query string, which ends up in access logs and browser history.</remarks>
    public string HeaderName { get; set; } = ApiKeyAuthenticationDefaults.HeaderName;
}
