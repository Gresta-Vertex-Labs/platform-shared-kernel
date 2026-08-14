using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Security.Oidc.Logging;

namespace SharedKernel.Security.Oidc.Dpop;

/// <summary>
/// Validates DPoP (RFC 9449) sender-constrained proof JWTs against the current HTTP request and the
/// access token's <c>cnf.jkt</c> confirmation claim.
/// </summary>
/// <remarks>
/// <para>
/// Runs inside <c>JwtBearerEvents.OnTokenValidated</c>, after standard signature/issuer/audience/lifetime
/// validation succeeds, and only when <c>SecurityAuthenticationBuilder.RequireDpop&lt;TReplayCache&gt;()</c>
/// was called.
/// </para>
/// <para>
/// Parses the request's <c>DPoP</c> header as a <c>typ: dpop+jwt</c> proof JWT, verifies its embedded
/// <c>jwk</c> signature, validates <c>htm</c>/<c>htu</c>/<c>iat</c> freshness, computes
/// <c>jkt</c> (base64url SHA-256 thumbprint of the canonical JWK) and compares it against the access
/// token's <c>cnf.jkt</c> confirmation claim, and consults the request-scoped
/// <see cref="IDpopProofReplayCache"/> resolved from <c>HttpContext.RequestServices</c> (WO-058, C-30).
/// </para>
/// </remarks>
internal static class DpopProofValidator
{
    private const string ProofHeaderName = "DPoP";
    private const string ExpectedTyp = "dpop+jwt";
    private const string ConfirmationClaimType = "cnf";
    private const string JktPropertyName = "jkt";
    private const string LoggerCategoryName = "SharedKernel.Security.Oidc.Dpop.DpopProofValidator";

    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>
    /// Validates the DPoP proof presented on the current request against <paramref name="context"/>'s
    /// already-validated access token, failing <paramref name="context"/> on any violation.
    /// </summary>
    /// <param name="context">The <c>OnTokenValidated</c> context for the current request.</param>
    internal static async Task ValidateAsync(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = context.HttpContext.RequestServices;
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategoryName);

        if (!context.HttpContext.Request.Headers.TryGetValue(ProofHeaderName, out var headerValues)
            || headerValues.Count == 0
            || string.IsNullOrWhiteSpace(headerValues[0]))
        {
            Reject(context, logger, "MissingProof");
            return;
        }

        var proofToken = headerValues[0]!;

        JsonWebToken proofJwt;
        try
        {
            proofJwt = Handler.ReadJsonWebToken(proofToken);
        }
        catch (Exception ex) when (ex is SecurityTokenMalformedException or ArgumentException or FormatException)
        {
            Reject(context, logger, "MalformedProof");
            return;
        }

        if (!proofJwt.TryGetHeaderValue<string>("typ", out var typ) || !string.Equals(typ, ExpectedTyp, StringComparison.Ordinal))
        {
            Reject(context, logger, "InvalidTyp");
            return;
        }

        JsonWebKey jwk;
        try
        {
            var headerJson = Base64UrlEncoder.Decode(proofJwt.EncodedHeader);
            using var headerDoc = JsonDocument.Parse(headerJson);
            if (!headerDoc.RootElement.TryGetProperty("jwk", out var jwkElement))
            {
                Reject(context, logger, "MissingJwk");
                return;
            }

            jwk = JsonWebKey.Create(jwkElement.GetRawText());
        }
        catch (JsonException)
        {
            Reject(context, logger, "MalformedProof");
            return;
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            RequireSignedTokens = true,
            IssuerSigningKey = jwk,
        };

        TokenValidationResult validationResult;
        try
        {
            validationResult = await Handler.ValidateTokenAsync(proofToken, validationParameters).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Reject(context, logger, "InvalidSignature");
            return;
        }

        if (!validationResult.IsValid)
        {
            Reject(context, logger, "InvalidSignature");
            return;
        }

        if (!proofJwt.TryGetPayloadValue<string>("htm", out var htm)
            || !string.Equals(htm, context.HttpContext.Request.Method, StringComparison.OrdinalIgnoreCase))
        {
            Reject(context, logger, "HtmMismatch");
            return;
        }

        var expectedHtu = $"{context.HttpContext.Request.Scheme}://{context.HttpContext.Request.Host}{context.HttpContext.Request.PathBase}{context.HttpContext.Request.Path}";
        if (!proofJwt.TryGetPayloadValue<string>("htu", out var htu) || !string.Equals(htu, expectedHtu, StringComparison.Ordinal))
        {
            Reject(context, logger, "HtuMismatch");
            return;
        }

        if (!proofJwt.TryGetPayloadValue<long>("iat", out var iatSeconds))
        {
            Reject(context, logger, "MissingIat");
            return;
        }

        var dpopOptions = services.GetRequiredService<IOptions<DpopOptions>>().Value;
        var proofIssuedAt = DateTimeOffset.FromUnixTimeSeconds(iatSeconds);
        var freshnessWindow = TimeSpan.FromSeconds(dpopOptions.ProofFreshnessWindowSeconds);
        var age = DateTimeOffset.UtcNow - proofIssuedAt;
        if (age < TimeSpan.Zero)
        {
            age = -age;
        }

        if (age > freshnessWindow)
        {
            Reject(context, logger, "Expired");
            return;
        }

        if (!proofJwt.TryGetPayloadValue<string>("jti", out var jti) || string.IsNullOrWhiteSpace(jti))
        {
            Reject(context, logger, "MissingJti");
            return;
        }

        byte[] thumbprint;
        try
        {
            thumbprint = jwk.ComputeJwkThumbprint();
        }
        catch (Exception)
        {
            Reject(context, logger, "InvalidJwk");
            return;
        }

        var jkt = Base64UrlEncoder.Encode(thumbprint);
        var expectedJkt = TryReadConfirmationJkt(context.Principal);

        if (expectedJkt is null || !string.Equals(jkt, expectedJkt, StringComparison.Ordinal))
        {
            Reject(context, logger, "JktMismatch");
            return;
        }

        var replayCache = services.GetRequiredService<IDpopProofReplayCache>();
        var proofExpiresAt = proofIssuedAt + freshnessWindow;
        var isFirstUse = await replayCache.TryConsumeAsync(jti, proofExpiresAt, context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (!isFirstUse)
        {
            Reject(context, logger, "Replayed");
            return;
        }

        if (context.Principal?.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim(DpopClaimTypes.SenderConstrained, bool.TrueString));
        }
    }

    private static string? TryReadConfirmationJkt(ClaimsPrincipal? principal)
    {
        var cnfClaim = principal?.FindFirst(ConfirmationClaimType);
        if (cnfClaim is null)
        {
            return null;
        }

        try
        {
            using var cnfDoc = JsonDocument.Parse(cnfClaim.Value);
            return cnfDoc.RootElement.TryGetProperty(JktPropertyName, out var jktElement)
                ? jktElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Reject(TokenValidatedContext context, ILogger? logger, string reason)
    {
        if (logger is not null)
        {
            SecurityLogEvents.DpopProofRejected(logger, reason);
        }

        context.Fail($"DPoP proof validation failed: {reason}");
    }
}
