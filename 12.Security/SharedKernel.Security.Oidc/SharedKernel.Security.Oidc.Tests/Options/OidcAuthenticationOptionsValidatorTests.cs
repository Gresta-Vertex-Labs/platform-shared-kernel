using Microsoft.Extensions.Options;
using SharedKernel.Security.Oidc.Authentication;
using SharedKernel.Security.Oidc.Options;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Options;

public sealed class OidcAuthenticationOptionsValidatorTests
{
    public static TheoryData<string, Action<OidcAuthenticationOptions>, string> InvalidOptions => new()
    {
        { "empty authority", o => o.Authority = string.Empty, "Authority must be an absolute URL." },
        { "relative authority", o => o.Authority = "/issuer", "Authority must be an absolute URL." },
        { "http authority", o => o.Authority = "http://issuer.example.test", "Authority must use https unless RequireHttpsMetadata is false." },
        { "no audiences", o => o.Audiences = [], "Audiences must contain at least one value and no empty values." },
        { "null audiences", o => o.Audiences = null!, "Audiences must contain at least one value and no empty values." },
        { "blank audience", o => o.Audiences = ["api://orders", " "], "Audiences must contain at least one value and no empty values." },
        { "blank issuer", o => o.ValidIssuers = [string.Empty], "ValidIssuers must not contain empty values." },
        { "blank token type", o => o.ValidTokenTypes = [" "], "ValidTokenTypes must not contain empty values." },
        { "null algorithms", o => o.ValidAlgorithms = null!, "ValidAlgorithms must not be null." },
        { "HS256", o => o.ValidAlgorithms = ["HS256"], "ValidAlgorithms contains 'HS256'." },
        { "none", o => o.ValidAlgorithms = ["none"], "ValidAlgorithms contains 'none'." },
        { "unknown", o => o.ValidAlgorithms = ["RS256", "EdDSA"], "ValidAlgorithms contains 'EdDSA'." },
        { "lower-case", o => o.ValidAlgorithms = ["rs256"], "ValidAlgorithms contains 'rs256'." },
        { "negative skew", o => o.ClockSkew = TimeSpan.FromSeconds(-1), "ClockSkew must be between" },
        { "large skew", o => o.ClockSkew = TimeSpan.FromMinutes(5).Add(TimeSpan.FromTicks(1)), "ClockSkew must be between" },
        { "null claims", o => o.Claims = null!, "Claims must not be null." },
        { "blank subject", o => o.Claims.SubjectClaimType = " ", "Claims.SubjectClaimType must not be empty." },
        { "blank name", o => o.Claims.NameClaimType = string.Empty, "Claims.NameClaimType must not be empty." },
        { "blank email", o => o.Claims.EmailClaimType = string.Empty, "Claims.EmailClaimType must not be empty." },
        { "blank role", o => o.Claims.RoleClaimType = string.Empty, "Claims.RoleClaimType must not be empty." },
        { "blank tenant", o => o.Claims.TenantClaimType = string.Empty, "Claims.TenantClaimType must not be empty." },
        { "blank amr", o => o.Claims.AuthenticationMethodClaimType = string.Empty, "Claims.AuthenticationMethodClaimType must not be empty." },
        { "blank acr", o => o.Claims.AuthContextClassReferenceClaimType = string.Empty, "Claims.AuthContextClassReferenceClaimType must not be empty." },
        { "blank auth_time", o => o.Claims.AuthTimeClaimType = null!, "Claims.AuthTimeClaimType must not be empty." },
        { "null permissions", o => o.Claims.PermissionClaimTypes = null!, "Claims.PermissionClaimTypes must not be null or contain empty values." },
        { "blank permission", o => o.Claims.PermissionClaimTypes = ["scope", ""], "Claims.PermissionClaimTypes must not be null or contain empty values." },
        { "blank client id", o => o.Claims.ClientIdClaimTypes = [" "], "Claims.ClientIdClaimTypes must not be null or contain empty values." },
        { "blank session id", o => o.Claims.SessionIdClaimTypes = [""], "Claims.SessionIdClaimTypes must not be null or contain empty values." },
        { "null app claims", o => o.Claims.ApplicationTokenClaims = null!, "Claims.ApplicationTokenClaims must not be null or contain empty types or values." },
        { "blank app claim type", o => o.Claims.ApplicationTokenClaims[" "] = "app", "Claims.ApplicationTokenClaims must not be null or contain empty types or values." },
        { "empty app claim value", o => o.Claims.ApplicationTokenClaims["idtyp"] = string.Empty, "Claims.ApplicationTokenClaims must not be null or contain empty types or values." },
        { "null dpop", o => o.Dpop = null!, "Dpop must not be null." },
        { "undefined dpop mode", o => o.Dpop.Mode = (DpopMode)7, "Dpop.Mode '7' is not defined." },
        { "null dpop algorithms", o => o.Dpop.ValidAlgorithms = null!, "Dpop.ValidAlgorithms must not be null." },
        { "HS256 dpop", o => o.Dpop.ValidAlgorithms = ["HS256"], "Dpop.ValidAlgorithms contains 'HS256'." },
        { "short proof lifetime", o => o.Dpop.ProofLifetime = TimeSpan.FromMilliseconds(999), "Dpop.ProofLifetime must be between" },
        { "long proof lifetime", o => o.Dpop.ProofLifetime = TimeSpan.FromMinutes(6), "Dpop.ProofLifetime must be between" },
        { "negative dpop skew", o => o.Dpop.ClockSkew = TimeSpan.FromSeconds(-1), "Dpop.ClockSkew must be between" },
        { "large dpop skew", o => o.Dpop.ClockSkew = TimeSpan.FromSeconds(61), "Dpop.ClockSkew must be between" },
        { "short nonce lifetime", o => o.Dpop.NonceLifetime = TimeSpan.FromSeconds(59), "Dpop.NonceLifetime must be between" },
        { "long nonce lifetime", o => o.Dpop.NonceLifetime = TimeSpan.FromMinutes(61), "Dpop.NonceLifetime must be between" },
        { "null revocation", o => o.Revocation = null!, "Revocation must not be null." },
        { "negative revocation cache", o => o.Revocation.NotRevokedCacheDuration = TimeSpan.FromSeconds(-1), "Revocation.NotRevokedCacheDuration must be between" },
        { "long revocation cache", o => o.Revocation.NotRevokedCacheDuration = TimeSpan.FromMinutes(6), "Revocation.NotRevokedCacheDuration must be between" },
    };

    [Fact]
    public void Validate_MinimalValidOptions_Succeeds()
    {
        ValidateOptionsResult result = Validate(Valid());

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Validate_FullyConfiguredValidOptions_Succeeds()
    {
        OidcAuthenticationOptions options = Valid();
        options.ValidIssuers = ["https://issuer.example.test"];
        options.ValidTokenTypes = ["at+jwt"];
        options.ValidAlgorithms = ["RS256", "RS384", "RS512", "PS256", "PS384", "PS512", "ES256", "ES384", "ES512"];
        options.ClockSkew = TimeSpan.FromMinutes(5);
        options.Claims.PermissionClaimTypes = ["scope"];
        options.Claims.ApplicationTokenClaims["idtyp"] = "app";
        options.Dpop.Mode = DpopMode.Required;
        options.Dpop.ValidAlgorithms = ["ES256"];
        options.Dpop.ProofLifetime = TimeSpan.FromSeconds(1);
        options.Dpop.ClockSkew = TimeSpan.FromMinutes(1);
        options.Dpop.NonceLifetime = TimeSpan.FromHours(1);
        options.Revocation.NotRevokedCacheDuration = TimeSpan.Zero;

        ValidateOptionsResult result = new OidcAuthenticationOptionsValidator(new DpopRegistration())
            .Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData(DpopMode.Required, false)]
    [InlineData(DpopMode.Allowed, true)]
    public void Validate_DpopSettingsNeedingAddDpopWithoutIt_Fails(DpopMode mode, bool requireNonce)
    {
        OidcAuthenticationOptions options = Valid();
        options.Dpop.Mode = mode;
        options.Dpop.RequireNonce = requireNonce;

        ValidateOptionsResult result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("need AddDpop", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_HttpAuthorityWithoutHttpsMetadata_Succeeds()
    {
        OidcAuthenticationOptions options = Valid();
        options.Authority = "http://localhost:8080/realms/dev";
        options.RequireHttpsMetadata = false;

        Assert.True(Validate(options).Succeeded);
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Validate_InvalidOptions_Fails(string description, Action<OidcAuthenticationOptions> mutate, string expectedFailure)
    {
        OidcAuthenticationOptions options = Valid();
        mutate(options);

        ValidateOptionsResult result = Validate(options);

        Assert.True(result.Failed, description);
        Assert.Contains(result.Failures!, failure => failure.StartsWith(expectedFailure, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SeveralProblems_ReportsEach()
    {
        var options = new OidcAuthenticationOptions { ValidAlgorithms = ["HS256", "none"] };

        ValidateOptionsResult result = Validate(options);

        Assert.Equal(4, result.Failures!.Count());
    }

    [Fact]
    public void Validate_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OidcAuthenticationOptionsValidator().Validate(null, null!));
    }

    private static ValidateOptionsResult Validate(OidcAuthenticationOptions options) =>
        new OidcAuthenticationOptionsValidator().Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

    private static OidcAuthenticationOptions Valid() => new()
    {
        Authority = "https://issuer.example.test",
        Audiences = ["api://orders"],
    };
}
