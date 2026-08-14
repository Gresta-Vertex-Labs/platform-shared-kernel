using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Security.Oidc.Dpop;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Dpop;

public sealed class DpopProofValidatorTests
{
    private const string RequestMethod = "GET";
    private const string RequestUrl = "https://api.example.com/orders";

    private sealed class TestReplayCache : IDpopProofReplayCache
    {
        private readonly HashSet<string> _seen = [];

        public bool RejectAll { get; set; }

        public Task<bool> TryConsumeAsync(string jti, DateTimeOffset proofExpiresAt, CancellationToken ct) =>
            Task.FromResult(!RejectAll && _seen.Add(jti));
    }

    private static (ECDsaSecurityKey Key, string Kty, string Crv, string X, string Y) CreateKeyMaterial()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(ecdsa));
        return (new ECDsaSecurityKey(ecdsa), jwk.Kty, jwk.Crv, jwk.X, jwk.Y);
    }

    private static string ComputeExpectedJkt(string kty, string crv, string x, string y)
    {
        var publicJwk = new JsonWebKey { Kty = kty, Crv = crv, X = x, Y = y };
        return Base64UrlEncoder.Encode(publicJwk.ComputeJwkThumbprint());
    }

    private static string BuildProof(
        ECDsaSecurityKey key,
        string kty,
        string crv,
        string x,
        string y,
        string? htm = RequestMethod,
        string? htu = RequestUrl,
        long? iatOverride = null,
        string? jti = null,
        string? typOverride = "dpop+jwt")
    {
        var handler = new JsonWebTokenHandler();
        var claims = new Dictionary<string, object>();
        if (htm is not null)
        {
            claims["htm"] = htm;
        }

        if (htu is not null)
        {
            claims["htu"] = htu;
        }

        claims["iat"] = iatOverride ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        claims["jti"] = jti ?? Guid.NewGuid().ToString("N");

        var headerClaims = new Dictionary<string, object>();
        if (typOverride is not null)
        {
            headerClaims["typ"] = typOverride;
        }

        headerClaims["jwk"] = new Dictionary<string, object>
        {
            ["kty"] = kty,
            ["crv"] = crv,
            ["x"] = x,
            ["y"] = y,
        };

        var descriptor = new SecurityTokenDescriptor
        {
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256),
            Claims = claims,
            AdditionalHeaderClaims = headerClaims,
        };

        return handler.CreateToken(descriptor);
    }

    private static TokenValidatedContext BuildContext(
        string? dpopHeaderValue,
        string? cnfJkt,
        IDpopProofReplayCache replayCache)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = RequestMethod;
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        httpContext.Request.Path = "/orders";

        if (dpopHeaderValue is not null)
        {
            httpContext.Request.Headers["DPoP"] = dpopHeaderValue;
        }

        var services = new ServiceCollection();
        services.AddSingleton(replayCache);
        services.AddOptions<DpopOptions>();
        httpContext.RequestServices = services.BuildServiceProvider();

        var claims = new List<Claim>();
        if (cnfJkt is not null)
        {
            claims.Add(new Claim("cnf", JsonSerializer.Serialize(new { jkt = cnfJkt })));
        }

        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var context = new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
        };

        return context;
    }

    [Fact]
    public async Task ValidProof_WithMatchingJkt_UnseenJti_ResolvesIsSenderConstrained()
    {
        var (key, kty, crv, x, y) = CreateKeyMaterial();
        var jkt = ComputeExpectedJkt(kty, crv, x, y);
        var proof = BuildProof(key, kty, crv, x, y);
        var context = BuildContext(proof, jkt, new TestReplayCache());

        await DpopProofValidator.ValidateAsync(context);

        Assert.Null(context.Result);
        var identity = Assert.IsType<ClaimsIdentity>(context.Principal!.Identity);
        Assert.Contains(identity.Claims, c => c.Type == "sk_dpop_bound" && string.Equals(c.Value, bool.TrueString, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingProofHeader_Rejects()
    {
        var context = BuildContext(dpopHeaderValue: null, cnfJkt: "irrelevant", new TestReplayCache());

        await DpopProofValidator.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task MalformedProof_Rejects()
    {
        var context = BuildContext(dpopHeaderValue: "not-a-jwt", cnfJkt: "irrelevant", new TestReplayCache());

        await DpopProofValidator.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task WrongTyp_Rejects()
    {
        var (key, kty, crv, x, y) = CreateKeyMaterial();
        var jkt = ComputeExpectedJkt(kty, crv, x, y);
        var proof = BuildProof(key, kty, crv, x, y, typOverride: "jwt");
        var context = BuildContext(proof, jkt, new TestReplayCache());

        await DpopProofValidator.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task HtmMismatch_Rejects()
    {
        var (key, kty, crv, x, y) = CreateKeyMaterial();
        var jkt = ComputeExpectedJkt(kty, crv, x, y);
        var proof = BuildProof(key, kty, crv, x, y, htm: "POST");
        var context = BuildContext(proof, jkt, new TestReplayCache());

        await DpopProofValidator.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task HtuMismatch_Rejects()
    {
        var (key, kty, crv, x, y) = CreateKeyMaterial();
        var jkt = ComputeExpectedJkt(kty, crv, x, y);
        var proof = BuildProof(key, kty, crv, x, y, htu: "https://api.example.com/other");
        var context = BuildContext(proof, jkt, new TestReplayCache());

        await DpopProofValidator.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task ExpiredIat_Rejects()
    {
        var (key, kty, crv, x, y) = CreateKeyMaterial();
        var jkt = ComputeExpectedJkt(kty, crv, x, y);
        var staleIat = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var proof = BuildProof(key, kty, crv, x, y, iatOverride: staleIat);
        var context = BuildContext(proof, jkt, new TestReplayCache());

        await DpopProofValidator.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task JktMismatch_Rejects()
    {
        var (key, kty, crv, x, y) = CreateKeyMaterial();
        var proof = BuildProof(key, kty, crv, x, y);
        // cnf.jkt deliberately does not match the proof's embedded jwk thumbprint.
        var context = BuildContext(proof, cnfJkt: "wrong-thumbprint-value", new TestReplayCache());

        await DpopProofValidator.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task MissingConfirmationClaim_Rejects()
    {
        var (key, kty, crv, x, y) = CreateKeyMaterial();
        var proof = BuildProof(key, kty, crv, x, y);
        var context = BuildContext(proof, cnfJkt: null, new TestReplayCache());

        await DpopProofValidator.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task ReplayedJti_Rejects_OnSecondUse()
    {
        var (key, kty, crv, x, y) = CreateKeyMaterial();
        var jkt = ComputeExpectedJkt(kty, crv, x, y);
        var jti = Guid.NewGuid().ToString("N");
        var replayCache = new TestReplayCache();

        var proof1 = BuildProof(key, kty, crv, x, y, jti: jti);
        var context1 = BuildContext(proof1, jkt, replayCache);
        await DpopProofValidator.ValidateAsync(context1);
        Assert.Null(context1.Result);

        var proof2 = BuildProof(key, kty, crv, x, y, jti: jti);
        var context2 = BuildContext(proof2, jkt, replayCache);
        await DpopProofValidator.ValidateAsync(context2);

        Assert.NotNull(context2.Result);
    }

    [Fact]
    public async Task TamperedSignature_Rejects()
    {
        var (key, kty, crv, x, y) = CreateKeyMaterial();
        var (otherKey, _, _, _, _) = CreateKeyMaterial();
        var jkt = ComputeExpectedJkt(kty, crv, x, y);
        // Sign with a DIFFERENT key than the one embedded in the jwk header — signature verification
        // against the embedded public key must fail.
        var proof = BuildProof(otherKey, kty, crv, x, y);
        var context = BuildContext(proof, jkt, new TestReplayCache());

        await DpopProofValidator.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }
}
