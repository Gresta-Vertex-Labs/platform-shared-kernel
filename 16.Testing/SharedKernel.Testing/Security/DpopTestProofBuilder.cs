using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharedKernel.Testing.Security;

/// <summary>
/// Fluent builder that constructs a genuinely well-formed RFC 9449 DPoP proof JWT for use as an input
/// fixture in a consuming service's own integration test.
/// </summary>
/// <remarks>
/// <para>
/// Produces a real ES256 signature over a freshly generated BCL <see cref="ECDsa"/> P-256 key pair,
/// hand-rolled base64url <c>header.payload.signature</c> — zero third-party JWT library dependency.
/// Only <see cref="WithMismatchedAth"/>/<see cref="WithMissingAth"/>/<see cref="WithMalformedAth"/> ever
/// corrupt the <c>ath</c> claim; every other binding (<c>htm</c>/<c>htu</c>/<c>iat</c>/<c>jti</c>/the
/// embedded <c>jwk</c>) is always correctly formed, so a consuming integration test proves GENUINE
/// rejection logic against <c>12.Security</c>'s real (internal, unreachable from this package under any
/// circumstance) <c>DpopProofValidator</c>, never a strawman.
/// </para>
/// <para>
/// References ZERO <c>SharedKernel.Security.Oidc</c> types — this builder's entire job is producing a
/// presentable INPUT FIXTURE (proof JWT + companion access token) for a consuming test's own real, wired
/// up DPoP validation pipeline, never a fake OF the validator. Built entirely on BCL
/// <see cref="System.Security.Cryptography"/>/<see cref="System.Text.Json"/> — no new
/// <c>PackageReference</c>.
/// </para>
/// <para>
/// The freshly generated ECDsa key pair (and, for <see cref="WithMismatchedAth"/>'s deviation, the
/// SHA-256 content hash) are inherent cryptographic material — not a violation of this package's
/// determinism convention, which governs test-assertion-relevant defaults (<c>iat</c>, <c>jti</c>,
/// access-token string), all of which default to fixed/deterministic values below.
/// </para>
/// </remarks>
public sealed class DpopTestProofBuilder
{
    private const string HeaderType = "dpop+jwt";
    private const string Algorithm = "ES256";
    private const string DefaultAccessToken = "dpop-test-access-token";

    private static readonly DateTimeOffset DefaultIssuedAt = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static int _jtiSequence;

    private readonly int _instanceSequence = Interlocked.Increment(ref _jtiSequence);

    private string _httpMethod = "POST";
    private string _httpUri = "https://api.example.test/resource";
    private DateTimeOffset _issuedAt = DefaultIssuedAt;
    private string _jti;
    private string _accessToken = DefaultAccessToken;
    private AthMode _athMode = AthMode.Correct;
    private string? _malformedAthValue;

    /// <summary>Initializes a new <see cref="DpopTestProofBuilder"/>.</summary>
    public DpopTestProofBuilder()
    {
        _jti = $"dpop-test-jti-{_instanceSequence:D6}";
    }

    /// <summary>Sets the DPoP <c>htm</c> (HTTP method) claim. Defaults to <c>"POST"</c>.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public DpopTestProofBuilder WithHttpMethod(string htm = "POST")
    {
        ArgumentNullException.ThrowIfNull(htm);
        _httpMethod = htm;
        return this;
    }

    /// <summary>Sets the DPoP <c>htu</c> (HTTP target URI, without query/fragment) claim.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public DpopTestProofBuilder WithHttpUri(string htu)
    {
        ArgumentNullException.ThrowIfNull(htu);
        _httpUri = htu;
        return this;
    }

    /// <summary>
    /// Sets the DPoP <c>iat</c> (issued-at) claim. Defaults to a FIXED, non-real baseline instant —
    /// never <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public DpopTestProofBuilder WithIssuedAt(DateTimeOffset iat)
    {
        _issuedAt = iat;
        return this;
    }

    /// <summary>
    /// Sets the DPoP <c>jti</c> (unique proof identifier) claim. Defaults to a deterministic
    /// per-instance incrementing sequence — never <see cref="Guid.NewGuid"/>.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public DpopTestProofBuilder WithJti(string jti)
    {
        ArgumentNullException.ThrowIfNull(jti);
        _jti = jti;
        return this;
    }

    /// <summary>
    /// Sets the companion bearer access token the proof binds to via <c>ath</c>. Defaults to a fixed
    /// deterministic test-token string.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public DpopTestProofBuilder WithAccessToken(string accessToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        _accessToken = accessToken;
        return this;
    }

    /// <summary>
    /// Negative path: emits an <c>ath</c> claim provably NOT equal to
    /// <c>base64url(SHA-256(AccessToken))</c>.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public DpopTestProofBuilder WithMismatchedAth()
    {
        _athMode = AthMode.Mismatched;
        return this;
    }

    /// <summary>Negative path: omits the <c>ath</c> claim from the payload entirely.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public DpopTestProofBuilder WithMissingAth()
    {
        _athMode = AthMode.Missing;
        return this;
    }

    /// <summary>Negative path: sets <c>ath</c> to a caller-supplied, non-base64url raw string.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public DpopTestProofBuilder WithMalformedAth(string rawValue)
    {
        ArgumentNullException.ThrowIfNull(rawValue);
        _athMode = AthMode.Malformed;
        _malformedAthValue = rawValue;
        return this;
    }

    /// <summary>
    /// Builds a genuinely well-formed, ES256-signed RFC 9449 DPoP proof JWT (unless a negative-path
    /// <c>ath</c> method was called).
    /// </summary>
    public DpopTestProof Build()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicParameters = ecdsa.ExportParameters(includePrivateParameters: false);

        var x = Base64UrlEncode(publicParameters.Q.X!);
        var y = Base64UrlEncode(publicParameters.Q.Y!);

        var jwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = x,
            ["y"] = y,
        };
        var publicJwkJson = JsonSerializer.Serialize(jwk);

        var header = new Dictionary<string, object>
        {
            ["typ"] = HeaderType,
            ["alg"] = Algorithm,
            ["jwk"] = jwk,
        };

        var payload = new Dictionary<string, object>
        {
            ["jti"] = _jti,
            ["htm"] = _httpMethod,
            ["htu"] = _httpUri,
            ["iat"] = _issuedAt.ToUnixTimeSeconds(),
        };

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
            case AthMode.Missing:
                break;
            default:
                throw new InvalidOperationException($"Unknown ath mode '{_athMode}'.");
        }

        var encodedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header)));
        var encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        var signingInput = $"{encodedHeader}.{encodedPayload}";

        var signature = ecdsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256);
        var proofJwt = $"{signingInput}.{Base64UrlEncode(signature)}";

        return new DpopTestProof(proofJwt, _accessToken, publicJwkJson);
    }

    private static string ComputeAth(string accessToken) =>
        Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private enum AthMode
    {
        Correct,
        Mismatched,
        Missing,
        Malformed,
    }
}

/// <summary>Return type of <see cref="DpopTestProofBuilder.Build"/>.</summary>
/// <param name="ProofJwt">The full DPoP proof, <c>"header.payload.signature"</c>.</param>
/// <param name="AccessToken">The companion bearer access-token string the proof is bound to.</param>
/// <param name="PublicJwk">
/// The JWK JSON embedded in the proof header — lets a consuming test independently verify <c>jkt</c>
/// binding.
/// </param>
public sealed record DpopTestProof(string ProofJwt, string AccessToken, string PublicJwk);
