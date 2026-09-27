using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication.Internal;

/// <summary>
/// Gets OAuth 2.0 client-credentials tokens and caches them per credential until shortly before they expire. One
/// request at a time per credential: concurrent callers wait for the token the first one fetches.
/// </summary>
internal sealed class ClientCredentialsTokenClient(
    IHttpClientFactory httpClientFactory,
    IClock clock,
    ILogger<ClientCredentialsTokenClient> logger)
{
    internal const string HttpClientName = "SharedKernel.Communication.TokenEndpoint";

    /// <summary>How long a token without <c>expires_in</c> is reused.</summary>
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    /// <summary>Returns a cached token, or fetches one.</summary>
    /// <param name="options">The credential.</param>
    /// <param name="rejectedToken">A token the service just answered 401 to: never returned again.</param>
    /// <param name="cancellationToken">Cancels the wait and the request.</param>
    public async ValueTask<Result<AccessToken>> GetTokenAsync(
        ClientCredentialsOptions options,
        string? rejectedToken,
        CancellationToken cancellationToken)
    {
        string key = CacheKey(options);
        if (TryGetUsable(key, rejectedToken, out AccessToken? cached))
        {
            return Result<AccessToken>.Success(cached);
        }

        SemaphoreSlim gate = _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have fetched a new token while this one waited.
            if (TryGetUsable(key, rejectedToken, out cached))
            {
                return Result<AccessToken>.Success(cached);
            }

            var (result, lifetime) = await RequestAsync(options, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                // Refresh ahead of expiry, but never later than half-way through a short-lived token.
                TimeSpan margin = TimeSpan.FromTicks(Math.Min(options.RefreshBeforeExpiry.Ticks, lifetime.Ticks / 2));
                _tokens[key] = new CachedToken(result.Value, clock.UtcNow + lifetime - margin);
            }

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetUsable(string key, string? rejectedToken, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AccessToken? token)
    {
        if (_tokens.TryGetValue(key, out CachedToken? cached)
            && clock.UtcNow < cached.RefreshAt
            && !string.Equals(cached.Token.Value, rejectedToken, StringComparison.Ordinal))
        {
            token = cached.Token;
            return true;
        }

        token = null;
        return false;
    }

    private async Task<(Result<AccessToken> Result, TimeSpan Lifetime)> RequestAsync(
        ClientCredentialsOptions options,
        CancellationToken cancellationToken)
    {
        Uri endpoint = options.TokenEndpoint!;
        var form = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials") };
        AddIfSet(form, "scope", options.Scope);
        AddIfSet(form, "audience", options.Audience);
        AddIfSet(form, "resource", options.Resource);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (options.SecretTransport == ClientSecretTransport.BasicAuthentication)
        {
            // RFC 6749 §2.3.1: the id and secret are form-encoded before they are joined and base64-encoded.
            string credentials = $"{Uri.EscapeDataString(options.ClientId!)}:{Uri.EscapeDataString(options.ClientSecret!)}";
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        }
        else
        {
            form.Add(new("client_id", options.ClientId!));
            form.Add(new("client_secret", options.ClientSecret!));
        }

        request.Content = new FormUrlEncodedContent(form);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            HttpClient http = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            TokenResponse? body = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body?.AccessToken))
            {
                CommunicationLog.TokenRequestRefused(logger, endpoint, options.ClientId!, (int)response.StatusCode, body?.Error ?? "none");
                return (Failure($"The token endpoint refused the client credentials (HTTP {(int)response.StatusCode}, error '{body?.Error ?? "none"}')."), TimeSpan.Zero);
            }

            TimeSpan lifetime = body.ExpiresIn is > 0 ? TimeSpan.FromSeconds(body.ExpiresIn.Value) : DefaultLifetime;
            string scheme = string.IsNullOrWhiteSpace(body.TokenType) || body.TokenType.Equals("bearer", StringComparison.OrdinalIgnoreCase)
                ? "Bearer"
                : body.TokenType;

            CommunicationLog.TokenAcquired(logger, endpoint, options.ClientId!, lifetime);
            return (Result<AccessToken>.Success(new AccessToken(body.AccessToken, clock.UtcNow + lifetime, scheme)), lifetime);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            CommunicationLog.TokenEndpointUnreachable(logger, endpoint, options.ClientId!, exception);
            return (Failure("The token endpoint could not be reached."), TimeSpan.Zero);
        }
    }

    private static async Task<TokenResponse?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, TokenJsonContext.Default.TokenResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Result<AccessToken> Failure(string message) =>
        Result<AccessToken>.Failure(Error.Unavailable(CommunicationErrorCodes.AccessTokenUnavailable, message));

    private static void AddIfSet(List<KeyValuePair<string, string>> form, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            form.Add(new(name, value));
        }
    }

    /// <summary>One entry per credential; the secret is hashed in, so a rotated secret never reuses the old token.</summary>
    private static string CacheKey(ClientCredentialsOptions o)
    {
        string secretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(o.ClientSecret ?? string.Empty)));
        return string.Join('\n', o.TokenEndpoint, o.ClientId, o.Scope, o.Audience, o.Resource, o.SecretTransport, secretHash);
    }

    private sealed record CachedToken(AccessToken Token, DateTimeOffset RefreshAt);
}

/// <summary>The members of an RFC 6749 §5.1 token response (and §5.2 error response) this client reads.</summary>
internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

[JsonSerializable(typeof(TokenResponse))]
[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]
internal sealed partial class TokenJsonContext : JsonSerializerContext;
