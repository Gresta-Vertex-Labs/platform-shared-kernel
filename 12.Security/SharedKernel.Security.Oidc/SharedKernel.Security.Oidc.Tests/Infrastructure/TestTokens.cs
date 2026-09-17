using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SharedKernel.Security.Oidc.Tests.Infrastructure;

internal static class TestTokens
{
    public const string Issuer = "https://issuer.example.test";
    public const string Audience = "api://orders";

    public static readonly RsaSecurityKey RsaKey = new(RSA.Create(2048)) { KeyId = "rsa-1" };

    public static readonly ECDsaSecurityKey EcKey = new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "ec-1" };

    // Listed as a provider signing key so a rejection proves the algorithm allow-list, not a missing key.
    public static readonly SymmetricSecurityKey SymmetricKey = new(RandomNumberGenerator.GetBytes(64)) { KeyId = "hs-1" };

    public static TokenBuilder Create() => new();

    public static string Sha256Base64Url(string value) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));

    public static string CertificateThumbprint(X509Certificate2 certificate) =>
        Base64Url.EncodeToString(SHA256.HashData(certificate.RawData));

    // An unsigned token ("alg": "none"), which JsonWebTokenHandler refuses to create.
    public static string Unsigned(IReadOnlyDictionary<string, object> payload)
    {
        string header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["alg"] = "none", ["typ"] = "JWT" }));
        string body = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{body}.";
    }

    public static Dictionary<string, object> StandardPayload(string subject = "user-1")
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = subject,
            ["iat"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
        };
    }
}

internal sealed class TokenBuilder
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly Dictionary<string, object> _claims = new(StringComparer.Ordinal) { ["sub"] = "user-1" };
    private string _issuer = TestTokens.Issuer;
    private string _audience = TestTokens.Audience;
    private DateTime _notBefore = DateTime.UtcNow.AddMinutes(-1);
    private DateTime _expires = DateTime.UtcNow.AddHours(1);
    private SigningCredentials _credentials = new(TestTokens.RsaKey, SecurityAlgorithms.RsaSha256);
    private string? _type;

    public TokenBuilder WithClaim(string type, object value)
    {
        _claims[type] = value;
        return this;
    }

    public TokenBuilder WithoutClaim(string type)
    {
        _claims.Remove(type);
        return this;
    }

    public TokenBuilder WithIssuer(string issuer)
    {
        _issuer = issuer;
        return this;
    }

    public TokenBuilder WithAudience(string audience)
    {
        _audience = audience;
        return this;
    }

    public TokenBuilder WithLifetime(DateTime notBefore, DateTime expires)
    {
        _notBefore = notBefore;
        _expires = expires;
        return this;
    }

    public TokenBuilder SignedWith(SecurityKey key, string algorithm)
    {
        _credentials = new SigningCredentials(key, algorithm);
        return this;
    }

    public TokenBuilder WithType(string type)
    {
        _type = type;
        return this;
    }

    public TokenBuilder BoundToDpopKey(string jwkThumbprint) =>
        WithClaim("cnf", new Dictionary<string, object> { ["jkt"] = jwkThumbprint });

    public TokenBuilder BoundToCertificate(X509Certificate2 certificate) =>
        WithClaim("cnf", new Dictionary<string, object> { ["x5t#S256"] = TestTokens.CertificateThumbprint(certificate) });

    public string Build() =>
        Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            NotBefore = _notBefore,
            IssuedAt = _notBefore,
            Expires = _expires,
            Claims = new Dictionary<string, object>(_claims, StringComparer.Ordinal),
            SigningCredentials = _credentials,
            TokenType = _type,
        });
}
