using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Security.Oidc.Internal;
using SharedKernel.Security.Oidc.Options;

namespace SharedKernel.Security.Oidc.Dpop;

// Checks a DPoP proof against the request and the access token it accompanies (RFC 9449 section 4.3).
internal sealed class DpopProofValidator(IDpopReplayCache replayCache, DpopNonceService nonces)
{
    internal const string InvalidProof = "invalid_dpop_proof";
    internal const string UseNonce = "use_dpop_nonce";

    private const string ProofType = "dpop+jwt";
    private const int MaxJtiLength = 256;
    private const int MinRsaKeyBits = 2048;

    private static readonly JsonWebTokenHandler Handler = new() { MapInboundClaims = false };

    // Members a public JWK must not carry: RSA and EC private parts, and symmetric key material.
    private static readonly string[] PrivateKeyMembers = ["d", "p", "q", "dp", "dq", "qi", "oth", "k"];

    public async Task<DpopResult> ValidateAsync(
        HttpContext context,
        string accessToken,
        string expectedJwkThumbprint,
        DpopOptions options,
        DateTimeOffset now)
    {
        StringValues headers = context.Request.Headers[OidcAuthenticationDefaults.DpopScheme];
        if (headers.Count != 1 || string.IsNullOrWhiteSpace(headers[0]))
        {
            return DpopResult.Fail(InvalidProof, headers.Count > 1 ? "MultipleProofs" : "MissingProof");
        }

        string proof = headers[0]!;
        JsonWebToken proofJwt;
        try
        {
            proofJwt = Handler.ReadJsonWebToken(proof);
        }
        catch (Exception ex) when (ex is SecurityTokenMalformedException or ArgumentException)
        {
            return DpopResult.Fail(InvalidProof, "MalformedProof");
        }

        IReadOnlyList<string> algorithms = OidcDefaults.ValidAlgorithms(options);
        if (!algorithms.Contains(proofJwt.Alg, StringComparer.Ordinal))
        {
            return DpopResult.Fail(InvalidProof, "AlgorithmNotAllowed");
        }

        if (!string.Equals(proofJwt.Typ, ProofType, StringComparison.OrdinalIgnoreCase))
        {
            return DpopResult.Fail(InvalidProof, "InvalidType");
        }

        if (!TryReadPublicKey(proofJwt, out JsonWebKey? key, out string? keyFailure))
        {
            return DpopResult.Fail(InvalidProof, keyFailure);
        }

        TokenValidationResult signature = await Handler.ValidateTokenAsync(proof, new TokenValidationParameters
        {
            IssuerSigningKey = key,
            ValidAlgorithms = algorithms,
            RequireSignedTokens = true,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            RequireExpirationTime = false,

            // Proofs carry no kid; without this IdentityModel ignores IssuerSigningKey. It is the only candidate.
            TryAllIssuerSigningKeys = true,
        }).ConfigureAwait(false);

        if (!signature.IsValid)
        {
            return DpopResult.Fail(InvalidProof, "InvalidSignature");
        }

        if (!proofJwt.TryGetPayloadValue("htm", out string? method)
            || !string.Equals(method, context.Request.Method, StringComparison.Ordinal))
        {
            return DpopResult.Fail(InvalidProof, "MethodMismatch");
        }

        if (!proofJwt.TryGetPayloadValue("htu", out string? uri) || !UriMatches(uri, context.Request))
        {
            return DpopResult.Fail(InvalidProof, "UriMismatch");
        }

        if (!proofJwt.TryGetPayloadValue("iat", out long issuedAtSeconds))
        {
            return DpopResult.Fail(InvalidProof, "MissingIssuedAt");
        }

        DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
        if (issuedAt > now + options.ClockSkew || issuedAt < now - options.ProofLifetime - options.ClockSkew)
        {
            return DpopResult.Fail(InvalidProof, "Expired");
        }

        if (!proofJwt.TryGetPayloadValue("jti", out string? jti) || string.IsNullOrEmpty(jti) || jti.Length > MaxJtiLength)
        {
            return DpopResult.Fail(InvalidProof, "InvalidJti");
        }

        string thumbprint = Base64Url.EncodeToString(key.ComputeJwkThumbprint());
        if (!string.Equals(thumbprint, expectedJwkThumbprint, StringComparison.Ordinal))
        {
            return DpopResult.Fail(InvalidProof, "KeyMismatch");
        }

        string expectedHash = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
        if (!proofJwt.TryGetPayloadValue("ath", out string? tokenHash)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedHash), Encoding.ASCII.GetBytes(tokenHash ?? string.Empty)))
        {
            return DpopResult.Fail(InvalidProof, "AccessTokenHashMismatch");
        }

        if (options.RequireNonce)
        {
            proofJwt.TryGetPayloadValue("nonce", out string? nonce);
            if (!nonces.IsValid(nonce))
            {
                return DpopResult.Fail(UseNonce, "NonceInvalid");
            }
        }

        string proofId = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes($"{thumbprint}.{jti}")));
        DateTimeOffset retainUntil = issuedAt + options.ProofLifetime + options.ClockSkew;
        bool firstUse;
        try
        {
            firstUse = await replayCache.TryAddAsync(proofId, retainUntil, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DpopResult.Fail(InvalidProof, "ReplayCacheUnavailable");
        }

        if (!firstUse)
        {
            return DpopResult.Fail(InvalidProof, "Replayed");
        }

        return DpopResult.Success;
    }

    private static bool TryReadPublicKey(
        JsonWebToken proof,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JsonWebKey? key,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? failure)
    {
        key = null;
        try
        {
            using JsonDocument header = JsonDocument.Parse(Base64Url.DecodeFromChars(proof.EncodedHeader));
            if (!header.RootElement.TryGetProperty("jwk", out JsonElement jwk) || jwk.ValueKind != JsonValueKind.Object)
            {
                failure = "MissingKey";
                return false;
            }

            foreach (string member in PrivateKeyMembers)
            {
                if (jwk.TryGetProperty(member, out _))
                {
                    failure = "PrivateKeyInProof";
                    return false;
                }
            }

            key = JsonWebKey.Create(jwk.GetRawText());
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            failure = "MalformedKey";
            return false;
        }

        switch (key.Kty)
        {
            case JsonWebAlgorithmsKeyTypes.EllipticCurve:
                break;
            case JsonWebAlgorithmsKeyTypes.RSA when string.IsNullOrEmpty(key.N) || Base64Url.DecodeFromChars(key.N).Length * 8 < MinRsaKeyBits:
                failure = "WeakKey";
                return false;
            case JsonWebAlgorithmsKeyTypes.RSA:
                break;
            default:
                failure = "UnsupportedKeyType";
                return false;
        }

        failure = null;
        return true;
    }

    // RFC 9449 section 4.3 check 9: compare with the request URI, ignoring query and fragment, after syntax-based
    // normalization (case-insensitive scheme and host, default port omitted).
    private static bool UriMatches(string? htu, HttpRequest request)
    {
        if (!Uri.TryCreate(htu, UriKind.Absolute, out Uri? claimed)
            || !Uri.TryCreate(UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, request.Path), UriKind.Absolute, out Uri? actual))
        {
            return false;
        }

        return Uri.Compare(claimed, actual, UriComponents.SchemeAndServer, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0
            && Uri.Compare(claimed, actual, UriComponents.Path, UriFormat.Unescaped, StringComparison.Ordinal) == 0;
    }
}

internal readonly record struct DpopResult(bool IsValid, string? Error, string? Reason)
{
    public static DpopResult Success => new(true, null, null);

    public static DpopResult Fail(string error, string reason) => new(false, error, reason);
}
