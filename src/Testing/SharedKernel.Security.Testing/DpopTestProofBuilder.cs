using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharedKernel.Testing.Security;

/// <summary>Builds a signed DPoP proof (RFC 9449) for tests.</summary>
/// <remarks>
/// <para>
/// Signs with ES256 over a P-256 key: a new key per builder, or the key passed to <see cref="WithKey"/> so several
/// proofs share one key, as a real client's do. <see cref="DpopTestProof.JwkThumbprint"/> is the value to put in
/// the access token's <c>cnf.jkt</c> claim.
/// </para>
/// <para>
/// Defaults are fixed (<c>iat</c> 2024-01-01T00:00:00Z, a sequential <c>jti</c>, access token
/// <c>dpop-test-access-token</c>), so pair the proof with a fake clock.
/// </para>
/// </remarks>
public sealed class DpopTestProofBuilder
{
    private const string DefaultAccessToken = "dpop-test-access-token";

    private static readonly DateTimeOffset DefaultIssuedAt = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static int _jtiSequence;

    private ECDsa? _key;
    private string _httpMethod = "POST";
    private string _httpUri = "https://api.example.test/resource";
    private DateTimeOffset _issuedAt = DefaultIssuedAt;
    private string _jti = $"dpop-test-jti-{Interlocked.Increment(ref _jtiSequence):D6}";
    private string _accessToken = DefaultAccessToken;
    private string? _nonce;
    private string _type = "dpop+jwt";
    private AthMode _athMode = AthMode.Correct;
    private string? _malformedAthValue;

    /// <summary>Signs with <paramref name="key"/> instead of a new key. The builder does not dispose it.</summary>
    /// <param name="key">A P-256 key.</param>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithKey(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _key = key;
        return this;
    }

    /// <summary>Sets the <c>htm</c> claim. Defaults to <c>POST</c>.</summary>
    /// <param name="htm">The HTTP method.</param>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithHttpMethod(string htm = "POST")
    {
        ArgumentNullException.ThrowIfNull(htm);
        _httpMethod = htm;
        return this;
    }

    /// <summary>Sets the <c>htu</c> claim. Defaults to <c>https://api.example.test/resource</c>.</summary>
    /// <param name="htu">The request URI.</param>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithHttpUri(string htu)
    {
        ArgumentNullException.ThrowIfNull(htu);
        _httpUri = htu;
        return this;
    }

    /// <summary>Sets the <c>iat</c> claim.</summary>
    /// <param name="iat">The issue time.</param>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithIssuedAt(DateTimeOffset iat)
    {
        _issuedAt = iat;
        return this;
    }

    /// <summary>Sets the <c>jti</c> claim.</summary>
    /// <param name="jti">The proof id.</param>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithJti(string jti)
    {
        ArgumentNullException.ThrowIfNull(jti);
        _jti = jti;
        return this;
    }

    /// <summary>Sets the access token the <c>ath</c> claim is computed from.</summary>
    /// <param name="accessToken">The encoded access token.</param>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithAccessToken(string accessToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        _accessToken = accessToken;
        return this;
    }

    /// <summary>Sets the <c>nonce</c> claim.</summary>
    /// <param name="nonce">The server-issued nonce.</param>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithNonce(string nonce)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        _nonce = nonce;
        return this;
    }

    /// <summary>Sets the <c>typ</c> header. Defaults to <c>dpop+jwt</c>.</summary>
    /// <param name="type">The header value.</param>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
        return this;
    }

    /// <summary>Computes <c>ath</c> from a different token.</summary>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithMismatchedAth()
    {
        _athMode = AthMode.Mismatched;
        return this;
    }

    /// <summary>Omits <c>ath</c>.</summary>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithMissingAth()
    {
        _athMode = AthMode.Missing;
        return this;
    }

    /// <summary>Sets <c>ath</c> to a raw value.</summary>
    /// <param name="rawValue">The value.</param>
    /// <returns>The same builder.</returns>
    public DpopTestProofBuilder WithMalformedAth(string rawValue)
    {
        ArgumentNullException.ThrowIfNull(rawValue);
        _athMode = AthMode.Malformed;
        _malformedAthValue = rawValue;
        return this;
    }

    /// <summary>Builds the signed proof.</summary>
    /// <returns>The proof, the access token and the key's JWK and thumbprint.</returns>
    public DpopTestProof Build()
    {
        ECDsa key = _key ?? ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
            string x = Base64Url.EncodeToString(parameters.Q.X);
            string y = Base64Url.EncodeToString(parameters.Q.Y);

            // RFC 7638: the thumbprint input has the required members only, in lexicographic order, without spaces.
            string thumbprintInput = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
            string thumbprint = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(thumbprintInput)));

            var jwk = new Dictionary<string, string> { ["kty"] = "EC", ["crv"] = "P-256", ["x"] = x, ["y"] = y };
            var header = new Dictionary<string, object> { ["typ"] = _type, ["alg"] = "ES256", ["jwk"] = jwk };
            var payload = new Dictionary<string, object>
            {
                ["jti"] = _jti,
                ["htm"] = _httpMethod,
                ["htu"] = _httpUri,
                ["iat"] = _issuedAt.ToUnixTimeSeconds(),
            };

            if (_nonce is not null)
            {
                payload["nonce"] = _nonce;
            }

            switch (_athMode)
            {
                case AthMode.Correct:
                    payload["ath"] = ComputeAth(_accessToken);
                    break;
                case AthMode.Mismatched:
                    payload["ath"] = ComputeAth(_accessToken + "-tampered");
                    break;
                case AthMode.Malformed:
                    payload["ath"] = _malformedAthValue!;
                    break;
            }

            string signingInput =
                $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload))}";
            byte[] signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);

            return new DpopTestProof($"{signingInput}.{Base64Url.EncodeToString(signature)}", _accessToken, JsonSerializer.Serialize(jwk), thumbprint);
        }
        finally
        {
            if (_key is null)
            {
                key.Dispose();
            }
        }
    }

    private static string ComputeAth(string accessToken) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));

    private enum AthMode
    {
        Correct,
        Mismatched,
        Missing,
        Malformed,
    }
}

/// <summary>A DPoP proof built by <see cref="DpopTestProofBuilder"/>.</summary>
/// <param name="ProofJwt">The proof, the value of the <c>DPoP</c> header.</param>
/// <param name="AccessToken">The access token the proof's <c>ath</c> claim refers to.</param>
/// <param name="PublicJwk">The signing key's public JWK as JSON.</param>
/// <param name="JwkThumbprint">The key's RFC 7638 thumbprint, the access token's <c>cnf.jkt</c>.</param>
public sealed record DpopTestProof(string ProofJwt, string AccessToken, string PublicJwk, string JwkThumbprint);
