using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

/// <summary>
/// Proves <see cref="DpopTestProofBuilder"/> produces a genuine, independently-verifiable ES256-signed
/// RFC 9449 DPoP proof — not merely a three-dot-separated string that looks like one.
/// </summary>
/// <remarks>
/// Every signature-verification assertion below independently re-implements ES256 verification against
/// the embedded <see cref="DpopTestProof.PublicJwk"/>, entirely separately from
/// <see cref="DpopTestProofBuilder"/>'s own signing code — proving the proof is cryptographically real,
/// not merely well-formed JSON.
/// </remarks>
public sealed class DpopTestProofBuilderTests
{
    [Fact]
    public void Build_ProofJwt_HasThreeDotSeparatedSegments()
    {
        var proof = new DpopTestProofBuilder().Build();

        Assert.Equal(3, proof.ProofJwt.Split('.').Length);
    }

    [Fact]
    public void Build_ProofJwt_IsAGenuineIndependentlyVerifiableEs256Signature()
    {
        var proof = new DpopTestProofBuilder().Build();

        Assert.True(VerifySignature(proof.ProofJwt, proof.PublicJwk));
    }

    [Fact]
    public void Build_DefaultAth_EqualsBase64UrlSha256OfAccessToken()
    {
        var proof = new DpopTestProofBuilder().WithAccessToken("my-access-token").Build();

        var payload = DecodePayload(proof.ProofJwt);
        var expectedAth = Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes("my-access-token")));

        Assert.Equal(expectedAth, payload["ath"].GetString());
    }

    [Fact]
    public void WithMismatchedAth_AthDiffersFromRealHash_ButSignatureStaysGenuinelyValid()
    {
        var proof = new DpopTestProofBuilder().WithAccessToken("real-token").WithMismatchedAth().Build();

        var payload = DecodePayload(proof.ProofJwt);
        var realAth = Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes("real-token")));

        Assert.NotEqual(realAth, payload["ath"].GetString());
        Assert.True(VerifySignature(proof.ProofJwt, proof.PublicJwk));
    }

    [Fact]
    public void WithMissingAth_OmitsAthClaimEntirely()
    {
        var proof = new DpopTestProofBuilder().WithMissingAth().Build();

        var payload = DecodePayload(proof.ProofJwt);

        Assert.False(payload.ContainsKey("ath"));
        Assert.True(VerifySignature(proof.ProofJwt, proof.PublicJwk));
    }

    [Fact]
    public void WithMalformedAth_SetsAthToRawCallerSuppliedValue()
    {
        var proof = new DpopTestProofBuilder().WithMalformedAth("not-a-real-hash").Build();

        var payload = DecodePayload(proof.ProofJwt);

        Assert.Equal("not-a-real-hash", payload["ath"].GetString());
        Assert.True(VerifySignature(proof.ProofJwt, proof.PublicJwk));
    }

    [Fact]
    public void Build_DefaultIssuedAt_IsReproducibleAcrossSeparatelyConstructedBuilders()
    {
        var iatA = DecodePayload(new DpopTestProofBuilder().Build().ProofJwt)["iat"].GetInt64();
        var iatB = DecodePayload(new DpopTestProofBuilder().Build().ProofJwt)["iat"].GetInt64();

        Assert.Equal(iatA, iatB);
    }

    [Fact]
    public void Build_ExplicitIssuedAt_IsHonored()
    {
        var issuedAt = new DateTimeOffset(2025, 6, 15, 8, 30, 0, TimeSpan.Zero);

        var proof = new DpopTestProofBuilder().WithIssuedAt(issuedAt).Build();

        var iat = DecodePayload(proof.ProofJwt)["iat"].GetInt64();
        Assert.Equal(issuedAt.ToUnixTimeSeconds(), iat);
    }

    [Fact]
    public void Build_DefaultJti_IsUniqueAcrossDifferentBuilderInstances()
    {
        var jtiA = DecodePayload(new DpopTestProofBuilder().Build().ProofJwt)["jti"].GetString();
        var jtiB = DecodePayload(new DpopTestProofBuilder().Build().ProofJwt)["jti"].GetString();

        Assert.NotEqual(jtiA, jtiB);
    }

    [Fact]
    public void Build_ExplicitJti_IsHonored()
    {
        var proof = new DpopTestProofBuilder().WithJti("explicit-jti-value").Build();

        Assert.Equal("explicit-jti-value", DecodePayload(proof.ProofJwt)["jti"].GetString());
    }

    [Fact]
    public void Header_CarriesTypAlgAndEmbeddedJwk()
    {
        var proof = new DpopTestProofBuilder().Build();

        var header = DecodeHeader(proof.ProofJwt);

        Assert.Equal("dpop+jwt", header["typ"].GetString());
        Assert.Equal("ES256", header["alg"].GetString());
        Assert.True(header.ContainsKey("jwk"));
    }

    [Fact]
    public void Payload_CarriesConfiguredHtmAndHtu()
    {
        var proof = new DpopTestProofBuilder()
            .WithHttpMethod("PUT")
            .WithHttpUri("https://api.example.test/orders/1")
            .Build();

        var payload = DecodePayload(proof.ProofJwt);

        Assert.Equal("PUT", payload["htm"].GetString());
        Assert.Equal("https://api.example.test/orders/1", payload["htu"].GetString());
    }

    [Fact]
    public void Build_ReturnedAccessToken_MatchesConfiguredValue()
    {
        var proof = new DpopTestProofBuilder().WithAccessToken("configured-token").Build();

        Assert.Equal("configured-token", proof.AccessToken);
    }

    private static bool VerifySignature(string proofJwt, string publicJwkJson)
    {
        var parts = proofJwt.Split('.');
        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64UrlDecode(parts[2]);

        using var doc = JsonDocument.Parse(publicJwkJson);
        var root = doc.RootElement;
        var x = Base64UrlDecode(root.GetProperty("x").GetString()!);
        var y = Base64UrlDecode(root.GetProperty("y").GetString()!);

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y },
        };

        using var ecdsa = ECDsa.Create(parameters);
        return ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256);
    }

    private static Dictionary<string, JsonElement> DecodeHeader(string proofJwt) =>
        DecodeSegment(proofJwt.Split('.')[0]);

    private static Dictionary<string, JsonElement> DecodePayload(string proofJwt) =>
        DecodeSegment(proofJwt.Split('.')[1]);

    private static Dictionary<string, JsonElement> DecodeSegment(string base64UrlSegment)
    {
        var json = Encoding.UTF8.GetString(Base64UrlDecode(base64UrlSegment));
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
