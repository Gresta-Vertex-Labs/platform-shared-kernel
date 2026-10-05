using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Options;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Dpop;

public sealed class DpopProofValidatorTests : IDisposable
{
    private const string AccessToken = "dpop-test-access-token";

    private static readonly DateTimeOffset Now = HandmadeProof.DefaultIssuedAt;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly OidcAuthenticationOptions _options = new();
    private readonly FakeClock _clock = new(Now);
    private readonly RecordingReplayCache _replayCache = new();
    private readonly DpopNonceService _nonces;

    public DpopProofValidatorTests()
    {
        _nonces = new DpopNonceService(
            new EphemeralDataProtectionProvider(),
            _clock,
            new StaticOptionsMonitor<OidcAuthenticationOptions>(_options));
    }

    public void Dispose() => _key.Dispose();

    [Fact]
    public async Task ValidateAsync_ProofWithoutKid_Succeeds()
    {
        DpopTestProof proof = new DpopTestProofBuilder().WithKey(_key).WithAccessToken(AccessToken).Build();

        DpopResult result = await ValidateAsync(proof.ProofJwt, proof.JwkThumbprint);

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_ValidProof_Succeeds()
    {
        DpopResult result = await ValidateAsync(Proof().Build());

        Assert.True(result.IsValid, result.Reason);
        Assert.Null(result.Error);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_ValidProof_RecordsHashedProofIdUntilLifetimePlusSkew()
    {
        await ValidateAsync(Proof().WithJti("jti-visible").Build());

        (string proofId, DateTimeOffset expiresAt) = Assert.Single(_replayCache.Entries);
        Assert.Equal(Now + TimeSpan.FromSeconds(65), expiresAt);
        Assert.Equal(43, proofId.Length);
        Assert.DoesNotContain("jti-visible", proofId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_NoProofHeader_FailsMissingProof()
    {
        DpopResult result = await ValidateAsync(proofs: []);

        AssertFailure(result, "MissingProof");
    }

    [Fact]
    public async Task ValidateAsync_BlankProofHeader_FailsMissingProof()
    {
        DpopResult result = await ValidateAsync("  ");

        AssertFailure(result, "MissingProof");
    }

    [Fact]
    public async Task ValidateAsync_TwoProofHeaders_FailsMultipleProofs()
    {
        string proof = Proof().Build();

        DpopResult result = await ValidateAsync(proofs: [proof, proof]);

        AssertFailure(result, "MultipleProofs");
        Assert.Empty(_replayCache.Entries);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task ValidateAsync_MalformedProof_FailsMalformedProof(string proof)
    {
        DpopResult result = await ValidateAsync(proof);

        AssertFailure(result, "MalformedProof");
    }

    [Fact]
    public async Task ValidateAsync_SymmetricAlgorithm_FailsAlgorithmNotAllowed()
    {
        DpopResult result = await ValidateAsync(HandmadeProof.SignHs256(AccessToken));

        AssertFailure(result, "AlgorithmNotAllowed");
    }

    [Fact]
    public async Task ValidateAsync_AlgNone_FailsAlgorithmNotAllowed()
    {
        DpopResult result = await ValidateAsync(HandmadeProof.Unsigned(_key, AccessToken));

        AssertFailure(result, "AlgorithmNotAllowed");
    }

    [Fact]
    public async Task ValidateAsync_HeaderAlgDiffersFromKey_IsRejected()
    {
        DpopResult result = await ValidateAsync(Proof().WithHeader("alg", "RS256").Build());

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ConfiguredAlgorithms_ReplaceDefaults()
    {
        _options.Dpop.ValidAlgorithms = ["PS256"];

        DpopResult result = await ValidateAsync(Proof().Build());

        AssertFailure(result, "AlgorithmNotAllowed");
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("at+jwt")]
    public async Task ValidateAsync_WrongType_FailsInvalidType(string type)
    {
        DpopResult result = await ValidateAsync(Proof().WithHeader("typ", type).Build());

        AssertFailure(result, "InvalidType");
    }

    [Fact]
    public async Task ValidateAsync_TypeInOtherCase_Succeeds()
    {
        DpopResult result = await ValidateAsync(Proof().WithHeader("typ", "DPoP+JWT").Build());

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_NoJwkHeader_FailsMissingKey()
    {
        DpopResult result = await ValidateAsync(Proof().WithoutJwk().Build());

        AssertFailure(result, "MissingKey");
    }

    [Fact]
    public async Task ValidateAsync_JwkNotAnObject_FailsMissingKey()
    {
        DpopResult result = await ValidateAsync(Proof().WithHeader("jwk", "kid-1").Build());

        AssertFailure(result, "MissingKey");
    }

    [Theory]
    [InlineData("d")]
    [InlineData("p")]
    [InlineData("q")]
    [InlineData("dp")]
    [InlineData("dq")]
    [InlineData("qi")]
    [InlineData("oth")]
    [InlineData("k")]
    public async Task ValidateAsync_JwkWithPrivateMember_FailsPrivateKeyInProof(string member)
    {
        DpopResult result = await ValidateAsync(Proof().WithJwkMember(member, "AQAB").Build());

        AssertFailure(result, "PrivateKeyInProof");
    }

    [Fact]
    public async Task ValidateAsync_JwkWithRealPrivateKey_FailsPrivateKeyInProof()
    {
        ECParameters parameters = _key.ExportParameters(includePrivateParameters: true);

        DpopResult result = await ValidateAsync(Proof().WithJwkMember("d", Base64Url.EncodeToString(parameters.D)).Build());

        AssertFailure(result, "PrivateKeyInProof");
    }

    [Fact]
    public async Task ValidateAsync_RsaKeyBelow2048Bits_FailsWeakKey()
    {
        using var rsa = RSA.Create(1024);

        DpopResult result = await ValidateAsync(HandmadeProof.SignRs256(rsa, AccessToken));

        AssertFailure(result, "WeakKey");
    }

    [Fact]
    public async Task ValidateAsync_Rsa2048Key_Succeeds()
    {
        using var rsa = RSA.Create(2048);
        RSAParameters parameters = rsa.ExportParameters(includePrivateParameters: false);
        var jwk = new JsonWebKey { Kty = "RSA", N = Base64Url.EncodeToString(parameters.Modulus), E = Base64Url.EncodeToString(parameters.Exponent) };

        DpopResult result = await ValidateAsync(HandmadeProof.SignRs256(rsa, AccessToken), Base64Url.EncodeToString(jwk.ComputeJwkThumbprint()));

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_UnsupportedKeyType_FailsUnsupportedKeyType()
    {
        string proof = Proof()
            .WithHeader("jwk", new Dictionary<string, object> { ["kty"] = "OKP", ["crv"] = "Ed25519", ["x"] = "11qYAYKxCrfVS_7TyWQHOg7hcvPapiMlrwIaaPcHURo" })
            .Build();

        DpopResult result = await ValidateAsync(proof);

        AssertFailure(result, "UnsupportedKeyType");
    }

    [Fact]
    public async Task ValidateAsync_SignedByOtherKeyThanJwk_FailsInvalidSignature()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        DpopResult result = await ValidateAsync(Proof().SignedBy(other).Build());

        AssertFailure(result, "InvalidSignature");
    }

    [Fact]
    public async Task ValidateAsync_TamperedPayload_FailsInvalidSignature()
    {
        string[] original = Proof().Build().Split('.');
        string[] other = Proof().WithMethod("DELETE").Build().Split('.');

        DpopResult result = await ValidateAsync($"{original[0]}.{other[1]}.{original[2]}");

        AssertFailure(result, "InvalidSignature");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("post")]
    public async Task ValidateAsync_MethodDiffers_FailsMethodMismatch(string htm)
    {
        DpopResult result = await ValidateAsync(Proof().WithMethod(htm).Build());

        AssertFailure(result, "MethodMismatch");
    }

    [Fact]
    public async Task ValidateAsync_MissingMethod_FailsMethodMismatch()
    {
        DpopResult result = await ValidateAsync(Proof().WithoutClaim("htm").Build());

        AssertFailure(result, "MethodMismatch");
    }

    [Theory]
    [InlineData("https://api.example.test/other")]
    [InlineData("https://api.example.test/Resource")]
    [InlineData("https://api.example.test/resource/")]
    [InlineData("https://attacker.example.test/resource")]
    [InlineData("http://api.example.test/resource")]
    [InlineData("https://api.example.test:8443/resource")]
    [InlineData("not a uri")]
    public async Task ValidateAsync_UriDiffers_FailsUriMismatch(string htu)
    {
        DpopResult result = await ValidateAsync(Proof().WithUri(htu).Build());

        AssertFailure(result, "UriMismatch");
    }

    [Fact]
    public async Task ValidateAsync_MissingUri_FailsUriMismatch()
    {
        DpopResult result = await ValidateAsync(Proof().WithoutClaim("htu").Build());

        AssertFailure(result, "UriMismatch");
    }

    [Theory]
    [InlineData("HTTPS://API.EXAMPLE.TEST/resource")]
    [InlineData("https://api.example.test:443/resource")]
    [InlineData("https://api.example.test/resource?page=2")]
    [InlineData("https://api.example.test/resource#fragment")]
    public async Task ValidateAsync_UriEquivalentAfterNormalization_Succeeds(string htu)
    {
        DpopResult result = await ValidateAsync(Proof().WithUri(htu).Build());

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_RequestHasQueryString_IgnoresQuery()
    {
        DpopResult result = await ValidateAsync(Proof().Build(), configure: request => request.QueryString = new QueryString("?page=3"));

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_RequestWithPathBase_ComparesFullPath()
    {
        string proof = Proof().WithUri("https://api.example.test/orders/resource").Build();

        DpopResult result = await ValidateAsync(proof, configure: request => request.PathBase = "/orders");

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_MissingIssuedAt_FailsMissingIssuedAt()
    {
        DpopResult result = await ValidateAsync(Proof().WithoutClaim("iat").Build());

        AssertFailure(result, "MissingIssuedAt");
    }

    [Theory]
    [InlineData(-66)]
    [InlineData(6)]
    [InlineData(-3600)]
    public async Task ValidateAsync_IssuedAtOutsideWindow_FailsExpired(int offsetSeconds)
    {
        DpopResult result = await ValidateAsync(Proof().WithIssuedAt(Now.AddSeconds(offsetSeconds)).Build());

        AssertFailure(result, "Expired");
    }

    [Theory]
    [InlineData(-65)]
    [InlineData(0)]
    [InlineData(5)]
    public async Task ValidateAsync_IssuedAtInsideWindow_Succeeds(int offsetSeconds)
    {
        DpopResult result = await ValidateAsync(Proof().WithIssuedAt(Now.AddSeconds(offsetSeconds)).Build());

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_ConfiguredProofLifetime_IsUsed()
    {
        _options.Dpop.ProofLifetime = TimeSpan.FromSeconds(10);

        DpopResult result = await ValidateAsync(Proof().WithIssuedAt(Now.AddSeconds(-16)).Build());

        AssertFailure(result, "Expired");
    }

    [Fact]
    public async Task ValidateAsync_MissingJti_FailsInvalidJti()
    {
        DpopResult result = await ValidateAsync(Proof().WithoutClaim("jti").Build());

        AssertFailure(result, "InvalidJti");
    }

    [Fact]
    public async Task ValidateAsync_EmptyJti_FailsInvalidJti()
    {
        DpopResult result = await ValidateAsync(Proof().WithJti(string.Empty).Build());

        AssertFailure(result, "InvalidJti");
    }

    [Fact]
    public async Task ValidateAsync_JtiLongerThan256_FailsInvalidJti()
    {
        DpopResult result = await ValidateAsync(Proof().WithJti(new string('j', 257)).Build());

        AssertFailure(result, "InvalidJti");
    }

    [Fact]
    public async Task ValidateAsync_Jti256Characters_Succeeds()
    {
        DpopResult result = await ValidateAsync(Proof().WithJti(new string('j', 256)).Build());

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_KeyDiffersFromConfirmation_FailsKeyMismatch()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        DpopResult result = await ValidateAsync(Proof().Build(), HandmadeProof.Thumbprint(other));

        AssertFailure(result, "KeyMismatch");
    }

    [Fact]
    public async Task ValidateAsync_AthForOtherToken_FailsAccessTokenHashMismatch()
    {
        DpopResult result = await ValidateAsync(Proof().WithClaim("ath", HandmadeProof.Ath(AccessToken + "-tampered")).Build());

        AssertFailure(result, "AccessTokenHashMismatch");
    }

    [Fact]
    public async Task ValidateAsync_MissingAth_FailsAccessTokenHashMismatch()
    {
        DpopResult result = await ValidateAsync(Proof().WithoutClaim("ath").Build());

        AssertFailure(result, "AccessTokenHashMismatch");
    }

    [Theory]
    [InlineData("not-the-hash")]
    [InlineData("")]
    public async Task ValidateAsync_MalformedAth_FailsAccessTokenHashMismatch(string ath)
    {
        DpopResult result = await ValidateAsync(Proof().WithClaim("ath", ath).Build());

        AssertFailure(result, "AccessTokenHashMismatch");
    }

    [Fact]
    public async Task ValidateAsync_ProofForDifferentAccessToken_FailsAccessTokenHashMismatch()
    {
        DpopResult result = await ValidateAsync(Proof().WithAccessToken("another-token").Build());

        AssertFailure(result, "AccessTokenHashMismatch");
    }

    [Fact]
    public async Task ValidateAsync_NonceRequiredButMissing_FailsWithUseNonce()
    {
        _options.Dpop.RequireNonce = true;

        DpopResult result = await ValidateAsync(Proof().Build());

        Assert.False(result.IsValid);
        Assert.Equal("use_dpop_nonce", result.Error);
        Assert.Equal("NonceInvalid", result.Reason);
        Assert.Empty(_replayCache.Entries);
    }

    [Fact]
    public async Task ValidateAsync_NonceRequiredAndIssued_Succeeds()
    {
        _options.Dpop.RequireNonce = true;

        DpopResult result = await ValidateAsync(Proof().WithNonce(_nonces.Create()).Build());

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_NonceNotRequired_IgnoresInvalidNonce()
    {
        DpopResult result = await ValidateAsync(Proof().WithNonce("forged").Build());

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_NonceFromOtherKeyRing_FailsWithUseNonce()
    {
        _options.Dpop.RequireNonce = true;
        var otherService = new DpopNonceService(new EphemeralDataProtectionProvider(), _clock, new StaticOptionsMonitor<OidcAuthenticationOptions>(_options));

        DpopResult result = await ValidateAsync(Proof().WithNonce(otherService.Create()).Build());

        Assert.Equal("use_dpop_nonce", result.Error);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredNonce_FailsWithUseNonce()
    {
        _options.Dpop.RequireNonce = true;
        string nonce = _nonces.Create();
        _clock.Advance(TimeSpan.FromMinutes(6));

        DpopResult result = await ValidateAsync(Proof().WithIssuedAt(_clock.UtcNow).WithNonce(nonce).Build());

        Assert.Equal("use_dpop_nonce", result.Error);
    }

    [Fact]
    public async Task ValidateAsync_SameProofTwice_FailsReplayed()
    {
        string proof = Proof().Build();

        DpopResult first = await ValidateAsync(proof);
        DpopResult second = await ValidateAsync(proof);

        Assert.True(first.IsValid, first.Reason);
        AssertFailure(second, "Replayed");
    }

    [Fact]
    public async Task ValidateAsync_SameJtiFromDifferentKeys_AreIndependent()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        DpopResult first = await ValidateAsync(Proof().WithJti("shared").Build());
        DpopResult second = await ValidateAsync(new HandmadeProof(other, AccessToken).WithJti("shared").Build(), HandmadeProof.Thumbprint(other));

        Assert.True(first.IsValid, first.Reason);
        Assert.True(second.IsValid, second.Reason);
    }

    [Fact]
    public async Task ValidateAsync_ReplayCacheThrows_FailsClosed()
    {
        var validator = new DpopProofValidator(new ThrowingReplayCache(), _nonces);
        HttpContext context = CreateContext([Proof().Build()]);

        DpopResult result = await validator.ValidateAsync(context, AccessToken, HandmadeProof.Thumbprint(_key), _options.Dpop, Now);

        AssertFailure(result, "ReplayCacheUnavailable");
    }

    private static void AssertFailure(DpopResult result, string reason)
    {
        Assert.False(result.IsValid);
        Assert.Equal("invalid_dpop_proof", result.Error);
        Assert.Equal(reason, result.Reason);
    }

    private HandmadeProof Proof() => new(_key, AccessToken);

    private Task<DpopResult> ValidateAsync(string proof, string? expectedThumbprint = null, Action<HttpRequest>? configure = null) =>
        ValidateAsync([proof], expectedThumbprint, configure);

    private async Task<DpopResult> ValidateAsync(string[] proofs, string? expectedThumbprint = null, Action<HttpRequest>? configure = null)
    {
        HttpContext context = CreateContext(proofs);
        configure?.Invoke(context.Request);
        var validator = new DpopProofValidator(_replayCache, _nonces);
        return await validator.ValidateAsync(context, AccessToken, expectedThumbprint ?? HandmadeProof.Thumbprint(_key), _options.Dpop, _clock.UtcNow);
    }

    private static DefaultHttpContext CreateContext(string[] proofs)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.example.test");
        context.Request.Path = "/resource";
        if (proofs.Length > 0)
        {
            context.Request.Headers["DPoP"] = proofs;
        }

        return context;
    }

    private sealed class RecordingReplayCache : IDpopReplayCache
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);

        public IReadOnlyList<(string ProofId, DateTimeOffset ExpiresAt)> Entries => [.. _entries.Select(entry => (entry.Key, entry.Value))];

        public ValueTask<bool> TryAddAsync(string proofId, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_entries.TryAdd(proofId, expiresAt));
    }

    private sealed class ThrowingReplayCache : IDpopReplayCache
    {
        public ValueTask<bool> TryAddAsync(string proofId, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Replay store unavailable.");
    }
}
