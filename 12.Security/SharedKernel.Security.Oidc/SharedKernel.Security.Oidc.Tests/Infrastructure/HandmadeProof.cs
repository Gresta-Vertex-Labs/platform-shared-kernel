using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharedKernel.Security.Oidc.Tests.Infrastructure;

// Builds DPoP proofs with any header and payload, for shapes DpopTestProofBuilder cannot produce.
//
// Proofs carry a "kid" in the header and in the embedded JWK. DpopProofValidator validates with
// TryAllIssuerSigningKeys = true, so spec-typical proofs without a kid are accepted too
// (DpopProofValidatorTests.ValidateAsync_ProofWithoutKid_Succeeds).
internal sealed class HandmadeProof
{
    public static readonly DateTimeOffset DefaultIssuedAt = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, object> _header = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _payload = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _jwk;
    private readonly ECDsa _key;
    private ECDsa? _signingKey;
    private string? _accessToken;
    private bool _includeJwk = true;

    public HandmadeProof(ECDsa key, string accessToken)
    {
        _key = key;
        _jwk = EcJwk(key);
        _accessToken = accessToken;
        _header["typ"] = "dpop+jwt";
        _header["alg"] = "ES256";
        _payload["jti"] = Guid.NewGuid().ToString("N");
        _payload["htm"] = "POST";
        _payload["htu"] = "https://api.example.test/resource";
        _payload["iat"] = DefaultIssuedAt.ToUnixTimeSeconds();
    }

    public static Dictionary<string, object> EcJwk(ECDsa key)
    {
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url.EncodeToString(parameters.Q.X),
            ["y"] = Base64Url.EncodeToString(parameters.Q.Y),
        };
    }

    public static string Thumbprint(ECDsa key)
    {
        Dictionary<string, object> jwk = EcJwk(key);
        string input = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{jwk["x"]}\",\"y\":\"{jwk["y"]}\"}}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    public static string Ath(string accessToken) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));

    public HandmadeProof WithMethod(string method) => WithClaim("htm", method);

    public HandmadeProof WithUri(string uri) => WithClaim("htu", uri);

    public HandmadeProof WithIssuedAt(DateTimeOffset issuedAt) => WithClaim("iat", issuedAt.ToUnixTimeSeconds());

    public HandmadeProof WithJti(string jti) => WithClaim("jti", jti);

    public HandmadeProof WithNonce(string nonce) => WithClaim("nonce", nonce);

    public HandmadeProof WithAccessToken(string? accessToken)
    {
        _accessToken = accessToken;
        return this;
    }

    public HandmadeProof WithClaim(string name, object value)
    {
        _payload[name] = value;
        return this;
    }

    public HandmadeProof WithoutClaim(string name)
    {
        _payload.Remove(name);
        if (name == "ath")
        {
            _accessToken = null;
        }

        return this;
    }

    public HandmadeProof WithHeader(string name, object value)
    {
        _header[name] = value;
        return this;
    }

    public HandmadeProof WithJwkMember(string name, object value)
    {
        _jwk[name] = value;
        return this;
    }

    public HandmadeProof WithoutJwk()
    {
        _includeJwk = false;
        return this;
    }

    public HandmadeProof SignedBy(ECDsa key)
    {
        _signingKey = key;
        return this;
    }

    public string Build()
    {
        var header = new Dictionary<string, object>(_header, StringComparer.Ordinal);
        var jwk = new Dictionary<string, object>(_jwk, StringComparer.Ordinal);
        header["kid"] = "proof-key";
        jwk["kid"] = "proof-key";

        if (_includeJwk && !header.ContainsKey("jwk"))
        {
            header["jwk"] = jwk;
        }

        var payload = new Dictionary<string, object>(_payload, StringComparer.Ordinal);
        if (_accessToken is not null && !payload.ContainsKey("ath"))
        {
            payload["ath"] = Ath(_accessToken);
        }

        string input = SigningInput(header, payload);
        byte[] signature = (_signingKey ?? _key).SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256);
        return $"{input}.{Base64Url.EncodeToString(signature)}";
    }

    public static string SignRs256(RSA key, string accessToken)
    {
        RSAParameters parameters = key.ExportParameters(includePrivateParameters: false);
        var jwk = new Dictionary<string, object>
        {
            ["kty"] = "RSA",
            ["n"] = Base64Url.EncodeToString(parameters.Modulus),
            ["e"] = Base64Url.EncodeToString(parameters.Exponent),
            ["kid"] = "proof-key",
        };
        var header = new Dictionary<string, object> { ["typ"] = "dpop+jwt", ["alg"] = "RS256", ["kid"] = "proof-key", ["jwk"] = jwk };

        var payload = new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["htm"] = "POST",
            ["htu"] = "https://api.example.test/resource",
            ["iat"] = DefaultIssuedAt.ToUnixTimeSeconds(),
            ["ath"] = Ath(accessToken),
        };

        string input = SigningInput(header, payload);
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{input}.{Base64Url.EncodeToString(signature)}";
    }

    public static string SignHs256(string accessToken)
    {
        var header = new Dictionary<string, object> { ["typ"] = "dpop+jwt", ["alg"] = "HS256", ["jwk"] = new Dictionary<string, object> { ["kty"] = "oct" } };
        var payload = new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["htm"] = "POST",
            ["htu"] = "https://api.example.test/resource",
            ["iat"] = DefaultIssuedAt.ToUnixTimeSeconds(),
            ["ath"] = Ath(accessToken),
        };

        string input = SigningInput(header, payload);
        return $"{input}.{Base64Url.EncodeToString(HMACSHA256.HashData(RandomNumberGenerator.GetBytes(32), Encoding.ASCII.GetBytes(input)))}";
    }

    public static string Unsigned(ECDsa key, string accessToken)
    {
        var header = new Dictionary<string, object> { ["typ"] = "dpop+jwt", ["alg"] = "none", ["jwk"] = EcJwk(key) };
        var payload = new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["htm"] = "POST",
            ["htu"] = "https://api.example.test/resource",
            ["iat"] = DefaultIssuedAt.ToUnixTimeSeconds(),
            ["ath"] = Ath(accessToken),
        };

        return $"{SigningInput(header, payload)}.";
    }

    private static string SigningInput(Dictionary<string, object> header, Dictionary<string, object> payload) =>
        $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload))}";
}
